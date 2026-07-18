$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "TemplateRecorderStandalone.csproj"
$publishDir = Join-Path $PSScriptRoot "publish\win-x64"
$zipPath = Join-Path $PSScriptRoot "TemplateRecorderStandalone-win-x64.zip"
$readmePath = Join-Path $PSScriptRoot "README.md"
$templatesPath = Join-Path $publishDir "templates.json"

dotnet publish $project `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o $publishDir

Copy-Item -LiteralPath $readmePath -Destination (Join-Path $publishDir "README.md") -Force

if (-not (Test-Path $templatesPath)) {
  '{ "entries": [] }' | Set-Content -LiteralPath $templatesPath -Encoding UTF8
}

if (Test-Path $zipPath) {
  Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

Write-Host "Published to $publishDir"
Write-Host "Zip created at $zipPath"
