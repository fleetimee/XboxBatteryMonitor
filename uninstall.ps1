$ErrorActionPreference = 'SilentlyContinue'

# 1. Stop running monitor process
Stop-Process -Name 'XboxBatteryMonitor' -Force

# 2. Remove Startup shortcut and any legacy Scheduled Task
$startupDir = [Environment]::GetFolderPath('Startup')
$shortcutPath = Join-Path $startupDir 'XboxBatteryMonitor.lnk'
if (Test-Path $shortcutPath) { Remove-Item $shortcutPath -Force }
schtasks.exe /Delete /TN "XboxBatteryMonitor" /F 2>$null | Out-Null

# 3. Clean up compiled binary and any temp files
$exePath = Join-Path $PSScriptRoot 'XboxBatteryMonitor.exe'
$tempXml = Join-Path $env:TEMP 'XboxBatteryMonitorTask.user.xml'

if (Test-Path $exePath) { Remove-Item $exePath -Force }
if (Test-Path $tempXml) { Remove-Item $tempXml -Force }
Remove-Item -Path 'HKCU:\Software\XboxBatteryMonitor' -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "XboxBatteryMonitor uninstalled and cleaned up."
