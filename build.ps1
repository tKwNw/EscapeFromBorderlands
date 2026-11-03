param([Parameter(Mandatory = $true)][string]$GameRoot, [string]$Configuration = "Release", [switch]$ServerOnly)
$ErrorActionPreference = "Stop"
$GameRoot = (Resolve-Path -LiteralPath $GameRoot).Path
$ServerRoot = Join-Path $GameRoot 'SPT_Runtime'
$CorePath = Join-Path $ServerRoot 'SPTarkov.Server.Core.dll'
if (-not (Test-Path -LiteralPath $CorePath -PathType Leaf)) { throw 'Select the installed SPT 4.1.6 game folder; SPT_Runtime/SPTarkov.Server.Core.dll is missing.' }
$CoreVersion = [Reflection.AssemblyName]::GetAssemblyName($CorePath).Version
if ($CoreVersion.Major -ne 4 -or $CoreVersion.Minor -ne 1 -or $CoreVersion.Build -ne 6) { throw "SPT 4.1.6 references are required; found $CoreVersion." }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET 10 SDK before building.' }
$Sdks = @(dotnet --list-sdks)
if ($LASTEXITCODE -ne 0 -or -not @($Sdks | Where-Object { $_ -match '^(\d+)\.' -and [int]$Matches[1] -ge 10 }).Count) { throw 'The .NET 10 SDK is required; a runtime alone cannot build this project.' }
Write-Host 'Building Escape From Borderlands 0.31.6 against native SPT 4.1.6 references.'
$ProjectRoot = $PSScriptRoot
$BuildOutput = Join-Path $ProjectRoot "output"
$ServerProject = Join-Path $ProjectRoot "src\EscapeFromBorderlands.Server\EscapeFromBorderlands.Server.csproj"
$ClientProject = Join-Path $ProjectRoot "src\EscapeFromBorderlands.Client\EscapeFromBorderlands.Client.csproj"
dotnet build $ServerProject -c $Configuration "-p:SptServerRoot=$ServerRoot" "-p:OutputPath=$(Join-Path $BuildOutput 'Server')"
if ($LASTEXITCODE -ne 0) { throw "Server build failed." }
if ($ServerOnly) {
    Write-Host "Server source built into $(Join-Path $BuildOutput 'Server'). Build the client as well to produce both installable components." -ForegroundColor Yellow
    exit 0
}
dotnet build $ClientProject -c $Configuration "-p:SptGameRoot=$GameRoot" "-p:OutputPath=$(Join-Path $BuildOutput 'Client')"
if ($LASTEXITCODE -ne 0) { throw "Client build failed." }
Write-Host "Escape From Borderlands 0.31.6 build written to $BuildOutput" -ForegroundColor Green
