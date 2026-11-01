#Requires -Version 5.1
<#
.SYNOPSIS
Exports the local references needed to port Escape From Borderlands to SPT 4.1.6.
.EXAMPLE
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Collect-EFB-SPT41.ps1 -GameRoot 'D:\SPT 4.1.6'
.NOTES
Collector revision 1.2: verifies renamed client symbols and finds patched references.
Copies selected assemblies, public database files and optional EFB/APBS inputs.
Does not install, update or launch anything. Does not collect profiles or logs.
#>
[CmdletBinding()]
param(
    [string]$GameRoot,
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 does not reliably load the ZipArchive assembly
# when only FileSystem is requested. Load both before collecting any files.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

if ([string]::IsNullOrWhiteSpace($GameRoot)) {
    if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'EscapeFromTarkov.exe') -PathType Leaf) {
        $GameRoot = $PSScriptRoot
    } else {
        $GameRoot = Read-Host 'Enter your SPT 4.1.6 folder (the one containing EscapeFromTarkov.exe)'
    }
}
$GameRoot = (Resolve-Path -LiteralPath $GameRoot.Trim().Trim('"').Trim("'")).Path
$ManagedRoot = Join-Path $GameRoot 'EscapeFromTarkov_Data\Managed'
$BepCoreRoot = Join-Path $GameRoot 'BepInEx\core'
$PluginRoot = Join-Path $GameRoot 'BepInEx\plugins'
$ServerRoot = Join-Path $GameRoot 'SPT_Runtime'
$CorePath = Join-Path $ServerRoot 'SPTarkov.Server.Core.dll'
if (-not (Test-Path -LiteralPath $CorePath -PathType Leaf)) {
    $ServerRoot = Join-Path $GameRoot 'SPT'
    $CorePath = Join-Path $ServerRoot 'SPTarkov.Server.Core.dll'
}
if (-not (Test-Path -LiteralPath $CorePath -PathType Leaf)) {
    throw 'SPTarkov.Server.Core.dll was not found under SPT_Runtime or SPT. Select the installed SPT game folder.'
}
$CoreVersion = [Reflection.AssemblyName]::GetAssemblyName($CorePath).Version
if ($CoreVersion.Major -ne 4 -or $CoreVersion.Minor -ne 1 -or $CoreVersion.Build -ne 6) {
    throw "This port targets SPT 4.1.6. The selected server assembly is $CoreVersion. Select an SPT 4.1.6 installation."
}

# SPT's launcher patches the game assembly. A fresh Managed folder can still
# contain the original Unicode-obfuscated DLL, which cannot be used to compile
# against the renamed types referenced by the installed SPT client plugins.
# Read metadata with SPT's existing Cecil library; never load game assemblies.
$CecilPath = Join-Path $BepCoreRoot 'Mono.Cecil.dll'
if (-not (Test-Path -LiteralPath $CecilPath -PathType Leaf)) {
    throw 'BepInEx/core/Mono.Cecil.dll is missing. Select a complete SPT 4.1.6 installation.'
}
[void][Reflection.Assembly]::LoadFrom($CecilPath)

function Get-ClientAssemblySymbols([string]$Path) {
    $Reader = New-Object Mono.Cecil.ReaderParameters
    $Reader.ReadSymbols = $false
    $Assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Path, $Reader)
    try {
        $SessionType = $Assembly.MainModule.GetType('EFT.EftClientBackendSession')
        $DamageType = $Assembly.MainModule.GetType('EFT.Ballistics.DamageInfo')
        return [pscustomobject]@{
            source = $Path.Substring($GameRoot.Length + 1).Replace('\', '/')
            hasRenamedSession = ($null -ne $SessionType)
            hasRenamedDamageInfo = ($null -ne $DamageType)
            ready = ($null -ne $SessionType -and $null -ne $DamageType)
        }
    } finally { $Assembly.Dispose() }
}

