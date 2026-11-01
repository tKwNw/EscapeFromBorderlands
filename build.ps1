param([string]$GameRoot = "C:\SPT MODDED", [string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot
$BuildOutput = Join-Path $ProjectRoot "output"
$ServerProject = Join-Path $ProjectRoot "src\EscapeFromBorderlands.Server\EscapeFromBorderlands.Server.csproj"
$ClientProject = Join-Path $ProjectRoot "src\EscapeFromBorderlands.Client\EscapeFromBorderlands.Client.csproj"
dotnet build $ServerProject -c $Configuration "-p:SptServerRoot=$(Join-Path $GameRoot 'SPT')" "-p:OutputPath=$(Join-Path $BuildOutput 'Server')"
if ($LASTEXITCODE -ne 0) { throw "Server build failed." }
dotnet build $ClientProject -c $Configuration "-p:EftManagedRoot=$(Join-Path $GameRoot 'EscapeFromTarkov_Data\Managed')" "-p:BepInExCoreRoot=$(Join-Path $GameRoot 'BepInEx\core')" "-p:OutputPath=$(Join-Path $BuildOutput 'Client')"
if ($LASTEXITCODE -ne 0) { throw "Client build failed." }
Write-Host "Escape From Borderlands 0.30.0 built in $BuildOutput" -ForegroundColor Green
