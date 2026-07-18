param(
  [string]$PackagePath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'release\CasePlannerLocalTest_2026-07-16'),
  [int]$Port = 5297
)

$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackagePath).Path
$exe = Join-Path $package 'CasePlanner.Web.Server.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Published server not found: $exe" }

$url = "http://127.0.0.1:$Port"
$process = Start-Process -FilePath $exe -ArgumentList '--urls', $url -WorkingDirectory $package -WindowStyle Hidden -PassThru
try {
  $health = $null
  for ($attempt = 0; $attempt -lt 30 -and $null -eq $health; $attempt++) {
    try { $health = Invoke-RestMethod "$url/api/health" -TimeoutSec 2 }
    catch { Start-Sleep -Milliseconds 500 }
  }
  if ($null -eq $health -or $health.status -ne 'ok') { throw 'The packaged server did not report a healthy status.' }

  $catalogResponse = Invoke-WebRequest "$url/api/document-catalog" -UseBasicParsing -TimeoutSec 5
  $catalog = $catalogResponse.Content | ConvertFrom-Json
  $catalogCount = if ($catalog -is [array]) { $catalog.Length } elseif ($null -eq $catalog) { 0 } else { 1 }
  if ($catalogCount -eq 0) { throw 'The document catalog is empty.' }

  $docxPath = Join-Path $env:TEMP "case-planner-smoke-$Port.docx"
  Invoke-WebRequest "$url/api/cases/1/generate-document-docx/interrogatories" -Method Post -UseBasicParsing -ContentType 'application/json' -Body (@{ manualInputs = @{}; outputFileName = 'Smoke Output.docx' } | ConvertTo-Json) -OutFile $docxPath -TimeoutSec 30
  $signature = (Get-Content -LiteralPath $docxPath -Encoding Byte -TotalCount 2) -join ','
  if ($signature -ne '80,75') { throw 'The generated file is not a DOCX/ZIP package.' }
  $utilitySignatures = @{}
  foreach ($kind in 'summary', 'memo') {
    $record = Invoke-RestMethod "$url/api/cases/1/generate/$kind" -Method Post -ContentType 'application/json' -Body '{}' -TimeoutSec 30
    $utilityPath = Join-Path $env:TEMP "case-planner-smoke-$Port-$kind.docx"
    Invoke-WebRequest "$url/api/document-exports/$($record.id)/download" -UseBasicParsing -OutFile $utilityPath -TimeoutSec 30
    $utilitySignatures[$kind] = (Get-Content -LiteralPath $utilityPath -Encoding Byte -TotalCount 2) -join ','
    if ($utilitySignatures[$kind] -ne '80,75') { throw "The generated $kind file is not a DOCX/ZIP package." }
  }

  [pscustomobject]@{
    Status = 'passed'
    Url = $url
    Provider = $health.provider
    CatalogEntries = $catalogCount
    DocxBytes = (Get-Item -LiteralPath $docxPath).Length
    DocxSignature = $signature
    SummarySignature = $utilitySignatures.summary
    ReviewSignature = $utilitySignatures.memo
  } | Format-List
}
finally {
  if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
}
