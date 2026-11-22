using System;
using System.Collections.Generic;

namespace XboxBatteryMonitor.Desktop
{
    internal enum DesktopAction
    {
        None,
        LeftClick,
        RightClick,
        MiddleClick,
        ToggleOsdKeyboard,
        Space,
        Enter,
        Escape,
        Backspace,
        Tab,
        AltTab,
        BrowserBack,
        BrowserForward,
        VolumeUp,
        VolumeDown,
        VolumeMute,
        MediaPlayPause
    }

    internal class ButtonBindings
    {
        public static readonly string[] ButtonKeys = new[]
        {
            "A", "B", "X", "Y", "LB", "RB", "LT", "RT", "L3", "R3", "View", "Menu"
        };

        private readonly Dictionary<string, DesktopAction> _map = new Dictionary<string, DesktopAction>(StringComparer.OrdinalIgnoreCase);

        public ButtonBindings()
        {
            ResetDefaults();
        }

        public void ResetDefaults()
        {
            _map["A"] = DesktopAction.LeftClick;
            _map["B"] = DesktopAction.RightClick;
            _map["X"] = DesktopAction.ToggleOsdKeyboard;
            _map["Y"] = DesktopAction.Space;
            _map["RT"] = DesktopAction.LeftClick;
            _map["LT"] = DesktopAction.RightClick;
            _map["LB"] = DesktopAction.BrowserBack;
            _map["RB"] = DesktopAction.BrowserForward;
            _map["L3"] = DesktopAction.MiddleClick;
            _map["R3"] = DesktopAction.Enter;
            _map["View"] = DesktopAction.AltTab;
            _map["Menu"] = DesktopAction.Escape;
        }

        public DesktopAction Get(string buttonKey)
        {
            DesktopAction act;
            return _map.TryGetValue(buttonKey, out act) ? act : DesktopAction.None;
        }

        public void Set(string buttonKey, DesktopAction action)
        {
            _map[buttonKey] = action;
        }

        public void LoadFromRegistry(string regKey)
        {
            foreach (string key in ButtonKeys)
            {
                try
                {
                    object val = Microsoft.Win32.Registry.GetValue(regKey, "Bind_" + key, null);
                    string s = val as string;
                    if (!string.IsNullOrEmpty(s))
                    {
                        DesktopAction parsed;
                        if (Enum.TryParse(s, true, out parsed))
                        {
                            _map[key] = parsed;
                        }
                    }
                }
                catch { }
            }
        }

        public void SaveToRegistry(string regKey)
        {
            foreach (string key in ButtonKeys)
            {
                try
                {
                    Microsoft.Win32.Registry.SetValue(regKey, "Bind_" + key, Get(key).ToString(), Microsoft.Win32.RegistryValueKind.String);
                }
                catch { }
            }
        }

        public static string ActionLabel(DesktopAction action)
        {
            switch (action)
            {
                case DesktopAction.None: return "Disabled";
                case DesktopAction.LeftClick: return "Left Click";
                case DesktopAction.RightClick: return "Right Click";
                case DesktopAction.MiddleClick: return "Middle Click";
                case DesktopAction.ToggleOsdKeyboard: return "On-Screen Keyboard";
                case DesktopAction.Space: return "Space";
                case DesktopAction.Enter: return "Enter";
                case DesktopAction.Escape: return "Esc";
                case DesktopAction.Backspace: return "Backspace";
                case DesktopAction.Tab: return "Tab";
                case DesktopAction.AltTab: return "Alt + Tab";
                case DesktopAction.BrowserBack: return "Browser Back";
                case DesktopAction.BrowserForward: return "Browser Forward";
                case DesktopAction.VolumeUp: return "Volume Up";
                case DesktopAction.VolumeDown: return "Volume Down";
                case DesktopAction.VolumeMute: return "Mute Audio";
                case DesktopAction.MediaPlayPause: return "Play / Pause";
                default: return action.ToString();
            }
        }
    }
}
