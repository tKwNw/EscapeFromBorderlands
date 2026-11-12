param([string]$GameRoot = "C:\SPT MODDED", [string]$Configuration = "Release")
$ErrorActionPreference = "Stop"

$GameRoot = (Resolve-Path -LiteralPath $GameRoot -ErrorAction Stop).Path
$ServerRoot = Join-Path $GameRoot 'SPT'
$CorePath = Join-Path $ServerRoot 'SPTarkov.Server.Core.dll'
if (-not (Test-Path -LiteralPath $CorePath -PathType Leaf)) {
    throw "SPT 4.0.13 required. SPT/SPTarkov.Server.Core.dll was not found under $GameRoot."
}
$SptVersion = [Reflection.AssemblyName]::GetAssemblyName($CorePath).Version
if ($SptVersion.Major -ne 4 -or $SptVersion.Minor -ne 0 -or $SptVersion.Build -ne 13) {
    throw "SPT 4.0.13 required. Found $SptVersion in $CorePath; do not compile this branch against SPT 4.1.x."
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install the .NET SDK with .NET 9 targeting support.'
}

$ProjectRoot = $PSScriptRoot
$BuildOutput = Join-Path $ProjectRoot 'output'
$ServerProject = Join-Path $ProjectRoot 'src\EscapeFromBorderlands.Server\EscapeFromBorderlands.Server.csproj'
$ClientProject = Join-Path $ProjectRoot 'src\EscapeFromBorderlands.Client\EscapeFromBorderlands.Client.csproj'
$ServerOutput = Join-Path $BuildOutput 'Server\EscapeFromBorderlands.Server.dll'
$ClientOutput = Join-Path $BuildOutput 'Client\EscapeFromBorderlands.Client.dll'

# output/ is ignored by Git; a branch switch does not remove older binaries.
# Fail safely on build errors rather than repackaging a prior 4.1.6 build.
foreach ($file in @($ServerOutput, $ClientOutput)) {
    if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
}

dotnet build $ServerProject -c $Configuration --no-incremental "-p:SptServerRoot=$ServerRoot" "-p:OutputPath=$(Join-Path $BuildOutput 'Server')"
if ($LASTEXITCODE -ne 0) { throw 'SPT 4.0.13 server build failed.' }
dotnet build $ClientProject -c $Configuration --no-incremental "-p:EftManagedRoot=$(Join-Path $GameRoot 'EscapeFromTarkov_Data\Managed')" "-p:BepInExCoreRoot=$(Join-Path $GameRoot 'BepInEx\core')" "-p:OutputPath=$(Join-Path $BuildOutput 'Client')"
if ($LASTEXITCODE -ne 0) { throw 'SPT 4.0.13 client build failed.' }

foreach ($file in @($ServerOutput, $ClientOutput)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Build output missing: $file" }
    if ([Reflection.AssemblyName]::GetAssemblyName($file).Version -ne [Version]'0.40.2.0') {
        throw "Unexpected assembly version in $file"
    }
}
$ServerBytes = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($ServerOutput))
$ClientBytes = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($ClientOutput))
if (-not $ServerBytes.Contains('.NETCoreApp,Version=v9.0') -or $ServerBytes.Contains('.NETCoreApp,Version=v10.0')) {
    throw 'Server DLL is not compiled for .NET 9. Remove stale build files and rebuild against SPT 4.0.13.'
}
if (-not $ClientBytes.Contains('.NETStandard,Version=v2.1') -or $ClientBytes.Contains('com.SPT.core')) {
    throw 'Client DLL looks incompatible with SPT 4.0.13 (possibly a stale 4.1.6 DLL).'
}
Write-Host "PASS: EFB 0.40.2 client and server built for SPT 4.0.13 ($SptVersion), .NET 9 / .NET Standard 2.1." -ForegroundColor Green
