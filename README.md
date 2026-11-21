# Xbox Battery Monitor (Windows 10/11)

Lightweight Windows utility that monitors your Xbox controller battery and sends a native notification when it drops to **20% or below**.

## Features
- **Event-driven (`WM_DEVICECHANGE`):** Starts silently at Windows logon via a Startup shortcut (no Task Scheduler needed). When no controller is connected, the tray icon is hidden and polling is stopped (**0% CPU**). As soon as you connect via **Bluetooth** or **USB**, the tray icon appears automatically.
- **Win11 Fluent Tray Icon & Details UI:** High-DPI 32×32 Xbox controller + battery tray icon; double-click to view controller model, connection type, charging status, and exact battery %.
- **Exact Bluetooth LE battery %:** Reads Windows' cached Bluetooth LE battery level (`DEVPKEY` `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2`), falling back to `XInputGetBatteryInformation` (`Low`/`Empty`) if unavailable.
- **Steam-aware anti-spam:** Automatically skips duplicate low-battery notifications if `steam.exe` is running.
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
