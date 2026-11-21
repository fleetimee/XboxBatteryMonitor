$ErrorActionPreference = 'SilentlyContinue'

# 1. Stop running monitor process
Stop-Process -Name 'XboxBatteryMonitor' -Force

# 2. Remove Scheduled Task
schtasks.exe /Delete /TN "XboxBatteryMonitor" /F

# 3. Clean up compiled binary and any temp files
$exePath = Join-Path $PSScriptRoot 'XboxBatteryMonitor.exe'
$tempXml = Join-Path $env:TEMP 'XboxBatteryMonitorTask.user.xml'

if (Test-Path $exePath) { Remove-Item $exePath -Force }
if (Test-Path $tempXml) { Remove-Item $tempXml -Force }

Write-Host "XboxBatteryMonitor uninstalled and cleaned up."
