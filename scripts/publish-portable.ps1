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

# Runtime folders start empty - PathService.EnsureFolders() (re)creates them on launch, and the
# app self-seeds its own SQLite schema, built-in document templates (writing their .docx bytes
# fresh), and Reference Library on first run. Do NOT copy a developer database or its backups/
# logs/exports into a handoff package - see docs/it-first-machine-checklist.md.
foreach ($folder in 'data','backups','exports','logs','templates/documents/custom','templates/reference') {
  New-Item -ItemType Directory -Force -Path (Join-Path $destination $folder) | Out-Null
}

# Sample/template CSVs for exercising the Import feature - fictional demo data, not real cases.
New-Item -ItemType Directory -Force -Path (Join-Path $destination 'import_samples') | Out-Null
Copy-Item (Join-Path $root 'import_samples/*') (Join-Path $destination 'import_samples') -Recurse -Force

Write-Host "Portable release written to $destination"
