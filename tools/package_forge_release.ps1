param(
    [string]$Destination = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist'),
    [string]$Version = '0.30.0'
)
$ErrorActionPreference = 'Stop'
if ($Version -ne '0.30.0') { throw 'This candidate must be packaged with version 0.30.0.' }
$root = Split-Path -Parent $PSScriptRoot
# Fail closed when metadata and project version declarations disagree.
& (Join-Path $PSScriptRoot 'verify_forge_identity.ps1') -Root $root -Version $Version -SptVersion '4.1.6'
$client = Join-Path $root 'output/Client/EscapeFromBorderlands.Client.dll'
$server = Join-Path $root 'output/Server/EscapeFromBorderlands.Server.dll'
foreach ($path in @($client, $server)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing compiled DLL: $path. Run build.ps1 first." }
    $assembly = [System.Reflection.AssemblyName]::GetAssemblyName($path)
    if ($assembly.Version -ne [Version]'0.30.0.0') { throw "Wrong assembly version for $path : $($assembly.Version)" }
}
# Verify framework and SPT-specific client marker so a stale 4.0.13 output
# cannot be packaged after changing GitHub Desktop branches.
$serverText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($server))
$clientText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($client))
if (-not $serverText.Contains('.NETCoreApp,Version=v10.0')) {
    throw 'Wrong server framework: SPT 4.1.6 requires the .NET 10 server DLL.'
}
if (-not $clientText.Contains('.NETStandard,Version=v2.1') -or
    -not $clientText.Contains('com.SPT.core')) {
    throw 'Wrong client DLL: expected the native SPT 4.1.6 client build.'
}
$sptruntime='SPT_Runtime'
$label='4.1.6'
$stage = Join-Path $env:TEMP ('efb-forge-stage-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    $clientDir=Join-Path $stage 'BepInEx/plugins/EscapeFromBorderlands'
    $serverDir=Join-Path $stage "$sptruntime/user/mods/EscapeFromBorderlands"
    New-Item -ItemType Directory -Path $clientDir,$serverDir,(Join-Path $serverDir 'config') -Force | Out-Null
    Copy-Item -LiteralPath $client -Destination (Join-Path $clientDir 'EscapeFromBorderlands.Client.dll')
    Copy-Item -LiteralPath $server -Destination (Join-Path $serverDir 'EscapeFromBorderlands.Server.dll')
    Copy-Item -LiteralPath (Join-Path $root 'config/config.json') -Destination (Join-Path $serverDir 'config/config.json')
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE.txt') -Destination (Join-Path $serverDir 'LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $serverDir 'THIRD_PARTY_NOTICES.md')
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $zip = Join-Path $Destination "EscapeFromBorderlands_0.30.0_SPT-$label.zip"
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    # ZIP entries use forward-slash paths per ZIP conventions, even on Windows.
    # Some extraction tools treat backslashes as literal filename characters.
    Add-Type -AssemblyName 'System.IO.Compression'
    Add-Type -AssemblyName 'System.IO.Compression.FileSystem'
    $zipStream = [System.IO.File]::Open($zip, [System.IO.FileMode]::Create)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $zipStream, [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($file in (Get-ChildItem -LiteralPath $stage -File -Recurse)) {
                $entryName = $file.FullName.Substring($stage.Length).TrimStart(
                    [char[]]@(92, 47)).Replace([char]92, [char]47)
                [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $archive, $file.FullName, $entryName,
                    [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $zipStream.Dispose() }
    Write-Host "Created $zip. Verify Forge metadata and conduct native/live testing before publishing." -ForegroundColor Green
} finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
