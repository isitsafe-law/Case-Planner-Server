param(
  [string]$ReferenceFolder = (Join-Path (Split-Path -Parent $PSScriptRoot) 'templates\reference'),
  [string]$OutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'temp\reference-library-seed.sql'),
  [string]$Schema = 'dbo'
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $ReferenceFolder)) { throw "Reference folder not found: $ReferenceFolder" }
if ($Schema -notmatch '^[A-Za-z0-9_]+$') { throw 'Schema must contain only letters, numbers, and underscores.' }

function Sql([string]$value) {
  if ($null -eq $value) { $value = '' }
  return "N'" + ($value -replace "'", "''") + "'"
}
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("-- Reviewable seed generated from $ReferenceFolder")
$lines.Add("SET NOCOUNT ON;")
foreach ($file in Get-ChildItem -LiteralPath $ReferenceFolder -Filter '*.txt' -File | Sort-Object Name) {
  $key = [IO.Path]::GetFileNameWithoutExtension($file.Name)
  if ($key -notmatch '^[a-zA-Z0-9][a-zA-Z0-9_-]{0,79}$') { continue }
  $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
  $title = $key -replace '[_-]+', ' '
  $title = (Get-Culture).TextInfo.ToTitleCase($title)
  $lines.Add("MERGE [$Schema].[reference_library_documents] AS target USING (SELECT $(Sql $key) AS document_key) AS source ON target.document_key=source.document_key")
  $lines.Add("WHEN MATCHED THEN UPDATE SET title=$(Sql $title),description=target.description,document_text=$(Sql $text),is_deleted=0,updated_utc=SYSUTCDATETIME()")
  $lines.Add("WHEN NOT MATCHED THEN INSERT(document_key,title,description,document_text) VALUES($(Sql $key),$(Sql $title),N'',$(Sql $text));")
}
$parent = Split-Path -Parent $OutputPath
if ($parent -and -not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
Set-Content -LiteralPath $OutputPath -Value $lines -Encoding UTF8
Write-Host "Wrote $($lines.Count) SQL lines to $OutputPath"
