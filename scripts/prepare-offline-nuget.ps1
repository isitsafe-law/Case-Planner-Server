param(
  [string]$Output = "temp/offline-nuget"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$outputPath = [IO.Path]::GetFullPath((Join-Path $root $Output))
$cache = Join-Path $env:USERPROFILE ".nuget\packages"

if (-not (Test-Path $cache)) { throw "NuGet global package cache was not found: $cache" }
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

foreach ($package in Get-ChildItem $cache -Recurse -Filter *.nupkg -File) {
  $target = Join-Path $outputPath $package.Name
  if (-not (Test-Path $target)) {
    Copy-Item -LiteralPath $package.FullName -Destination $target
  }
}

$count = (Get-ChildItem $outputPath -Filter *.nupkg -File).Count
Write-Host "Prepared offline NuGet feed with $count packages at $outputPath"
