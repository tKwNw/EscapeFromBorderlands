param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [string]$Version = '0.30.0',
    [Parameter(Mandatory=$true)][string]$SptVersion
)
$ErrorActionPreference = 'Stop'
$guid = 'com.tKwNw.escapefromborderlands'
$client = Get-Content -LiteralPath (Join-Path $Root 'src/EscapeFromBorderlands.Client/Plugin.cs') -Raw
$server = Get-Content -LiteralPath (Join-Path $Root 'src/EscapeFromBorderlands.Server/ModMetadata.cs') -Raw
$clientProj = Get-Content -LiteralPath (Join-Path $Root 'src/EscapeFromBorderlands.Client/EscapeFromBorderlands.Client.csproj') -Raw
$serverProj = Get-Content -LiteralPath (Join-Path $Root 'src/EscapeFromBorderlands.Server/EscapeFromBorderlands.Server.csproj') -Raw
function Ensure([bool]$condition, [string]$message) { if (-not $condition) { throw $message } }
Ensure ($client.Contains('public const string PluginGuid = "' + $guid + '";')) 'Client GUID does not match the expected Forge GUID.'
Ensure ($server.Contains('ModGuid { get; init; } = "' + $guid + '";')) 'Server GUID does not match the expected Forge GUID.'
Ensure ($client.Contains('public const string PluginName = "tKwNw-EscapeFromBorderlands";')) 'Client display name is not Forge-compliant.'
Ensure ($server.Contains('Author { get; init; } = "tKwNw";')) 'Server author does not match GitHub username.'
Ensure ($client.Contains('public const string PluginVersion = "' + $Version + '";')) 'Client plugin version mismatch.'
Ensure ($server.Contains('new("' + $Version + '", false)')) 'Server mod version mismatch.'
Ensure ($clientProj.Contains('<Version>' + $Version + '</Version>')) 'Client project version mismatch.'
Ensure ($serverProj.Contains('<Version>' + $Version + '</Version>')) 'Server project version mismatch.'
Ensure ($server.Contains('new("~' + $SptVersion + '", false)')) 'Server SPT compatibility mismatch.'
Ensure (Test-Path (Join-Path $Root 'src/EscapeFromBorderlands.Client/LegacyClientConfigMigration.cs')) 'Missing migration helper.'
Ensure (Test-Path (Join-Path $Root 'LICENSE.txt')) 'Missing MIT license.'
Ensure (Test-Path (Join-Path $Root 'THIRD_PARTY_NOTICES.md')) 'Missing third-party audio licensing notice.'
Write-Host "PASS: GUID=$guid, author=tKwNw, version=$Version, SPT=$SptVersion, migration, licensing." -ForegroundColor Green
