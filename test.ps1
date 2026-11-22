$ErrorActionPreference = 'Stop'

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    throw "Could not find .NET Framework compiler at $csc"
}

$testExe = Join-Path $env:TEMP 'XboxBatteryMonitor.Tests.exe'
$srcFiles = Get-ChildItem -Path (Join-Path $PSScriptRoot 'src') -Recurse -Filter '*.cs' | ForEach-Object { $_.FullName }
$srcTest = Join-Path $PSScriptRoot 'tests\XboxBatteryMonitor.Tests.cs'

& $csc /nologo /warn:4 /warnaserror /target:exe /main:XboxBatteryMonitor.Tests.TestRunner "/out:$testExe" $srcFiles $srcTest
if ($LASTEXITCODE -ne 0) {
    throw "Test compilation failed."
}

try {
    & $testExe
    if ($LASTEXITCODE -ne 0) {
        throw "One or more unit tests failed."
    }
}
finally {
    Remove-Item $testExe -Force -ErrorAction SilentlyContinue
}