$GameAssemblyPath = Join-Path $ManagedRoot 'Assembly-CSharp.dll'
$ClientAssemblyCandidates = New-Object 'System.Collections.Generic.List[object]'
$CandidatePaths = @{}
$SearchRoots = @(
    $ManagedRoot,
    (Join-Path $GameRoot 'BepInEx\cache'),
    (Join-Path $GameRoot 'BepInEx\DumpedAssemblies'),
    (Join-Path $GameRoot 'SPT_Data'),
    (Join-Path $ServerRoot 'SPT_Data')
)
foreach ($SearchRoot in $SearchRoots) {
    if (-not (Test-Path -LiteralPath $SearchRoot -PathType Container)) { continue }
    foreach ($Candidate in @(Get-ChildItem -LiteralPath $SearchRoot -Recurse -File -Filter 'Assembly-CSharp.dll')) {
        if ($CandidatePaths.ContainsKey($Candidate.FullName)) { continue }
        $CandidatePaths[$Candidate.FullName] = $true
        try { [void]$ClientAssemblyCandidates.Add((Get-ClientAssemblySymbols $Candidate.FullName)) }
        catch { Write-Warning ("Could not inspect client assembly {0}: {1}" -f $Candidate.Name, $_.Exception.Message) }
    }
}
$SelectedSymbols = $ClientAssemblyCandidates | Where-Object { $_.ready } | Select-Object -First 1
if ($null -ne $SelectedSymbols) {
    $ClientAssemblyPath = Join-Path $GameRoot $SelectedSymbols.source
    Write-Host ("Renamed SPT client references found: {0}" -f $SelectedSymbols.source) -ForegroundColor Green
} else {
    $ClientAssemblyPath = $GameAssemblyPath
    Write-Warning 'The game DLL still has original obfuscated names. Start SPT 4.1.6 through its launcher, reach the main menu, close the game, and run this collector again. This export will include patch inputs for diagnosis.'
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $DesktopDirectory = [Environment]::GetFolderPath('Desktop')
    if ([string]::IsNullOrWhiteSpace($DesktopDirectory)) { $DesktopDirectory = $PSScriptRoot }
    $OutputDirectory = Join-Path $DesktopDirectory 'EFB-Port-Exports'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$ExportId = [Guid]::NewGuid().ToString('N')
$Stage = Join-Path ([IO.Path]::GetTempPath()) ("EFB-SPT41-" + $ExportId)
$Stamp = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'
$ZipPath = Join-Path $OutputDirectory ("EFB_SPT-4.1.6_References_{0}_{1}.zip" -f $Stamp, $ExportId.Substring(0, 8))
$Files = New-Object 'System.Collections.Generic.List[object]'
$MissingOptional = New-Object 'System.Collections.Generic.List[string]'
$Destinations = @{}
$Completed = $false

function Get-AssemblyVersion([string]$Path) {
    try { return [Reflection.AssemblyName]::GetAssemblyName($Path).Version.ToString() }
    catch { return $null }
}

function Add-ExportFile([string]$Source, [string]$Relative, [bool]$Required = $false) {
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        if ($Required) { throw "Required reference missing: $Source" }
        [void]$MissingOptional.Add($Relative)
        return
    }
    $Relative = $Relative.Replace('\', '/')
    if ($Destinations.ContainsKey($Relative)) {
        if ($Destinations[$Relative] -ne $Source) {
            throw "Multiple source files would map to $Relative. Remove stale duplicate SPT references before exporting."
        }
        return
    }
    $Destination = Join-Path $Stage $Relative
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Destination))
    [IO.File]::Copy($Source, $Destination, $false)
    $Destinations[$Relative] = $Source
    $Info = Get-Item -LiteralPath $Destination
    $AssemblyVersion = $null
    if ($Info.Extension -eq '.dll') { $AssemblyVersion = Get-AssemblyVersion $Source }
    [void]$Files.Add([pscustomobject]@{
        path = $Relative
        sizeBytes = $Info.Length
        sha256 = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
        assemblyVersion = $AssemblyVersion
    })
}

function Add-Reference([string]$Source, [string]$Relative, [bool]$Required = $false) {
    Add-ExportFile $Source $Relative $Required
    $XmlPath = [IO.Path]::ChangeExtension($Source, '.xml')
    if (Test-Path -LiteralPath $XmlPath -PathType Leaf) {
        Add-ExportFile $XmlPath ([IO.Path]::ChangeExtension($Relative, '.xml'))
    }
}

