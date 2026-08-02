param(
  [Parameter(Mandatory = $true)]
  [string]$PackagePath,
  [string]$ExpectedVersion = '',
  [string]$ExpectedCommit = ''
)

$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackagePath).Path
$manifestPath = Join-Path $package 'portable-build-manifest.json'
$exePath = Join-Path $package 'CasePlanner.Web.Server.exe'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Portable build manifest not found: $manifestPath" }
if (-not (Test-Path -LiteralPath $exePath)) { throw "Portable server executable not found: $exePath" }

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($manifest.appVersion)) { throw 'Manifest appVersion is required.' }
if ($ExpectedVersion -and $manifest.appVersion -ne $ExpectedVersion) { throw "Manifest version mismatch: expected=$ExpectedVersion; actual=$($manifest.appVersion)." }
if ($manifest.buildIdentifier -ne "CasePlannerWeb_v$($manifest.appVersion)") { throw "Manifest buildIdentifier does not match appVersion." }
if ($manifest.target -ne 'win-x64') { throw "Manifest target must be win-x64; actual=$($manifest.target)." }
if (-not [bool]$manifest.selfContained) { throw 'Manifest selfContained must be true.' }
if ($ExpectedCommit -and $manifest.commit -ne $ExpectedCommit) { throw "Manifest commit mismatch: expected=$ExpectedCommit; actual=$($manifest.commit)." }
$parsedTimestamp = [DateTimeOffset]::MinValue
if (-not [DateTimeOffset]::TryParse($manifest.generatedAtUtc, [ref]$parsedTimestamp)) { throw 'Manifest generatedAtUtc is not a valid timestamp.' }

[pscustomobject]@{
  Status = 'passed'
  PackagePath = $package
  BuildIdentifier = $manifest.buildIdentifier
  Commit = $manifest.commit
  GeneratedAtUtc = $manifest.generatedAtUtc
} | Format-List
