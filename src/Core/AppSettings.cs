using System;
using System.IO;
using System.Reflection;

namespace XboxBatteryMonitor.Core
{
    internal class AppSettings
    {
        private const string RegKey = @"HKEY_CURRENT_USER\Software\XboxBatteryMonitor";

        public int CursorSensitivity = 100;
        public int ScrollSpeed = 100;
        public int LowBatteryThreshold = 20;
        public bool OsdVibration = true;
        public bool MuteWhenSteamOpen = true;

        public static AppSettings Load()
        {
            AppSettings s = new AppSettings();
            s.CursorSensitivity = ReadInt("CursorSensitivity", 100, 25, 300);
            s.ScrollSpeed = ReadInt("ScrollSpeed", 100, 25, 300);
            s.LowBatteryThreshold = ReadInt("LowBatteryThreshold", 20, 10, 30);
            s.OsdVibration = ReadInt("OsdVibration", 1, 0, 1) == 1;
            s.MuteWhenSteamOpen = ReadInt("MuteWhenSteamOpen", 1, 0, 1) == 1;
            return s;
        }

        public void Save()
        {
            try
            {
                Microsoft.Win32.Registry.SetValue(RegKey, "CursorSensitivity", CursorSensitivity, Microsoft.Win32.RegistryValueKind.DWord);
                Microsoft.Win32.Registry.SetValue(RegKey, "ScrollSpeed", ScrollSpeed, Microsoft.Win32.RegistryValueKind.DWord);
                Microsoft.Win32.Registry.SetValue(RegKey, "LowBatteryThreshold", LowBatteryThreshold, Microsoft.Win32.RegistryValueKind.DWord);
                Microsoft.Win32.Registry.SetValue(RegKey, "OsdVibration", OsdVibration ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
                Microsoft.Win32.Registry.SetValue(RegKey, "MuteWhenSteamOpen", MuteWhenSteamOpen ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch { }
        }

        public static string GetStartupShortcutPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                "XboxBatteryMonitor.lnk");
        }

        public static bool IsAutoStartEnabled()
        {
            return File.Exists(GetStartupShortcutPath());
        }

        public static void SetAutoStart(bool enabled)
        {
            try
            {
                string path = GetStartupShortcutPath();
                if (!enabled)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }
                Type wshType = Type.GetTypeFromProgID("WScript.Shell");
                if (wshType == null) return;
                object wsh = Activator.CreateInstance(wshType);
                object shortcut = wshType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, wsh, new object[] { path });
                Type scType = shortcut.GetType();
                string exePath = Assembly.GetExecutingAssembly().Location;
                scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { exePath });
                scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(exePath) });
                scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Xbox Controller Battery Monitor" });
                scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            catch { }
        }

        private static int ReadInt(string name, int def, int min, int max)
        {
            try
            {
                object val = Microsoft.Win32.Registry.GetValue(RegKey, name, def);
                if (val is int)
                {
                    return Math.Max(min, Math.Min(max, (int)val));
                }
            }
            catch { }
            return def;
        }
    }
}