try {
    [void][IO.Directory]::CreateDirectory($Stage)
    Write-Host "Collecting references for SPT $CoreVersion..." -ForegroundColor Cyan
    Add-Reference $ClientAssemblyPath 'Client/Assembly-CSharp.dll' $true
    if ($ClientAssemblyPath -ne $GameAssemblyPath) {
        Add-Reference $GameAssemblyPath 'Client/Original/Assembly-CSharp.dll'
    }
    $ManagedRequired = @(
        'Comfort.dll', 'Newtonsoft.Json.dll',
        'UnityEngine.dll', 'UnityEngine.CoreModule.dll'
    )
    foreach ($Name in $ManagedRequired) {
        Add-Reference (Join-Path $ManagedRoot $Name) ("Client/" + $Name) $true
    }
    $ManagedOptional = @(
        'Assembly-CSharp-firstpass.dll', 'Comfort.Unity.dll', 'netstandard.dll',
        'mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Runtime.dll',
        'System.Runtime.Serialization.dll', 'System.ValueTuple.dll'
    )
    foreach ($Name in $ManagedOptional) {
        Add-Reference (Join-Path $ManagedRoot $Name) ("Client/" + $Name)
    }
    Get-ChildItem -LiteralPath $ManagedRoot -File -Filter 'UnityEngine*.dll' | ForEach-Object {
        Add-Reference $_.FullName ("Client/" + $_.Name)
    }
    foreach ($Name in @('BepInEx.dll', '0Harmony.dll')) {
        Add-Reference (Join-Path $BepCoreRoot $Name) ("Client/BepInEx/" + $Name) $true
    }
    Get-ChildItem -LiteralPath $BepCoreRoot -File -Filter '*.dll' | ForEach-Object {
        Add-Reference $_.FullName ("Client/BepInEx/" + $_.Name)
    }
    $ClientSptFiles = @(Get-ChildItem -LiteralPath $PluginRoot -Recurse -File -Filter '*.dll' | Where-Object {
        $_.Name -match '^spt[-.].*\.dll$'
    })
    if ($ClientSptFiles.Count -eq 0) { throw 'SPT client plugin assemblies were not found in BepInEx/plugins.' }
    foreach ($File in $ClientSptFiles) {
        Add-Reference $File.FullName ("Client/SPT/" + $File.Name) $true
    }
    $PatcherRoot = Join-Path $GameRoot 'BepInEx\patchers'
    if (Test-Path -LiteralPath $PatcherRoot -PathType Container) {
        foreach ($File in @(Get-ChildItem -LiteralPath $PatcherRoot -Recurse -File -Filter 'spt*.dll')) {
            $Relative = $File.FullName.Substring($PatcherRoot.Length + 1).Replace('\', '/')
            Add-Reference $File.FullName ("Client/SPT-Patchers/" + $Relative)
        }
    }
    # Include only assembly patch data, never launcher account/settings files.
    foreach ($PatchRoot in @((Join-Path $GameRoot 'SPT_Data\Launcher\Patches'), (Join-Path $ServerRoot 'SPT_Data\Launcher\Patches'))) {
        if (-not (Test-Path -LiteralPath $PatchRoot -PathType Container)) { continue }
        foreach ($File in @(Get-ChildItem -LiteralPath $PatchRoot -Recurse -File -Filter 'Assembly-CSharp.dll.bpf')) {
            $Relative = $File.FullName.Substring($GameRoot.Length + 1).Replace('\', '/')
            Add-ExportFile $File.FullName ("Client/LauncherPatches/" + $Relative)
        }
    }

    foreach ($Name in @('SPTarkov.Common.dll', 'SPTarkov.DI.dll', 'SPTarkov.Server.Core.dll')) {
        Add-Reference (Join-Path $ServerRoot $Name) ("Server/" + $Name) $true
    }
    Get-ChildItem -LiteralPath $ServerRoot -File -Filter 'SPT*.dll' | ForEach-Object {
        Add-Reference $_.FullName ("Server/" + $_.Name)
    }
    foreach ($Name in @(
        'SemanticVersioning.dll', '0Harmony.dll',
        'Microsoft.Extensions.DependencyInjection.dll',
        'Microsoft.Extensions.DependencyInjection.Abstractions.dll',
        'Microsoft.Extensions.Logging.Abstractions.dll',
        'SPT.Server.deps.json', 'SPT.Server.runtimeconfig.json'
    )) {
        Add-Reference (Join-Path $ServerRoot $Name) ("Server/" + $Name)
    }

    $DatabaseCandidates = @(
        (Join-Path $ServerRoot 'database'),
        (Join-Path $ServerRoot 'SPT_Data\database'),
        (Join-Path $GameRoot 'SPT_Data\database'),
        (Join-Path $ServerRoot 'data\database')
    )
    $DatabaseRoot = $null
    foreach ($Candidate in $DatabaseCandidates) {
        if (Test-Path -LiteralPath (Join-Path $Candidate 'templates\items.json') -PathType Leaf) {
            $DatabaseRoot = $Candidate
            break
        }
    }
    if ($null -ne $DatabaseRoot) {
        foreach ($Entry in @(
            @('templates\items.json', 'Database/items.json'),
            @('templates\handbook.json', 'Database/handbook.json'),
            @('templates\prices.json', 'Database/prices.json'),
            @('locales\global\en.json', 'Database/en.json'),
            @('globals.json', 'Database/globals.json')
        )) {
            Add-ExportFile (Join-Path $DatabaseRoot $Entry[0]) $Entry[1]
        }
    } else {
        [void]$MissingOptional.Add('Database/plaintext-database-not-found')
        Write-Warning 'Plaintext database files were not found. Assembly references will still be exported.'
    }

    $ServerModRoot = Join-Path $ServerRoot 'user\mods'
    $ServerMods = @()
    if (Test-Path -LiteralPath $ServerModRoot -PathType Container) {
        $ModFiles = @(Get-ChildItem -LiteralPath $ServerModRoot -Recurse -File -Filter '*.dll')
        $ServerMods = @($ModFiles | ForEach-Object {
            [pscustomobject]@{
                path = $_.FullName.Substring($ServerModRoot.Length + 1).Replace('\', '/')
                assemblyVersion = Get-AssemblyVersion $_.FullName
            }
        })
        foreach ($File in $ModFiles) {
            if ($File.Name -match '(?i)progressive.*bot|apbs|^EscapeFromBorderlands\.Server\.dll$') {
                $Relative = $File.FullName.Substring($ServerModRoot.Length + 1).Replace('\', '/')
                Add-Reference $File.FullName ("Compatibility/ServerMods/" + $Relative)
            }
        }
        Add-ExportFile (Join-Path $ServerModRoot 'EscapeFromBorderlands\config\config.json') 'Compatibility/EFB-config.json'
    }
    $ClientMods = @(Get-ChildItem -LiteralPath $PluginRoot -Recurse -File -Filter '*.dll' | ForEach-Object {
        [pscustomobject]@{
            path = $_.FullName.Substring($PluginRoot.Length + 1).Replace('\', '/')
            assemblyVersion = Get-AssemblyVersion $_.FullName
        }
    })
    $GameExe = Join-Path $GameRoot 'EscapeFromTarkov.exe'
    $GameVersion = $null
    if (Test-Path -LiteralPath $GameExe -PathType Leaf) {
        $GameVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($GameExe).FileVersion
    }
    $Manifest = [ordered]@{
        purpose = 'Escape From Borderlands SPT 4.1.6 port reference export'
        schemaVersion = 1
        collectorRevision = '1.2'
        createdUtc = [DateTime]::UtcNow.ToString('o')
        sptCoreVersion = $CoreVersion.ToString()
        eftFileVersion = $GameVersion
        serverDirectoryName = [IO.Path]::GetFileName($ServerRoot)
        sourceModVersion = '0.18.12'
        clientAssemblyReady = ($null -ne $SelectedSymbols)
        clientAssemblySource = $ClientAssemblyPath.Substring($GameRoot.Length + 1).Replace('\', '/')
        clientAssemblyCandidates = @($ClientAssemblyCandidates.ToArray())
        files = @($Files.ToArray())
        missingOptional = @($MissingOptional.ToArray())
        clientPluginInventory = $ClientMods
        serverModInventory = $ServerMods
    }
    $Utf8 = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText((Join-Path $Stage 'export-manifest.json'), ($Manifest | ConvertTo-Json -Depth 8), $Utf8)
    $Archive = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem -LiteralPath $Stage -Recurse -File | ForEach-Object {
            $Relative = $_.FullName.Substring($Stage.Length + 1).Replace('\', '/')
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $Archive, $_.FullName, $Relative, [System.IO.Compression.CompressionLevel]::Optimal
            )
        }
    } finally { $Archive.Dispose() }
    $CheckArchive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        if ($CheckArchive.Entries.Count -ne ($Files.Count + 1)) { throw 'ZIP entry count did not match the export manifest.' }
        if ($null -eq $CheckArchive.GetEntry('export-manifest.json')) { throw 'The export manifest is missing from the ZIP.' }
    } finally { $CheckArchive.Dispose() }
    $Completed = $true
    $ZipSizeMiB = (Get-Item -LiteralPath $ZipPath).Length / 1MB
    Write-Host ("Saved {0} files ({1:N1} MiB):" -f $Files.Count, $ZipSizeMiB) -ForegroundColor Green
    Write-Host $ZipPath
    Write-Host 'Upload this ZIP in the chat so the port can be built against your target installation.'
} finally {
    if (Test-Path -LiteralPath $Stage -PathType Container) { Remove-Item -LiteralPath $Stage -Recurse -Force }
    if (-not $Completed -and (Test-Path -LiteralPath $ZipPath -PathType Leaf)) { Remove-Item -LiteralPath $ZipPath -Force }
}
