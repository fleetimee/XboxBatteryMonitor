$ErrorActionPreference = 'Stop'

$exePath = Join-Path $PSScriptRoot 'XboxBatteryMonitor.exe'
$csPath = Join-Path $PSScriptRoot 'XboxBatteryMonitor.cs'

# Stop any running instance before compiling
Stop-Process -Name 'XboxBatteryMonitor' -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

# Compile if exe is missing or source is newer
$needsBuild = -not (Test-Path $exePath)
if (-not $needsBuild) {
    $needsBuild = (Get-Item $csPath).LastWriteTime -gt (Get-Item $exePath).LastWriteTime
}
if ($needsBuild) {
    $csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path $csc)) {
        throw "Could not find .NET Framework compiler at $csc"
    }
    & $csc /nologo /target:winexe /optimize+ "/out:$exePath" $csPath
    if ($LASTEXITCODE -ne 0) {
        throw "Compilation failed."
    }
}

# Remove legacy Scheduled Task if present
$prevPref = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
schtasks.exe /Delete /TN "XboxBatteryMonitor" /F 2>$null | Out-Null
$ErrorActionPreference = $prevPref

# Create Startup folder shortcut (no Task Scheduler needed)
$startupDir = [Environment]::GetFolderPath('Startup')
$shortcutPath = Join-Path $startupDir 'XboxBatteryMonitor.lnk'
$wsh = New-Object -ComObject WScript.Shell
$shortcut = $wsh.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $PSScriptRoot
$shortcut.Description = 'Xbox Controller Battery Monitor'
$shortcut.Save()

# Launch watcher detached via Explorer (same as Windows Startup)
Start-Process explorer.exe -ArgumentList "`"$shortcutPath`""
Start-Sleep -Seconds 2
Get-Process XboxBatteryMonitor -ErrorAction SilentlyContinue |
    Select-Object Name, Id, @{N='RAM_MB';E={[math]::Round($_.WorkingSet64 / 1MB, 1)}}
