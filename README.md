# Xbox Battery Monitor (Windows 10/11)

Lightweight, event-triggered Windows utility that sends a native notification when your Xbox controller battery drops to **20% or below**.

## Features
- **Zero idle overhead:** Uses Windows Task Scheduler (`Kernel-PnP/Configuration` Event ID `410`) to auto-start when your controller connects via **Bluetooth** or **USB**, and automatically exits when disconnected.
- **Exact Bluetooth LE battery %:** Reads Windows' cached Bluetooth LE battery level (`DEVPKEY` `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2`), falling back to `XInputGetBatteryInformation` (`Low`/`Empty`) if unavailable.
- **Anti-spam:** Notifies once per low-battery cycle and resets when recharged above 25% or reconnected.
- **No external dependencies:** Compiles directly with Windows' built-in .NET Framework `csc.exe`.

## Setup
Run in PowerShell:
```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

## Test Notification
```powershell
.\XboxBatteryMonitor.exe --test
```

## Uninstall
```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```
