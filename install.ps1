$ErrorActionPreference = 'Stop'

$exePath = Join-Path $PSScriptRoot 'XboxBatteryMonitor.exe'
$csPath = Join-Path $PSScriptRoot 'XboxBatteryMonitor.cs'

if (-not (Test-Path $exePath)) {
    $csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path $csc)) {
        throw "Could not find .NET Framework compiler at $csc"
    }
    & $csc /nologo /target:winexe /optimize+ "/out:$exePath" $csPath
    if ($LASTEXITCODE -ne 0) {
        throw "Compilation failed."
    }
}

$user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$templatePath = Join-Path $PSScriptRoot 'XboxBatteryMonitorTask.xml'
$tempXmlPath = Join-Path $env:TEMP 'XboxBatteryMonitorTask.user.xml'

$content = [System.IO.File]::ReadAllText($templatePath).
    Replace('__USER__', $user).
    Replace('__EXE_PATH__', $exePath)
[System.IO.File]::WriteAllText($tempXmlPath, $content, [System.Text.Encoding]::Unicode)

try {
    schtasks.exe /Create /TN "XboxBatteryMonitor" /XML $tempXmlPath /F
    if ($LASTEXITCODE -eq 0) {
        schtasks.exe /Run /TN "XboxBatteryMonitor"
        Start-Sleep -Seconds 2
        Get-Process XboxBatteryMonitor -ErrorAction SilentlyContinue |
            Select-Object Name, Id, @{N='RAM_MB';E={[math]::Round($_.WorkingSet64 / 1MB, 1)}}
    }
}
finally {
    Remove-Item $tempXmlPath -Force -ErrorAction SilentlyContinue
}
