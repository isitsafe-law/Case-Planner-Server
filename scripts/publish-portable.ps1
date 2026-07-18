param(
  [string]$Output = "release/CasePlannerWeb_Portable_win-x64"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$client = Join-Path $root 'client'
$server = Join-Path $root 'server/CasePlanner.Web.Server/CasePlanner.Web.Server.csproj'
$destination = Join-Path $root $Output

Push-Location $client
try { npm run build } finally { Pop-Location }

dotnet publish $server -c Release -p:PublishProfile=PortableWinX64 -o $destination
Copy-Item (Join-Path $client 'dist/*') $destination -Recurse -Force

$cleanRelease = Join-Path $root 'release/CasePlannerWeb_v1.0.0_2026-07-12'
foreach ($folder in 'backups','data','exports','import_samples','logs','templates') {
  Copy-Item (Join-Path $cleanRelease $folder) (Join-Path $destination $folder) -Recurse -Force
}

Write-Host "Portable release written to $destination"
