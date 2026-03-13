param(
    [string]$Runtime = "",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $projectRoot

$publishDir = Join-Path $projectRoot "publish"
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

Write-Host "Publishing StockInvoiceApp..."

if ([string]::IsNullOrWhiteSpace($Runtime)) {
    dotnet publish .\StockInvoiceApp.csproj -c Release -o $publishDir
} elseif ($SelfContained.IsPresent) {
    dotnet publish .\StockInvoiceApp.csproj -c Release -r $Runtime --self-contained true -o $publishDir
} else {
    dotnet publish .\StockInvoiceApp.csproj -c Release -r $Runtime --self-contained false -o $publishDir
}

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Publish complete: $publishDir"
Write-Host "Executable: $(Join-Path $publishDir 'StockInvoiceApp.exe')"
