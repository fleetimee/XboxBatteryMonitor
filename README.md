# Xbox Battery Monitor (Windows 10/11)

Lightweight Windows utility that monitors your Xbox controller battery, provides a Steam-layout Desktop Control mode, and includes a compact controller-navigable On-Screen Keyboard.

## Architecture
```text
src/
  Core/      # Entry point (Program.cs), AppSettings.cs, Win32/XInput NativeMethods.cs
  Battery/   # BatteryReader.cs (Bluetooth LE + XInput), BatteryAlertLogic.cs
  Desktop/   # DesktopController.cs (~250 Hz cursor/scroll/shortcuts), FocusDetector.cs
  UI/        # TrayMonitorContext.cs, FluentControls.cs, DetailsForm.cs, SettingsForm.cs, LayoutForm.cs, OsdKeyboardForm.cs
tests/
  XboxBatteryMonitor.Tests.cs
```

## Features
- **Zero-dependency native build:** Compiles directly via Windows' built-in .NET Framework `csc.exe /warn:4 /warnaserror` with no `.csproj`, `obj/`, or `bin/` cache.
- **Win11 Fluent Tray Icon & UI:** High-DPI 32×32 Xbox controller + battery tray icon, Details window, visual Steam Desktop Layout diagram, and Settings window.
- **Desktop Control (Steam Layout):** 250 Hz sub-pixel smooth cursor, scroll wheel, clicks, and shortcuts—auto-pauses in Steam (when Xbox support is enabled), Fullscreen/Borderless games (except browsers/players like YouTube), and Windows UWP/Terminal/Start menus.
- **Compact OSD Keyboard:** Non-activating (`WS_EX_NOACTIVATE`) keyboard toggled with `X`, featuring XInput haptic vibration feedback.

## Build & Setup
```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

## Run Tests
```powershell
powershell -ExecutionPolicy Bypass -File .\test.ps1
```

## Uninstall
```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```
