param(
  [string]$Configuration = "Release",
  [string]$SmokeUrl = "http://127.0.0.1:5271",
  [switch]$Restore,
  [switch]$WebOnly,
  [switch]$SkipRuntimeSmoke
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root "CasePlanner.slnx"
$publish = [IO.Path]::Combine($root, "temp", "phase1-publish")

if ($Restore) { dotnet restore $solution }
$webProject = [IO.Path]::Combine($root, "server", "CasePlanner.Web.Server", "CasePlanner.Web.Server.csproj")
if ($WebOnly) {
  dotnet build $webProject -c $Configuration --no-restore
} else {
  dotnet build $solution -c $Configuration --no-restore
  dotnet test ([IO.Path]::Combine($root, "server", "CasePlanner.Web.Server.Tests", "CasePlanner.Web.Server.Tests.csproj")) -c $Configuration --no-build
}

if (Test-Path $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
dotnet publish $webProject -c $Configuration -p:PublishProfile=IisFrameworkDependent -o $publish --no-restore

if ($SkipRuntimeSmoke -or $WebOnly) {
  Write-Host "Phase 1 build/publish checks passed; runtime smoke was skipped."
  Remove-Item -LiteralPath $publish -Recurse -Force -ErrorAction SilentlyContinue
  exit 0
}

$appDll = Join-Path $publish "CasePlanner.Web.Server.dll"
$process = Start-Process -FilePath "dotnet" -ArgumentList ('"' + $appDll + '" --urls "' + $SmokeUrl + '"') -WorkingDirectory $publish -WindowStyle Hidden -PassThru
try {
  $ready = $false
  for ($i = 0; $i -lt 30; $i++) {
    try {
      $health = Invoke-RestMethod "$SmokeUrl/api/health" -TimeoutSec 2
      $home = Invoke-WebRequest "$SmokeUrl/" -UseBasicParsing -TimeoutSec 2
      $ready = $true
      break
    } catch {
      if ($process.HasExited) { throw "Published application exited before readiness checks completed." }
      Start-Sleep -Milliseconds 500
    }
  }
  if (-not $ready) { throw "Published application did not become ready." }
  if ($health.status -ne "ok" -or $home.StatusCode -ne 200) { throw "Health or home-page smoke check failed." }
  Write-Host "Phase 1 smoke checks passed: home=$($home.StatusCode); health=$($health.status); provider=$($health.databaseProvider)"
}
finally {
  if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
  if (Test-Path $publish) { Remove-Item -LiteralPath $publish -Recurse -Force -ErrorAction SilentlyContinue }
}
