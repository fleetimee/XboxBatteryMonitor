$ErrorActionPreference = 'Stop'

$exePath = Join-Path $PSScriptRoot 'XboxBatteryMonitor.exe'
$srcDir = Join-Path $PSScriptRoot 'src'
$icoPath = Join-Path $PSScriptRoot 'XboxBatteryMonitor.ico'

Stop-Process -Name 'XboxBatteryMonitor' -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

$srcFiles = Get-ChildItem -Path $srcDir -Recurse -Filter '*.cs' | ForEach-Object { $_.FullName }
if ($srcFiles.Count -eq 0) {
    throw "No C# source files found under $srcDir"
}

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    throw "Could not find .NET Framework compiler at $csc"
}

if (Test-Path $icoPath) {
    & $csc /nologo /warn:4 /warnaserror /target:winexe /optimize+ "/win32icon:$icoPath" "/out:$exePath" $srcFiles
} else {
    & $csc /nologo /warn:4 /warnaserror /target:winexe /optimize+ "/out:$exePath" $srcFiles
}
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed."
}

# Remove legacy Scheduled Task if present
$prevPref = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
schtasks.exe /Delete /TN "XboxBatteryMonitor" /F 2>$null | Out-Null
$ErrorActionPreference = $prevPref

# Create Startup folder shortcut
$startupDir = [Environment]::GetFolderPath('Startup')
$shortcutPath = Join-Path $startupDir 'XboxBatteryMonitor.lnk'
$wsh = New-Object -ComObject WScript.Shell
$shortcut = $wsh.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $PSScriptRoot
$shortcut.Description = 'Xbox Controller Battery Monitor'
$shortcut.Save()

Write-Host "Built $exePath and registered Startup shortcut."

# Launch in the user's interactive desktop session (detached from terminal job object)
$launchTask = 'XboxBatteryMonitor_OneShotLaunch'
$prevPref2 = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
schtasks.exe /Create /TN $launchTask /TR "`"$exePath`"" /SC ONCE /ST 00:00 /F 2>$null | Out-Null
schtasks.exe /Run /TN $launchTask 2>$null | Out-Null
Start-Sleep -Seconds 2
schtasks.exe /Delete /TN $launchTask /F 2>$null | Out-Null
$ErrorActionPreference = $prevPref2

Get-Process XboxBatteryMonitor -ErrorAction SilentlyContinue |
    Select-Object Name, Id, @{N='RAM_MB';E={[math]::Round($_.WorkingSet64 / 1MB, 1)}}
