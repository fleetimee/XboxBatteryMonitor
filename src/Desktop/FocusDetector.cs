using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;
using XboxBatteryMonitor.Core;

namespace XboxBatteryMonitor.Desktop
{
    internal static class FocusDetector
    {
        public static bool IsSteamRunning()
        {
            return Process.GetProcessesByName("steam").Length > 0;
        }

        public static bool IsSteamDesktopLayoutActive()
        {
            if (!IsSteamRunning()) return false;
            try
            {
                object steamPathObj = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null);
                string steamPath = steamPathObj as string;
                if (string.IsNullOrEmpty(steamPath)) return false;

                string vdfPath = Path.Combine(steamPath.Replace('/', '\\'), "config", "config.vdf");
                if (!File.Exists(vdfPath)) return false;

                return IsSteamXboxSupportEnabled(File.ReadAllText(vdfPath));
            }
            catch
            {
                return false;
            }
        }

        public static bool IsSteamXboxSupportEnabled(string configVdfContent)
        {
            if (string.IsNullOrEmpty(configVdfContent)) return false;
            return configVdfContent.IndexOf("\"controller_xbox_support\"\t\t\"1\"", StringComparison.OrdinalIgnoreCase) >= 0
                || configVdfContent.IndexOf("\"controller_xbox_support\" \"1\"", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsUwpGamepadAppFocused()
        {
            try
            {
                IntPtr hwnd = NativeMethods.GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return false;

                StringBuilder cls = new StringBuilder(64);
                NativeMethods.GetClassName(hwnd, cls, cls.Capacity);
                if (cls.ToString().Equals("Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                uint pid;
                NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
                if (pid == 0) return false;

                using (Process proc = Process.GetProcessById((int)pid))
                {
                    string name = proc.ProcessName;
                    return name.Equals("SearchHost", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("SystemSettings", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("WinStore.App", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("GameBar", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool IsFullscreenOrBorderlessAppFocused()
        {
            try
            {
                int qState;
                if (NativeMethods.SHQueryUserNotificationState(out qState) == 0 && qState == 3)
                {
                    return true;
                }

                IntPtr hwnd = NativeMethods.GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return false;

                uint pid;
                NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
                if (pid != 0)
                {
                    using (Process proc = Process.GetProcessById((int)pid))
                    {
                        string pName = proc.ProcessName;
                        if (pName.Equals("chrome", StringComparison.OrdinalIgnoreCase)
                            || pName.Equals("msedge", StringComparison.OrdinalIgnoreCase)
                            || pName.Equals("firefox", StringComparison.OrdinalIgnoreCase)
                            || pName.Equals("brave", StringComparison.OrdinalIgnoreCase)
                            || pName.Equals("opera", StringComparison.OrdinalIgnoreCase)
                            || pName.Equals("vivaldi", StringComparison.OrdinalIgnoreCase)
                            || pName.Equals("arc", StringComparison.OrdinalIgnoreCase)
                            || pName.Equals("vlc", StringComparison.OrdinalIgnoreCase)
                            || pName.Equals("mpv", StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                    }
                }

                StringBuilder cls = new StringBuilder(64);
                NativeMethods.GetClassName(hwnd, cls, cls.Capacity);
                string className = cls.ToString();
                if (className == "Progman" || className == "WorkerW" || className == "Shell_TrayWnd")
                {
                    return false;
                }

                NativeMethods.RECT r;
                if (!NativeMethods.GetWindowRect(hwnd, out r)) return false;

                Screen scr = Screen.FromHandle(hwnd);
                int w = r.Right - r.Left;
                int h = r.Bottom - r.Top;
                return (w >= scr.Bounds.Width && h >= scr.Bounds.Height);
            }
            catch
            {
                return false;
            }
        }
    }
}
