param(
    [string]$ExePath = "",
    [string]$ShortcutName = "StockInvoiceApp"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $projectRoot = Split-Path -Parent $PSScriptRoot
    $candidate = Join-Path $projectRoot "publish\StockInvoiceApp.exe"
    if (Test-Path $candidate) {
        $ExePath = $candidate
    }
}

if (-not (Test-Path $ExePath)) {
    throw "Executable not found. Provide -ExePath or run scripts/Publish-App.ps1 first."
}

$desktop = [Environment]::GetFolderPath("Desktop")
$shortcutPath = Join-Path $desktop ($ShortcutName + ".lnk")

$wsh = New-Object -ComObject WScript.Shell
$shortcut = $wsh.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $ExePath
$shortcut.WorkingDirectory = Split-Path -Parent $ExePath
$shortcut.IconLocation = "$ExePath,0"
$shortcut.Description = "Open StockInvoiceApp"
$shortcut.Save()

Write-Host "Desktop shortcut created: $shortcutPath"
