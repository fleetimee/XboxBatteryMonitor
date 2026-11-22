using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace XboxBatteryMonitor
{
    static class Program
    {
        // XInput constants
        private const int ERROR_SUCCESS = 0;
        private const byte BATTERY_DEVTYPE_GAMEPAD = 0x00;
        private const byte BATTERY_TYPE_DISCONNECTED = 0x00;
        private const byte BATTERY_TYPE_WIRED = 0x01;
        private const byte BATTERY_TYPE_ALKALINE = 0x02;
        private const byte BATTERY_TYPE_NIMH = 0x03;
        private const byte BATTERY_LEVEL_EMPTY = 0x00;
        private const byte BATTERY_LEVEL_LOW = 0x01;
        private const byte BATTERY_LEVEL_MEDIUM = 0x02;
        private const byte BATTERY_LEVEL_FULL = 0x03;

        private const int LOW_THRESHOLD_PERCENT = 20;
        private const int RESET_THRESHOLD_PERCENT = 25;
        private const int POLL_INTERVAL_MS = 60000;

        // SetupAPI / CfgMgr32 constants
        private const int DIGCF_PRESENT = 0x02;
        private const int DIGCF_ALLCLASSES = 0x04;
        private const int CR_SUCCESS = 0;
        private const int DEVPROP_TYPE_BYTE = 0x00000003;

        [StructLayout(LayoutKind.Sequential)]
        struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XINPUT_BATTERY_INFORMATION
        {
            public byte BatteryType;
            public byte BatteryLevel;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XINPUT_VIBRATION
        {
            public ushort wLeftMotorSpeed;
            public ushort wRightMotorSpeed;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DEVPROPKEY
        {
            public Guid fmtid;
            public uint pid;
        }

        internal class ControllerSnapshot
        {
            public int Slot;
            public string ModelName;
            public string ConnectionType;
            public string BatteryTypeLabel;
            public int? ExactPercent;
            public byte XInputLevel;
            public bool IsWired;
            public bool IsChargingInferred;
        }

        [DllImport("xinput1_4.dll")]
        private static extern int XInputGetState(int dwUserIndex, ref XINPUT_STATE pState);

        [DllImport("xinput1_4.dll")]
        private static extern int XInputGetBatteryInformation(int dwUserIndex, byte devType, ref XINPUT_BATTERY_INFORMATION pBatteryInformation);

        [DllImport("xinput1_4.dll")]
        private static extern int XInputSetState(int dwUserIndex, ref XINPUT_VIBRATION pVibration);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(IntPtr ClassGuid, string Enumerator, IntPtr hwndParent, int Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceRegistryProperty(
            IntPtr DeviceInfoSet,
            ref SP_DEVINFO_DATA DeviceInfoData,
            uint Property,
            out uint PropertyRegDataType,
            byte[] PropertyBuffer,
            uint PropertyBufferSize,
            out uint RequiredSize);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInstanceId(
            IntPtr DeviceInfoSet,
            ref SP_DEVINFO_DATA DeviceInfoData,
            StringBuilder DeviceInstanceId,
            uint DeviceInstanceIdSize,
            out uint RequiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Get_DevNode_Property(
            uint dnDevInst,
            ref DEVPROPKEY PropertyKey,
            out uint PropertyType,
            byte[] PropertyBuffer,
            ref uint PropertyBufferSize,
            uint ulFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern IntPtr SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out int pquns);

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint uPeriod);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private const int INPUT_MOUSE = 0;
        private const int INPUT_KEYBOARD = 1;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const uint MOUSEEVENTF_HWHEEL = 0x1000;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public int type;
            public INPUTUNION u;
        }

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWCP_ROUND = 2;

        [STAThread]
        static void Main(string[] args)
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } // Per-Monitor V2
            catch { try { SetProcessDPIAware(); } catch { } }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool testMode = args.Length > 0 && args[0].Equals("--test", StringComparison.OrdinalIgnoreCase);

            bool createdNew;
            using (Mutex mutex = new Mutex(true, @"Local\XboxBatteryMonitor_SingleInstance", out createdNew))
            {
                if (!createdNew && !testMode)
                {
                    return;
                }

                Application.Run(new MonitorContext(testMode));
            }
        }

        internal static bool IsAnyControllerConnected()
        {
            XINPUT_STATE state = new XINPUT_STATE();
            for (int i = 0; i < 4; i++)
            {
                if (XInputGetState(i, ref state) == ERROR_SUCCESS)
                {
                    return true;
                }
            }
            return false;
        }

        internal static List<ControllerSnapshot> GetConnectedControllers()
        {
            var list = new List<ControllerSnapshot>();
            string bleName;
            int? blePct = GetBluetoothXboxInfo(out bleName);
            string usbModel = GetUsbXboxModelName();

            for (int i = 0; i < 4; i++)
            {
                XINPUT_STATE state = new XINPUT_STATE();
                if (XInputGetState(i, ref state) != ERROR_SUCCESS)
                {
                    continue;
                }

                XINPUT_BATTERY_INFORMATION bat = new XINPUT_BATTERY_INFORMATION();
                XInputGetBatteryInformation(i, BATTERY_DEVTYPE_GAMEPAD, ref bat);

                bool isWired = (bat.BatteryType == BATTERY_TYPE_WIRED);
                string model = isWired
                    ? (!string.IsNullOrEmpty(usbModel) ? usbModel : "Xbox Controller (USB)")
                    : (!string.IsNullOrEmpty(bleName) ? bleName : "Xbox Wireless Controller");

                list.Add(new ControllerSnapshot
                {
                    Slot = i + 1,
                    ModelName = model,
                    ConnectionType = isWired ? "USB Cable" : "Bluetooth LE",
                    BatteryTypeLabel = GetBatteryTypeLabel(bat.BatteryType),
                    ExactPercent = isWired ? null : blePct,
                    XInputLevel = bat.BatteryLevel,
                    IsWired = isWired
                });
            }

            return list;
        }

        private static string GetBatteryTypeLabel(byte batteryType)
        {
            switch (batteryType)
            {
                case BATTERY_TYPE_WIRED: return "USB Powered";
                case BATTERY_TYPE_ALKALINE: return "Alkaline (AA)";
                case BATTERY_TYPE_NIMH: return "NiMH / Rechargeable";
                default: return "Wireless Battery";
            }
        }

        private static int? GetBluetoothXboxInfo(out string modelName)
        {
            modelName = null;
            IntPtr devInfoSet = SetupDiGetClassDevs(IntPtr.Zero, "BTHLE", IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (devInfoSet == IntPtr.Zero || devInfoSet == new IntPtr(-1))
            {
                return null;
            }

            try
            {
                DEVPROPKEY batteryKey = new DEVPROPKEY
                {
                    fmtid = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"),
                    pid = 2
                };

                SP_DEVINFO_DATA devInfoData = new SP_DEVINFO_DATA();
                devInfoData.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));

                uint index = 0;
                while (SetupDiEnumDeviceInfo(devInfoSet, index++, ref devInfoData))
                {
                    string name = GetDeviceRegistryString(devInfoSet, ref devInfoData, 0x0000000C) // SPDRP_FRIENDLYNAME
                               ?? GetDeviceRegistryString(devInfoSet, ref devInfoData, 0x00000000); // SPDRP_DEVICEDESC

                    if (string.IsNullOrEmpty(name) || name.IndexOf("Xbox", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    modelName = name;

                    byte[] buffer = new byte[4];
                    uint propType;
                    uint size = (uint)buffer.Length;
                    int cr = CM_Get_DevNode_Property(devInfoData.DevInst, ref batteryKey, out propType, buffer, ref size, 0);
                    if (cr == CR_SUCCESS && propType == DEVPROP_TYPE_BYTE && size >= 1)
                    {
                        return buffer[0];
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devInfoSet);
            }

            return null;
        }

        private static string GetUsbXboxModelName()
        {
            IntPtr devInfoSet = SetupDiGetClassDevs(IntPtr.Zero, "USB", IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (devInfoSet == IntPtr.Zero || devInfoSet == new IntPtr(-1))
            {
                return null;
            }

            try
            {
                SP_DEVINFO_DATA devInfoData = new SP_DEVINFO_DATA();
                devInfoData.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));

                uint index = 0;
                while (SetupDiEnumDeviceInfo(devInfoSet, index++, ref devInfoData))
                {
                    StringBuilder idBuf = new StringBuilder(256);
                    uint reqSize;
                    if (!SetupDiGetDeviceInstanceId(devInfoSet, ref devInfoData, idBuf, (uint)idBuf.Capacity, out reqSize))
                    {
                        continue;
                    }

                    string instanceId = idBuf.ToString();
                    if (instanceId.IndexOf("VID_045E", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    if (instanceId.IndexOf("PID_0B12", StringComparison.OrdinalIgnoreCase) >= 0) return "Xbox Series X|S Controller (USB)";
                    if (instanceId.IndexOf("PID_0B00", StringComparison.OrdinalIgnoreCase) >= 0) return "Xbox Elite Series 2 (USB)";
                    if (instanceId.IndexOf("PID_02EA", StringComparison.OrdinalIgnoreCase) >= 0) return "Xbox One S Controller (USB)";
                    if (instanceId.IndexOf("PID_02DD", StringComparison.OrdinalIgnoreCase) >= 0) return "Xbox One Controller (USB)";
                    if (instanceId.IndexOf("PID_028E", StringComparison.OrdinalIgnoreCase) >= 0) return "Xbox 360 Controller (USB)";

                    string name = GetDeviceRegistryString(devInfoSet, ref devInfoData, 0x0000000C)
                               ?? GetDeviceRegistryString(devInfoSet, ref devInfoData, 0x00000000);
                    if (!string.IsNullOrEmpty(name) && name.IndexOf("Xbox", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return name;
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devInfoSet);
            }

            return null;
        }

        private static string GetDeviceRegistryString(IntPtr devInfoSet, ref SP_DEVINFO_DATA devInfoData, uint property)
        {
            byte[] buffer = new byte[512];
            uint regType;
            uint reqSize;
            if (SetupDiGetDeviceRegistryProperty(devInfoSet, ref devInfoData, property, out regType, buffer, (uint)buffer.Length, out reqSize))
            {
                string val = Encoding.Unicode.GetString(buffer, 0, (int)reqSize).TrimEnd('\0');
                if (!string.IsNullOrEmpty(val))
                {
                    return val;
                }
            }
            return null;
        }

        internal static bool IsSteamXboxSupportEnabled(string configVdfContent)
        {
            if (string.IsNullOrEmpty(configVdfContent)) return false;
            return configVdfContent.IndexOf("\"controller_xbox_support\"\t\t\"1\"", StringComparison.OrdinalIgnoreCase) >= 0
                || configVdfContent.IndexOf("\"controller_xbox_support\" \"1\"", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool EvaluateBatteryAlert(int? exactPercent, byte xinputLevel, bool isWired, ref bool notifiedLow)
        {
            if (isWired)
            {
                notifiedLow = false;
                return false;
            }

            bool isLow = exactPercent.HasValue
                ? exactPercent.Value <= LOW_THRESHOLD_PERCENT
                : (xinputLevel <= BATTERY_LEVEL_LOW);

            bool isRecovered = exactPercent.HasValue
                ? exactPercent.Value >= RESET_THRESHOLD_PERCENT
                : (xinputLevel > BATTERY_LEVEL_LOW);

            if (isLow && !notifiedLow)
            {
                return true;
            }
            if (isRecovered)
            {
                notifiedLow = false;
            }
            return false;
        }

        internal static void UpdateChargingInference(int? exactPercent, bool isWired, ref int? lastPercent, ref bool isCharging)
        {
            if (!isWired && exactPercent.HasValue)
            {
                if (lastPercent.HasValue)
                {
                    if (exactPercent.Value > lastPercent.Value)
                        isCharging = true;
                    else if (exactPercent.Value < lastPercent.Value)
                        isCharging = false;
                }
                lastPercent = exactPercent.Value;
            }
            else
            {
                lastPercent = null;
                isCharging = false;
            }
        }

        internal static string FormatTrayTooltip(string modelName, bool isWired, int? exactPercent, byte xinputLevel)
        {
            string tooltip = isWired
                ? string.Format("{0} (USB)", modelName)
                : exactPercent.HasValue
                    ? string.Format("{0}: {1}%", modelName, exactPercent.Value)
                    : string.Format("{0}: {1}", modelName, LevelText(xinputLevel));
            return tooltip.Length > 63 ? tooltip.Substring(0, 60) + "..." : tooltip;
        }

        internal class DesktopController
        {
            // XInput button bitmasks
            private const ushort DPAD_UP = 0x0001;
            private const ushort DPAD_DOWN = 0x0002;
            private const ushort DPAD_LEFT = 0x0004;
            private const ushort DPAD_RIGHT = 0x0008;
            private const ushort BTN_START = 0x0010;
            private const ushort BTN_BACK = 0x0020;
            private const ushort BTN_LS = 0x0040;
            private const ushort BTN_RS = 0x0080;
            private const ushort BTN_LB = 0x0100;
            private const ushort BTN_RB = 0x0200;
            private const ushort BTN_A = 0x1000;
            private const ushort BTN_B = 0x2000;
            private const ushort BTN_X = 0x4000;
            private const ushort BTN_Y = 0x8000;

            private const int STICK_DEADZONE = 7849;
            private const int TRIGGER_THRESHOLD = 40;

            private readonly OsdKeyboardForm _osdKeyboard;
            private readonly SynchronizationContext _uiContext;
            private volatile float _sensitivityMultiplier = 1.0f;
            private bool _prevLeftClick = false;
            private bool _prevRightClick = false;
            private bool _prevMiddleClick = false;
            private ushort _prevButtons = 0;
            private double _scrollAccumY = 0;
            private double _scrollAccumX = 0;
            private double _cursorAccumX = 0;
            private double _cursorAccumY = 0;
            private long _lastNavTickMs = 0;

            public DesktopController(OsdKeyboardForm osdKeyboard = null, SynchronizationContext uiContext = null, float sensitivityMultiplier = 1.0f)
            {
                _osdKeyboard = osdKeyboard;
                _uiContext = uiContext;
                _sensitivityMultiplier = Math.Max(0.25f, Math.Min(3.0f, sensitivityMultiplier));
            }

            public float SensitivityMultiplier
            {
                get { return _sensitivityMultiplier; }
                set { _sensitivityMultiplier = Math.Max(0.25f, Math.Min(3.0f, value)); }
            }

            private void RunOnUi(Action action)
            {
                if (_uiContext != null) _uiContext.Post(_ => action(), null);
                else action();
            }

            public static void PulseVibration(ushort leftMotor, ushort rightMotor, int durationMs)
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        XINPUT_VIBRATION vib = new XINPUT_VIBRATION { wLeftMotorSpeed = leftMotor, wRightMotorSpeed = rightMotor };
                        XInputSetState(0, ref vib);
                        Thread.Sleep(durationMs);
                        XINPUT_VIBRATION stop = new XINPUT_VIBRATION { wLeftMotorSpeed = 0, wRightMotorSpeed = 0 };
                        XInputSetState(0, ref stop);
                    }
                    catch { }
                });
            }

            public void Reset()
            {
                if (_prevLeftClick) SendMouseButton(MOUSEEVENTF_LEFTUP);
                if (_prevRightClick) SendMouseButton(MOUSEEVENTF_RIGHTUP);
                if (_prevMiddleClick) SendMouseButton(MOUSEEVENTF_MIDDLEUP);
                ReleaseHeldArrows();
                _prevLeftClick = false;
                _prevRightClick = false;
                _prevMiddleClick = false;
                _prevButtons = 0;
                _scrollAccumY = 0;
                _scrollAccumX = 0;
                _cursorAccumX = 0;
                _cursorAccumY = 0;
            }

            public void Tick(double dtSeconds = 1.0 / 60.0)
            {
                XINPUT_STATE state = new XINPUT_STATE();
                if (XInputGetState(0, ref state) != ERROR_SUCCESS)
                {
                    Reset();
                    return;
                }

                var gp = state.Gamepad;
                ushort pressed = (ushort)(gp.wButtons & ~_prevButtons);

                // Toggle compact OSD Keyboard on X
                if ((pressed & BTN_X) != 0 && _osdKeyboard != null)
                {
                    RunOnUi(() => _osdKeyboard.ToggleFromController());
                    _prevButtons = gp.wButtons;
                    return;
                }

                // If OSD Keyboard is open, route D-Pad / Stick / A / B / LB / RB / Y to it
                if (_osdKeyboard != null && _osdKeyboard.Visible)
                {
                    long nowMs = Environment.TickCount;
                    int navDx = 0, navDy = 0;
                    if ((pressed & DPAD_LEFT) != 0) navDx = -1;
                    else if ((pressed & DPAD_RIGHT) != 0) navDx = 1;
                    else if ((pressed & DPAD_UP) != 0) navDy = -1;
                    else if ((pressed & DPAD_DOWN) != 0) navDy = 1;
                    else if (nowMs - _lastNavTickMs > 170)
                    {
                        if (gp.sThumbLX < -16000) navDx = -1;
                        else if (gp.sThumbLX > 16000) navDx = 1;
                        else if (gp.sThumbLY > 16000) navDy = -1;
                        else if (gp.sThumbLY < -16000) navDy = 1;
                    }

                    if (navDx != 0 || navDy != 0)
                    {
                        _lastNavTickMs = nowMs;
                        int dx = navDx, dy = navDy;
                        RunOnUi(() => _osdKeyboard.MoveSelection(dx, dy));
                        PulseVibration(0, 9000, 20);
                    }

                    if ((pressed & BTN_A) != 0) RunOnUi(() => _osdKeyboard.ActivateSelected());
                    if ((pressed & BTN_LB) != 0) { SendKeyTap(0x08); PulseVibration(10000, 18000, 40); } // Backspace
                    if ((pressed & BTN_Y) != 0) { SendKeyTap(0x20); PulseVibration(10000, 18000, 40); }  // Space
                    if ((pressed & BTN_RB) != 0) { SendKeyTap(0x0D); PulseVibration(12000, 22000, 45); } // Enter
                    if ((pressed & BTN_START) != 0 || (pressed & BTN_B) != 0) RunOnUi(() => _osdKeyboard.HideKeyboard());

                    _prevButtons = gp.wButtons;
                    return;
                }

                // 1. Left Stick -> Mouse Cursor (sub-pixel accumulation + delta-time + configurable sensitivity)
                MoveCursorFromStick(gp.sThumbLX, gp.sThumbLY, dtSeconds, _sensitivityMultiplier);

                // 2. Right Stick -> Mouse Scroll Wheel
                ScrollFromStick(gp.sThumbRX, gp.sThumbRY, dtSeconds);

                // 3. A or RT -> Left Mouse Click (holdable)
                bool leftDown = ((gp.wButtons & BTN_A) != 0) || (gp.bRightTrigger > TRIGGER_THRESHOLD);
                if (leftDown != _prevLeftClick)
                {
                    SendMouseButton(leftDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP);
                    _prevLeftClick = leftDown;
                }

                // 4. B or LT -> Right Mouse Click (holdable)
                bool rightDown = ((gp.wButtons & BTN_B) != 0) || (gp.bLeftTrigger > TRIGGER_THRESHOLD);
                if (rightDown != _prevRightClick)
                {
                    SendMouseButton(rightDown ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP);
                    _prevRightClick = rightDown;
                }

                // 5. L3 (Left Stick Click) -> Middle Mouse Click
                bool midDown = (gp.wButtons & BTN_LS) != 0;
                if (midDown != _prevMiddleClick)
                {
                    SendMouseButton(midDown ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP);
                    _prevMiddleClick = midDown;
                }

                // 6. D-Pad -> Arrow Keys
                SyncKey(gp.wButtons, DPAD_UP, 0x26);    // VK_UP
                SyncKey(gp.wButtons, DPAD_DOWN, 0x28);  // VK_DOWN
                SyncKey(gp.wButtons, DPAD_LEFT, 0x25);  // VK_LEFT
                SyncKey(gp.wButtons, DPAD_RIGHT, 0x27); // VK_RIGHT

                // 7. Edge-triggered Steam Desktop shortcuts
                if ((pressed & BTN_Y) != 0) SendKeyTap(0x20);              // Space
                if ((pressed & BTN_START) != 0) SendKeyTap(0x1B);          // Escape
                if ((pressed & BTN_RS) != 0) SendKeyTap(0x0D);             // Enter
                if ((pressed & BTN_BACK) != 0) SendKeyCombo(0x12, 0x09);   // Alt + Tab
                if ((pressed & BTN_LB) != 0) SendKeyTap(0xA6);             // VK_BROWSER_BACK
                if ((pressed & BTN_RB) != 0) SendKeyTap(0xA7);             // VK_BROWSER_FORWARD

                _prevButtons = gp.wButtons;
            }

            private void ReleaseHeldArrows()
            {
                if ((_prevButtons & DPAD_UP) != 0) SendKey(0x26, true);
                if ((_prevButtons & DPAD_DOWN) != 0) SendKey(0x28, true);
                if ((_prevButtons & DPAD_LEFT) != 0) SendKey(0x25, true);
                if ((_prevButtons & DPAD_RIGHT) != 0) SendKey(0x27, true);
            }

            private void SyncKey(ushort buttons, ushort mask, ushort vk)
            {
                bool now = (buttons & mask) != 0;
                bool prev = (_prevButtons & mask) != 0;
                if (now != prev) SendKey(vk, !now);
            }

            internal static Point ComputeStickCursorDelta(short rawX, short rawY, double dtSeconds, ref double accumX, ref double accumY, double sensitivityMultiplier = 1.0)
            {
                double mag = Math.Sqrt((double)rawX * rawX + (double)rawY * rawY);
                if (mag <= STICK_DEADZONE)
                {
                    accumX = 0;
                    accumY = 0;
                    return Point.Empty;
                }

                double normMag = Math.Min(1.0, (mag - STICK_DEADZONE) / (32767.0 - STICK_DEADZONE));
                // Blended linear+quadratic curve (px/sec): fine precision near deadzone, fast at full tilt
                double pxPerSec = (0.20 * normMag + 0.80 * normMag * normMag) * 1200.0 * sensitivityMultiplier;
                double step = pxPerSec * Math.Min(dtSeconds, 0.05);

                accumX += (rawX / mag) * step;
                accumY += (-rawY / mag) * step; // invert Y for screen coords

                int dx = (int)Math.Truncate(accumX);
                int dy = (int)Math.Truncate(accumY);
                accumX -= dx;
                accumY -= dy;
                return new Point(dx, dy);
            }

            internal static int ComputeScrollDelta(short rawAxis, ref double accum, double dtSeconds = 1.0 / 60.0)
            {
                int absAxis = Math.Abs((int)rawAxis);
                if (absAxis <= STICK_DEADZONE) return 0;

                double norm = Math.Min(1.0, (absAxis - STICK_DEADZONE) / (32767.0 - STICK_DEADZONE));
                accum += Math.Sign((int)rawAxis) * norm * 2100.0 * Math.Min(dtSeconds, 0.05);
                int delta = (int)accum;
                if (Math.Abs(delta) >= 20)
                {
                    accum -= delta;
                    return delta;
                }
                return 0;
            }

            private void MoveCursorFromStick(short rawX, short rawY, double dtSeconds, double sensitivityMultiplier)
            {
                Point delta = ComputeStickCursorDelta(rawX, rawY, dtSeconds, ref _cursorAccumX, ref _cursorAccumY, sensitivityMultiplier);
                if (delta.X == 0 && delta.Y == 0) return;

                INPUT[] inp = new INPUT[1];
                inp[0].type = INPUT_MOUSE;
                inp[0].u.mi = new MOUSEINPUT { dx = delta.X, dy = delta.Y, dwFlags = MOUSEEVENTF_MOVE };
                SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
            }

            private void ScrollFromStick(short rawX, short rawY, double dtSeconds)
            {
                int deltaY = ComputeScrollDelta(rawY, ref _scrollAccumY, dtSeconds);
                if (deltaY != 0)
                {
                    INPUT[] inp = new INPUT[1];
                    inp[0].type = INPUT_MOUSE;
                    inp[0].u.mi = new MOUSEINPUT { mouseData = unchecked((uint)deltaY), dwFlags = MOUSEEVENTF_WHEEL };
                    SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
                }

                int deltaX = ComputeScrollDelta(rawX, ref _scrollAccumX, dtSeconds);
                if (deltaX != 0)
                {
                    INPUT[] inp = new INPUT[1];
                    inp[0].type = INPUT_MOUSE;
                    inp[0].u.mi = new MOUSEINPUT { mouseData = unchecked((uint)deltaX), dwFlags = MOUSEEVENTF_HWHEEL };
                    SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
                }
            }

            private static void SendMouseButton(uint flag)
            {
                INPUT[] inp = new INPUT[1];
                inp[0].type = INPUT_MOUSE;
                inp[0].u.mi = new MOUSEINPUT { dwFlags = flag };
                SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
            }

            private static void SendKey(ushort vk, bool keyUp)
            {
                INPUT[] inp = new INPUT[1];
                inp[0].type = INPUT_KEYBOARD;
                inp[0].u.ki = new KEYBDINPUT { wVk = vk, dwFlags = keyUp ? KEYEVENTF_KEYUP : 0 };
                SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
            }

            internal static void SendKeyTap(ushort vk)
            {
                SendKey(vk, false);
                SendKey(vk, true);
            }

            internal static void SendUnicodeChar(char ch)
            {
                INPUT[] inp = new INPUT[2];
                inp[0].type = INPUT_KEYBOARD;
                inp[0].u.ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE };
                inp[1].type = INPUT_KEYBOARD;
                inp[1].u.ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP };
                SendInput(2, inp, Marshal.SizeOf(typeof(INPUT)));
            }

            private static void SendKeyCombo(params ushort[] vks)
            {
                for (int i = 0; i < vks.Length; i++) SendKey(vks[i], false);
                for (int i = vks.Length - 1; i >= 0; i--) SendKey(vks[i], true);
            }
        }

        private class MonitorContext : ApplicationContext
        {
            private readonly NotifyIcon _trayIcon;
            private readonly System.Windows.Forms.Timer _pollTimer;
            private readonly System.Windows.Forms.Timer _idleCheckTimer;
            private readonly System.Windows.Forms.Timer _hideTimer;
            private readonly OsdKeyboardForm _osdKeyboard;
            private readonly DesktopController _desktopController;
            private readonly ToolStripMenuItem _desktopMenuItem;
            private Thread _desktopThread;
            private volatile bool _desktopThreadRunning = false;
            private readonly bool[] _notifiedLow = new bool[4];
            private readonly int?[] _lastPercent = new int?[4];
            private readonly bool[] _isChargingInferred = new bool[4];
            private bool _wasConnected = false;
            private bool _desktopModeEnabled = false;
            private volatile bool _steamPausedDesktop = false;
            private int _connectedTicks = 0;
            private IntPtr _currentHIcon = IntPtr.Zero;
            private DetailsForm _detailsForm;
            private LayoutForm _layoutForm;

            public MonitorContext(bool testMode)
            {
                _osdKeyboard = new OsdKeyboardForm();
                _desktopController = new DesktopController(_osdKeyboard, SynchronizationContext.Current, LoadSensitivityPercent() / 100.0f);

                ContextMenuStrip menu = new ContextMenuStrip();
                menu.Items.Add("Show Controller Details", null, (s, e) => ShowDetails());
                _desktopMenuItem = new ToolStripMenuItem("Desktop Control (Steam Layout)");
                _desktopMenuItem.CheckOnClick = true;
                _desktopMenuItem.Click += (s, e) => ToggleDesktopMode(_desktopMenuItem.Checked);
                menu.Items.Add(_desktopMenuItem);
                menu.Items.Add("On-Screen Keyboard...", null, (s, e) => _osdKeyboard.ShowKeyboard());
                menu.Items.Add("View Desktop Layout...", null, (s, e) => ShowLayoutMap());
                menu.Items.Add("Test Notification", null, (s, e) => ShowTestNotification());
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Exit", null, (s, e) => ExitApplication());

                _trayIcon = new NotifyIcon
                {
                    Icon = SystemIcons.Application,
                    Visible = true,
                    ContextMenuStrip = menu
                };
                _trayIcon.DoubleClick += (s, e) => ShowDetails();

                _hideTimer = new System.Windows.Forms.Timer
                {
                    Interval = 8500
                };
                _hideTimer.Tick += (s, e) =>
                {
                    _hideTimer.Stop();
                    if (!_wasConnected)
                    {
                        _trayIcon.Visible = false;
                    }
                };

                // Check connection every 5s (fast disconnect detection); full battery check every 60s (12 ticks)
                _pollTimer = new System.Windows.Forms.Timer
                {
                    Interval = 5000
                };
                _pollTimer.Tick += (s, e) =>
                {
                    if (!IsAnyControllerConnected())
                    {
                        UpdateStatus(checkAlerts: true);
                        return;
                    }
                    _connectedTicks++;
                    if (_connectedTicks >= 12)
                    {
                        _connectedTicks = 0;
                        UpdateStatus(checkAlerts: true);
                    }
                };

                // Lightweight 5s check when disconnected (1 XInputGetState call = 0% CPU)
                _idleCheckTimer = new System.Windows.Forms.Timer
                {
                    Interval = 5000
                };
                _idleCheckTimer.Tick += (s, e) =>
                {
                    if (IsAnyControllerConnected())
                    {
                        _idleCheckTimer.Stop();
                        _connectedTicks = 0;
                        UpdateStatus(checkAlerts: true);
                    }
                };

                UpdateStatus(checkAlerts: !testMode);

                if (testMode)
                {
                    _trayIcon.Visible = true;
                    ShowTestNotification();
                }
            }

            private void UpdateStatus(bool checkAlerts)
            {
                List<ControllerSnapshot> controllers = GetConnectedControllers();

                if (controllers.Count == 0)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        _notifiedLow[i] = false;
                        _lastPercent[i] = null;
                        _isChargingInferred[i] = false;
                    }

                    _pollTimer.Stop();
                    StopDesktopThread();
                    _connectedTicks = 0;
                    if (!_idleCheckTimer.Enabled) _idleCheckTimer.Start();

                    if (_wasConnected)
                    {
                        _wasConnected = false;
                        SetTrayTooltip("Xbox Controller: Disconnected");
                        if (!IsSteamRunning())
                        {
                            _trayIcon.ShowBalloonTip(5000, "Xbox Controller", "Controller disconnected.", ToolTipIcon.None);
                            _hideTimer.Stop();
                            _hideTimer.Start();
                        }
                        else
                        {
                            _trayIcon.Visible = false;
                        }
                    }
                    else
                    {
                        _trayIcon.Visible = false;
                    }

                    if (_detailsForm != null && !_detailsForm.IsDisposed && _detailsForm.Visible)
                    {
                        _detailsForm.UpdateData(controllers);
                    }
                    return;
                }

                bool justConnected = !_wasConnected;
                _hideTimer.Stop();
                _wasConnected = true;
                _idleCheckTimer.Stop();
                if (!_pollTimer.Enabled)
                {
                    _pollTimer.Start();
                }
                if (_desktopModeEnabled && !_desktopThreadRunning)
                {
                    StartDesktopThread();
                }
                foreach (var c in controllers)
                {
                    int idx = c.Slot - 1;
                    UpdateChargingInference(c.ExactPercent, c.IsWired, ref _lastPercent[idx], ref _isChargingInferred[idx]);
                    c.IsChargingInferred = _isChargingInferred[idx];
                }

                ControllerSnapshot primary = controllers[0];

                SetTrayIcon(primary.ExactPercent, primary.IsWired, primary.XInputLevel);
                if (!_trayIcon.Visible) _trayIcon.Visible = true;

                string tooltip = FormatTrayTooltip(primary.ModelName, primary.IsWired, primary.ExactPercent, primary.XInputLevel);
                SetTrayTooltip(tooltip);

                if (justConnected && checkAlerts && !IsSteamRunning())
                {
                    string connectMsg = primary.IsWired
                        ? string.Format("{0} connected (USB Powered).", primary.ModelName)
                        : primary.ExactPercent.HasValue
                            ? string.Format("{0} connected — Battery: {1}% ({2}).", primary.ModelName, primary.ExactPercent.Value, primary.ConnectionType)
                            : string.Format("{0} connected — Battery: {1} ({2}).", primary.ModelName, LevelText(primary.XInputLevel), primary.ConnectionType);
                    _trayIcon.ShowBalloonTip(5000, "Xbox Controller Connected", connectMsg, ToolTipIcon.None);
                }

                if (_detailsForm != null && !_detailsForm.IsDisposed && _detailsForm.Visible)
                {
                    _detailsForm.UpdateData(controllers);
                }

                if (!checkAlerts) return;

                foreach (var c in controllers)
                {
                    int idx = c.Slot - 1;
                    if (EvaluateBatteryAlert(c.ExactPercent, c.XInputLevel, c.IsWired, ref _notifiedLow[idx]))
                    {
                        if (!IsSteamRunning())
                        {
                            string msg = c.ExactPercent.HasValue
                                ? string.Format("{0} (Slot {1}) is at {2}%. Please charge or swap batteries.", c.ModelName, c.Slot, c.ExactPercent.Value)
                                : string.Format("{0} (Slot {1}) battery is low (below ~20%).", c.ModelName, c.Slot);

                            _trayIcon.ShowBalloonTip(8000, "Low Xbox Controller Battery", msg, ToolTipIcon.Warning);
                            _notifiedLow[idx] = true;
                        }
                    }
                }
            }

            private void ShowLayoutMap()
            {
                if (_layoutForm == null || _layoutForm.IsDisposed)
                {
                    _layoutForm = new LayoutForm(
                        () => _desktopModeEnabled,
                        () =>
                        {
                            _desktopMenuItem.Checked = !_desktopMenuItem.Checked;
                            ToggleDesktopMode(_desktopMenuItem.Checked);
                        });
                }
                _layoutForm.RefreshStatus();
                if (!_layoutForm.Visible) _layoutForm.Show();
                _layoutForm.Activate();
            }

            private void StartDesktopThread()
            {
                if (_desktopThreadRunning) return;
                _desktopThreadRunning = true;
                _desktopThread = new Thread(() =>
                {
                    timeBeginPeriod(1); // 1ms Windows timer resolution for silky-smooth ~250 Hz cursor
                    try
                    {
                        Stopwatch sw = Stopwatch.StartNew();
                        Stopwatch steamCheckSw = Stopwatch.StartNew();
                        IntPtr lastHwnd = IntPtr.Zero;
                        bool lastWindowPaused = false;
                        bool lastSteamPaused = IsSteamDesktopLayoutActive();
                        bool lastOsdOpen = false;

                        while (_desktopThreadRunning)
                        {
                            double dt = sw.Elapsed.TotalSeconds;
                            sw.Restart();

                            bool osdOpen = (_osdKeyboard != null && _osdKeyboard.Visible);
                            IntPtr fgHwnd = GetForegroundWindow();

                            // Only inspect foreground process when active window actually changes (zero hitching during movement)
                            if (fgHwnd != lastHwnd)
                            {
                                lastHwnd = fgHwnd;
                                lastWindowPaused = IsFullscreenOrBorderlessAppFocused() || IsUwpGamepadAppFocused();
                            }

                            if (steamCheckSw.ElapsedMilliseconds >= 2000)
                            {
                                steamCheckSw.Restart();
                                lastSteamPaused = IsSteamDesktopLayoutActive();
                            }

                            if (osdOpen != lastOsdOpen || ((lastWindowPaused || lastSteamPaused) && !osdOpen) != _steamPausedDesktop)
                            {
                                lastOsdOpen = osdOpen;
                                bool shouldPause = !osdOpen && (lastSteamPaused || lastWindowPaused);
                                if (shouldPause != _steamPausedDesktop)
                                {
                                    _steamPausedDesktop = shouldPause;
                                    if (_steamPausedDesktop) _desktopController.Reset();
                                }
                            }

                            if (!_steamPausedDesktop)
                            {
                                _desktopController.Tick(dt);
                            }

                            Thread.Sleep(4);
                        }
                    }
                    finally
                    {
                        timeEndPeriod(1);
                        _desktopController.Reset();
                    }
                })
                {
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal
                };
                _desktopThread.Start();
            }

            private void StopDesktopThread()
            {
                _desktopThreadRunning = false;
                if (_desktopThread != null && _desktopThread.IsAlive)
                {
                    _desktopThread.Join(200);
                    _desktopThread = null;
                }
                _desktopController.Reset();
            }

            private void ToggleDesktopMode(bool enabled)
            {
                _desktopModeEnabled = enabled;
                if (enabled && _wasConnected)
                {
                    bool osdOpen = (_osdKeyboard != null && _osdKeyboard.Visible);
                    _steamPausedDesktop = !osdOpen && (IsSteamDesktopLayoutActive() || IsFullscreenOrBorderlessAppFocused() || IsUwpGamepadAppFocused());
                    StartDesktopThread();
                }
                else
                {
                    StopDesktopThread();
                }
                if (_layoutForm != null && !_layoutForm.IsDisposed && _layoutForm.Visible)
                {
                    _layoutForm.RefreshStatus();
                }
            }

            private static bool IsUwpGamepadAppFocused()
            {
                try
                {
                    IntPtr hwnd = GetForegroundWindow();
                    if (hwnd == IntPtr.Zero) return false;

                    StringBuilder cls = new StringBuilder(64);
                    GetClassName(hwnd, cls, cls.Capacity);
                    if (cls.ToString().Equals("Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    uint pid;
                    GetWindowThreadProcessId(hwnd, out pid);
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

            private static bool IsFullscreenOrBorderlessAppFocused()
            {
                try
                {
                    int qState;
                    if (SHQueryUserNotificationState(out qState) == 0 && qState == 3) // QUNS_RUNNING_D3D_FULL_SCREEN
                    {
                        return true;
                    }

                    IntPtr hwnd = GetForegroundWindow();
                    if (hwnd == IntPtr.Zero) return false;

                    uint pid;
                    GetWindowThreadProcessId(hwnd, out pid);
                    if (pid != 0)
                    {
                        using (Process proc = Process.GetProcessById((int)pid))
                        {
                            string pName = proc.ProcessName;
                            // Don't pause Desktop Control for fullscreen browsers/video players (e.g. fullscreen YouTube)
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
                    GetClassName(hwnd, cls, cls.Capacity);
                    string className = cls.ToString();
                    if (className == "Progman" || className == "WorkerW" || className == "Shell_TrayWnd")
                    {
                        return false;
                    }

                    RECT r;
                    if (!GetWindowRect(hwnd, out r)) return false;

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

            private static bool IsSteamRunning()
            {
                return Process.GetProcessesByName("steam").Length > 0;
            }

            private static bool IsSteamDesktopLayoutActive()
            {
                if (!IsSteamRunning()) return false;
                try
                {
                    object steamPathObj = Microsoft.Win32.Registry.GetValue(
                        @"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null);
                    string steamPath = steamPathObj as string;
                    if (string.IsNullOrEmpty(steamPath)) return false;

                    string vdfPath = System.IO.Path.Combine(steamPath.Replace('/', '\\'), "config", "config.vdf");
                    if (!System.IO.File.Exists(vdfPath)) return false;

                    return IsSteamXboxSupportEnabled(System.IO.File.ReadAllText(vdfPath));
                }
                catch
                {
                    return false;
                }
            }

            private void ShowTestNotification()
            {
                var controllers = GetConnectedControllers();
                string msg;
                if (controllers.Count == 0)
                {
                    msg = "No Xbox controller currently connected.";
                }
                else
                {
                    var c = controllers[0];
                    msg = c.IsWired
                        ? string.Format("{0} connected via USB Cable.", c.ModelName)
                        : c.ExactPercent.HasValue
                            ? string.Format("{0} — Battery at {1}% ({2}).", c.ModelName, c.ExactPercent.Value, c.ConnectionType)
                            : string.Format("{0} — Battery: {1} ({2}).", c.ModelName, LevelText(c.XInputLevel), c.ConnectionType);
                }
                _trayIcon.ShowBalloonTip(6000, "Xbox Battery Monitor", msg, ToolTipIcon.Info);
            }

            private static int LoadSensitivityPercent()
            {
                try
                {
                    object val = Microsoft.Win32.Registry.GetValue(
                        @"HKEY_CURRENT_USER\Software\XboxBatteryMonitor", "CursorSensitivity", 100);
                    if (val is int)
                    {
                        int pct = (int)val;
                        return Math.Max(25, Math.Min(300, pct));
                    }
                }
                catch { }
                return 100;
            }

            private static void SaveSensitivityPercent(int pct)
            {
                try
                {
                    Microsoft.Win32.Registry.SetValue(
                        @"HKEY_CURRENT_USER\Software\XboxBatteryMonitor", "CursorSensitivity", pct, Microsoft.Win32.RegistryValueKind.DWord);
                }
                catch { }
            }

            private void ShowDetails()
            {
                if (_detailsForm == null || _detailsForm.IsDisposed)
                {
                    _detailsForm = new DetailsForm(
                        () => GetConnectedControllers(),
                        () => ShowTestNotification(),
                        () => ShowLayoutMap(),
                        () => (int)Math.Round(_desktopController.SensitivityMultiplier * 100),
                        pct =>
                        {
                            _desktopController.SensitivityMultiplier = pct / 100.0f;
                            SaveSensitivityPercent(pct);
                        });
                }

                var list = GetConnectedControllers();
                for (int i = 0; i < list.Count; i++)
                {
                    int idx = list[i].Slot - 1;
                    list[i].IsChargingInferred = _isChargingInferred[idx];
                }
                _detailsForm.UpdateData(list);
                if (!_detailsForm.Visible)
                {
                    _detailsForm.Show();
                }
                _detailsForm.Activate();
            }

            private void SetTrayTooltip(string text)
            {
                if (text.Length > 63) text = text.Substring(0, 60) + "...";
                _trayIcon.Text = text;
            }

            private void SetTrayIcon(int? exactPercent, bool isWired, byte xinputLevel = BATTERY_LEVEL_MEDIUM)
            {
                int fillPct = isWired ? 100
                    : exactPercent.HasValue ? Math.Max(0, Math.Min(100, exactPercent.Value))
                    : xinputLevel == BATTERY_LEVEL_FULL ? 90
                    : xinputLevel == BATTERY_LEVEL_MEDIUM ? 55
                    : xinputLevel == BATTERY_LEVEL_LOW ? 15
                    : 5;

                // Soft Win11 Fluent palette matching the native taskbar battery icon
                Color fillColor = isWired ? Color.FromArgb(125, 211, 252) // Soft sky blue (USB)
                    : fillPct <= 20 ? Color.FromArgb(248, 113, 113)       // Soft red
                    : fillPct <= 40 ? Color.FromArgb(252, 211, 77)        // Soft amber
                    : Color.FromArgb(134, 239, 172);                      // Soft mint green (matches Win11 battery)

                Color strokeColor = IsLightTaskbar() ? Color.FromArgb(32, 32, 32) : Color.FromArgb(242, 242, 242);

                using (Bitmap bmp = new Bitmap(32, 32))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);

                    // 1. Fluent-style Xbox controller silhouette (upper 2/3, centered)
                    using (GraphicsPath pad = new GraphicsPath())
                    {
                        pad.AddArc(3f, 4f, 8f, 8f, 180, 90);
                        pad.AddLine(7f, 4f, 25f, 4f);
                        pad.AddArc(21f, 4f, 8f, 8f, 270, 90);
                        pad.AddLine(29f, 8f, 30f, 16f);
                        pad.AddArc(24f, 14f, 6f, 6f, 0, 145);
                        pad.AddLine(25f, 19f, 21f, 15f);
                        pad.AddLine(21f, 15f, 11f, 15f);
                        pad.AddLine(11f, 15f, 7f, 19f);
                        pad.AddArc(2f, 14f, 6f, 6f, 35, 145);
                        pad.AddLine(2f, 16f, 3f, 8f);
                        pad.CloseFigure();

                        using (Pen strokePen = new Pen(strokeColor, 2.0f) { LineJoin = LineJoin.Round })
                        {
                            g.DrawPath(strokePen, pad);
                        }
                    }

                    // Subtle thumbstick & button accents
                    using (SolidBrush accentBrush = new SolidBrush(strokeColor))
                    {
                        g.FillEllipse(accentBrush, 7.5f, 8.2f, 2.8f, 2.8f);   // Left stick
                        g.FillEllipse(accentBrush, 21.5f, 8.2f, 2.8f, 2.8f);  // ABXY cluster
                    }

                    // 2. Rounded Win11-style battery pill below the controller
                    RectangleF pillRect = new RectangleF(4f, 22.5f, 22f, 6.5f);
                    using (GraphicsPath pillPath = CreateRoundedRect(pillRect, 2.5f))
                    {
                        using (Pen pillPen = new Pen(strokeColor, 1.6f))
                        {
                            g.DrawPath(pillPen, pillPath);
                        }
                    }

                    // Battery terminal nub
                    using (SolidBrush nubBrush = new SolidBrush(strokeColor))
                    {
                        g.FillRectangle(nubBrush, 27f, 24.5f, 1.8f, 2.6f);
                    }

                    // Battery fill bar inside pill
                    float maxInnerW = 18.4f;
                    float fillW = Math.Max(2.5f, maxInnerW * (fillPct / 100.0f));
                    RectangleF fillRect = new RectangleF(5.8f, 24.1f, fillW, 3.3f);
                    using (GraphicsPath fillPath = CreateRoundedRect(fillRect, 1.2f))
                    using (SolidBrush fillBrush = new SolidBrush(fillColor))
                    {
                        g.FillPath(fillBrush, fillPath);
                    }

                    IntPtr newHIcon = bmp.GetHicon();
                    Icon newIcon = Icon.FromHandle(newHIcon);
                    _trayIcon.Icon = newIcon;

                    if (_currentHIcon != IntPtr.Zero)
                    {
                        DestroyIcon(_currentHIcon);
                    }
                    _currentHIcon = newHIcon;
                }
            }

            internal static GraphicsPath CreateRoundedRect(RectangleF bounds, float radius)
            {
                GraphicsPath path = new GraphicsPath();
                float d = radius * 2f;
                path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
                path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
                path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
                path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }

            private static bool IsLightTaskbar()
            {
                try
                {
                    object val = Microsoft.Win32.Registry.GetValue(
                        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                        "SystemUsesLightTheme", 0);
                    return val is int && (int)val == 1;
                }
                catch
                {
                    return false;
                }
            }

            private void ExitApplication()
            {
                _pollTimer.Stop();
                _idleCheckTimer.Stop();
                _hideTimer.Stop();
                StopDesktopThread();
                _trayIcon.Visible = false;
                if (_currentHIcon != IntPtr.Zero)
                {
                    DestroyIcon(_currentHIcon);
                    _currentHIcon = IntPtr.Zero;
                }
                Application.Exit();
            }
        }

        private static string LevelText(byte level)
        {
            switch (level)
            {
                case BATTERY_LEVEL_FULL: return "Full";
                case BATTERY_LEVEL_MEDIUM: return "Medium";
                case BATTERY_LEVEL_LOW: return "Low";
                default: return "Empty";
            }
        }

        private class FluentCard : Panel
        {
            public Color CardColor { get; set; }
            public Color BorderColor { get; set; }
            public int CornerRadius { get; set; }

            public FluentCard()
            {
                DoubleBuffered = true;
                CardColor = Color.FromArgb(43, 43, 46);
                BorderColor = Color.FromArgb(58, 58, 62);
                CornerRadius = 8;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                using (GraphicsPath path = MonitorContext.CreateRoundedRect(rect, CornerRadius))
                using (SolidBrush fill = new SolidBrush(CardColor))
                using (Pen border = new Pen(BorderColor, 1f))
                {
                    e.Graphics.FillPath(fill, path);
                    e.Graphics.DrawPath(border, path);
                }
            }
        }

        private class FluentProgressBar : Control
        {
            private int _value = 0;
            public int Value
            {
                get { return _value; }
                set { _value = Math.Max(0, Math.Min(100, value)); Invalidate(); }
            }
            public Color TrackColor { get; set; }
            public Color FillColor { get; set; }

            public FluentProgressBar()
            {
                DoubleBuffered = true;
                TrackColor = Color.FromArgb(62, 62, 66);
                FillColor = Color.FromArgb(134, 239, 172);
                Height = 8;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float r = Height / 2f;
                RectangleF trackRect = new RectangleF(0, 0, Width - 1, Height - 1);
                using (GraphicsPath trackPath = MonitorContext.CreateRoundedRect(trackRect, r))
                using (SolidBrush trackBrush = new SolidBrush(TrackColor))
                {
                    e.Graphics.FillPath(trackBrush, trackPath);
                }

                if (_value > 0)
                {
                    float fillW = Math.Max(Height, (Width - 1) * (_value / 100f));
                    RectangleF fillRect = new RectangleF(0, 0, fillW, Height - 1);
                    using (GraphicsPath fillPath = MonitorContext.CreateRoundedRect(fillRect, r))
                    using (SolidBrush fillBrush = new SolidBrush(FillColor))
                    {
                        e.Graphics.FillPath(fillBrush, fillPath);
                    }
                }
            }
        }

        private class FluentButton : Control
        {
            private bool _hover = false;
            private bool _pressed = false;
            public bool IsPrimary { get; set; }

            public FluentButton()
            {
                DoubleBuffered = true;
                Cursor = Cursors.Hand;
                Font = new Font("Segoe UI Variable Text", 9f, FontStyle.Regular);
                Size = new Size(92, 32);
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Color bg = IsPrimary
                    ? (_pressed ? Color.FromArgb(0, 90, 158) : _hover ? Color.FromArgb(24, 131, 215) : Color.FromArgb(0, 120, 212))
                    : (_pressed ? Color.FromArgb(45, 45, 48) : _hover ? Color.FromArgb(62, 62, 66) : Color.FromArgb(52, 52, 56));
                Color border = IsPrimary ? bg : Color.FromArgb(72, 72, 76);

                RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                using (GraphicsPath path = MonitorContext.CreateRoundedRect(rect, 6f))
                using (SolidBrush fill = new SolidBrush(bg))
                using (Pen pen = new Pen(border, 1f))
                {
                    e.Graphics.FillPath(fill, path);
                    e.Graphics.DrawPath(pen, path);
                }

                TextRenderer.DrawText(
                    e.Graphics,
                    Text,
                    Font,
                    ClientRectangle,
                    ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        private class FluentSlider : Control
        {
            private int _min = 25;
            private int _max = 300;
            private int _value = 100;
            private bool _dragging = false;

            public event Action<int> ValueChanged;

            public int Minimum { get { return _min; } set { _min = value; Invalidate(); } }
            public int Maximum { get { return _max; } set { _max = value; Invalidate(); } }
            public int Value
            {
                get { return _value; }
                set
                {
                    int clamped = Math.Max(_min, Math.Min(_max, value));
                    if (clamped != _value)
                    {
                        _value = clamped;
                        Invalidate();
                    }
                }
            }

            public FluentSlider()
            {
                DoubleBuffered = true;
                Cursor = Cursors.Hand;
                Height = 22;
            }

            private void UpdateFromMouse(int mouseX)
            {
                float pad = 8f;
                float usable = Math.Max(1f, Width - pad * 2);
                float ratio = Math.Max(0f, Math.Min(1f, (mouseX - pad) / usable));
                int newVal = _min + (int)Math.Round(ratio * (_max - _min));
                // Snap to nearest 5% for clean feel
                newVal = Math.Max(_min, Math.Min(_max, ((newVal + 2) / 5) * 5));
                if (newVal != _value)
                {
                    _value = newVal;
                    Invalidate();
                    if (ValueChanged != null) ValueChanged(_value);
                }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) { _dragging = true; UpdateFromMouse(e.X); }
                base.OnMouseDown(e);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                if (_dragging) UpdateFromMouse(e.X);
                base.OnMouseMove(e);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                _dragging = false;
                base.OnMouseUp(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                float pad = 8f;
                float trackH = 4f;
                float trackY = (Height - trackH) / 2f;
                float usable = Math.Max(1f, Width - pad * 2);
                float ratio = (_value - _min) / (float)Math.Max(1, _max - _min);
                float thumbX = pad + ratio * usable;

                using (GraphicsPath trackPath = MonitorContext.CreateRoundedRect(new RectangleF(pad, trackY, usable, trackH), 2f))
                using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(66, 66, 72)))
                {
                    g.FillPath(trackBrush, trackPath);
                }

                if (thumbX > pad)
                {
                    using (GraphicsPath fillPath = MonitorContext.CreateRoundedRect(new RectangleF(pad, trackY, thumbX - pad, trackH), 2f))
                    using (SolidBrush fillBrush = new SolidBrush(Color.FromArgb(0, 120, 212)))
                    {
                        g.FillPath(fillBrush, fillPath);
                    }
                }

                float r = 7f;
                using (SolidBrush outer = new SolidBrush(Color.White))
                using (SolidBrush inner = new SolidBrush(Color.FromArgb(0, 120, 212)))
                {
                    g.FillEllipse(outer, thumbX - r, Height / 2f - r, r * 2, r * 2);
                    g.FillEllipse(inner, thumbX - 3.5f, Height / 2f - 3.5f, 7f, 7f);
                }
            }
        }

        private class DetailsForm : Form
        {
            private readonly Func<List<ControllerSnapshot>> _refreshProvider;
            private readonly Action _onTestNotify;
            private readonly Action _onShowLayout;
            private readonly Func<int> _getSensitivity;
            private readonly Action<int> _setSensitivity;
            private readonly Label _lblModel;
            private readonly Label _lblConnection;
            private readonly Label _lblBatteryValue;
            private readonly Label _lblBatterySub;
            private readonly Label _lblSensValue;
            private readonly FluentSlider _sensSlider;
            private readonly FluentProgressBar _progressBar;

            public DetailsForm(
                Func<List<ControllerSnapshot>> refreshProvider,
                Action onTestNotify,
                Action onShowLayout,
                Func<int> getSensitivity,
                Action<int> setSensitivity)
            {
                _refreshProvider = refreshProvider;
                _onTestNotify = onTestNotify;
                _onShowLayout = onShowLayout;
                _getSensitivity = getSensitivity;
                _setSensitivity = setSensitivity;

                Text = "Xbox Controller";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                ShowIcon = false;
                MaximizeBox = false;
                MinimizeBox = false;
                AutoScaleMode = AutoScaleMode.Dpi;
                StartPosition = FormStartPosition.Manual;
                ClientSize = new Size(376, 290);
                BackColor = Color.FromArgb(32, 32, 34);
                ForeColor = Color.FromArgb(245, 245, 247);
                Font = new Font("Segoe UI Variable Text", 9.5f);

                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.Right - Width - 14, wa.Bottom - Height - 14);

                FluentCard headerCard = new FluentCard
                {
                    Location = new Point(14, 14),
                    Size = new Size(348, 62)
                };

                Label lblIcon = new Label
                {
                    Text = "\uE7FC", // Segoe Fluent Icons: Game / Xbox Controller
                    Location = new Point(12, 11),
                    Size = new Size(40, 40),
                    Font = new Font("Segoe Fluent Icons", 20f, FontStyle.Regular),
                    ForeColor = Color.FromArgb(125, 211, 252),
                    BackColor = Color.FromArgb(52, 52, 58),
                    TextAlign = ContentAlignment.MiddleCenter
                };

                _lblModel = new Label
                {
                    Location = new Point(62, 10),
                    Size = new Size(246, 24),
                    Font = new Font("Segoe UI Variable Display", 11f, FontStyle.Bold),
                    ForeColor = Color.White,
                    BackColor = Color.Transparent
                };

                _lblConnection = new Label
                {
                    Location = new Point(62, 34),
                    Size = new Size(246, 18),
                    Font = new Font("Segoe UI Variable Text", 8.75f),
                    ForeColor = Color.FromArgb(170, 170, 178),
                    BackColor = Color.Transparent
                };
                headerCard.Controls.Add(lblIcon);
                headerCard.Controls.Add(_lblModel);
                headerCard.Controls.Add(_lblConnection);

                FluentCard batteryCard = new FluentCard
                {
                    Location = new Point(14, 86),
                    Size = new Size(348, 82)
                };

                _lblBatteryValue = new Label
                {
                    Location = new Point(14, 10),
                    Size = new Size(292, 22),
                    Font = new Font("Segoe UI Variable Display", 10.5f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(134, 239, 172),
                    BackColor = Color.Transparent
                };

                _progressBar = new FluentProgressBar
                {
                    Location = new Point(14, 38),
                    Size = new Size(292, 8)
                };

                _lblBatterySub = new Label
                {
                    Location = new Point(14, 54),
                    Size = new Size(292, 18),
                    Font = new Font("Segoe UI Variable Text", 8.5f),
                    ForeColor = Color.FromArgb(170, 170, 178),
                    BackColor = Color.Transparent
                };
                batteryCard.Controls.Add(_lblBatteryValue);
                batteryCard.Controls.Add(_progressBar);
                batteryCard.Controls.Add(_lblBatterySub);

                FluentCard sensCard = new FluentCard
                {
                    Location = new Point(14, 176),
                    Size = new Size(348, 58)
                };

                int initialSens = _getSensitivity();
                _lblSensValue = new Label
                {
                    Text = string.Format("Desktop Cursor Sensitivity: {0}%", initialSens),
                    Location = new Point(14, 8),
                    Size = new Size(320, 18),
                    Font = new Font("Segoe UI Variable Text", 8.75f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(220, 220, 228),
                    BackColor = Color.Transparent
                };

                _sensSlider = new FluentSlider
                {
                    Location = new Point(12, 29),
                    Size = new Size(324, 22),
                    Minimum = 25,
                    Maximum = 300,
                    Value = initialSens
                };
                _sensSlider.ValueChanged += val =>
                {
                    _lblSensValue.Text = string.Format("Desktop Cursor Sensitivity: {0}%", val);
                    _setSensitivity(val);
                };
                sensCard.Controls.Add(_lblSensValue);
                sensCard.Controls.Add(_sensSlider);

                FluentButton btnRefresh = new FluentButton
                {
                    Text = "Refresh",
                    Location = new Point(14, 246),
                    Size = new Size(78, 32),
                    ForeColor = Color.White
                };
                btnRefresh.Click += (s, e) => UpdateData(_refreshProvider());

                FluentButton btnTest = new FluentButton
                {
                    Text = "Test Alert",
                    Location = new Point(98, 246),
                    Size = new Size(84, 32),
                    ForeColor = Color.White
                };
                btnTest.Click += (s, e) => _onTestNotify();

                FluentButton btnLayout = new FluentButton
                {
                    Text = "Layout",
                    Location = new Point(188, 246),
                    Size = new Size(78, 32),
                    ForeColor = Color.White
                };
                btnLayout.Click += (s, e) => _onShowLayout();

                FluentButton btnClose = new FluentButton
                {
                    Text = "Close",
                    Location = new Point(284, 246),
                    Size = new Size(78, 32),
                    ForeColor = Color.White,
                    IsPrimary = true
                };
                btnClose.Click += (s, e) => Hide();

                Controls.AddRange(new Control[] { headerCard, batteryCard, sensCard, btnRefresh, btnTest, btnLayout, btnClose });

                FormClosing += (s, e) =>
                {
                    if (e.CloseReason == CloseReason.UserClosing)
                    {
                        e.Cancel = true;
                        Hide();
                    }
                };
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                try
                {
                    int dark = 1;
                    DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                    int round = DWMWCP_ROUND;
                    DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
                    int borderColor = 0x00424246;
                    DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
                }
                catch { }
            }

            public void UpdateData(List<ControllerSnapshot> controllers)
            {
                if (controllers == null || controllers.Count == 0)
                {
                    _lblModel.Text = "No Xbox Controller Connected";
                    _lblConnection.Text = "Disconnected";
                    _lblBatteryValue.Text = "Battery: --";
                    _lblBatteryValue.ForeColor = Color.Gray;
                    _progressBar.Value = 0;
                    _lblBatterySub.Text = "Monitor will auto-exit shortly.";
                    return;
                }

                var c = controllers[0];
                _lblModel.Text = c.ModelName;
                _lblConnection.Text = string.Format("{0}  •  Controller #{1}", c.ConnectionType, c.Slot);

                if (c.IsWired)
                {
                    _lblBatteryValue.Text = "USB Connected (Wired / Charging)";
                    _lblBatteryValue.ForeColor = Color.FromArgb(125, 211, 252);
                    _progressBar.FillColor = Color.FromArgb(125, 211, 252);
                    _progressBar.Value = 100;
                    _lblBatterySub.Text = string.Format("Status: Plugged into PC  •  {0}", c.BatteryTypeLabel);
                }
                else if (c.ExactPercent.HasValue)
                {
                    int pct = Math.Max(0, Math.Min(100, c.ExactPercent.Value));
                    Color cColor = pct <= LOW_THRESHOLD_PERCENT
                        ? Color.FromArgb(248, 113, 113)
                        : pct <= 40 ? Color.FromArgb(252, 211, 77) : Color.FromArgb(134, 239, 172);
                    _lblBatteryValue.Text = c.IsChargingInferred
                        ? string.Format("Battery: {0}%  (Charging ↑)", pct)
                        : string.Format("Battery: {0}%", pct);
                    _lblBatteryValue.ForeColor = cColor;
                    _progressBar.FillColor = cColor;
                    _progressBar.Value = pct;
                    _lblBatterySub.Text = string.Format("{0}  •  Alert at ≤ {1}%", c.BatteryTypeLabel, LOW_THRESHOLD_PERCENT);
                }
                else
                {
                    int approx = c.XInputLevel == BATTERY_LEVEL_FULL ? 90
                               : c.XInputLevel == BATTERY_LEVEL_MEDIUM ? 55
                               : c.XInputLevel == BATTERY_LEVEL_LOW ? 15 : 5;
                    Color cColor = c.XInputLevel <= BATTERY_LEVEL_LOW
                        ? Color.FromArgb(248, 113, 113) : Color.FromArgb(134, 239, 172);
                    _lblBatteryValue.Text = string.Format("Battery: {0}", LevelText(c.XInputLevel));
                    _lblBatteryValue.ForeColor = cColor;
                    _progressBar.FillColor = cColor;
                    _progressBar.Value = approx;
                    _lblBatterySub.Text = string.Format("{0}  •  Alert at ≤ {1}%", c.BatteryTypeLabel, LOW_THRESHOLD_PERCENT);
                }
            }
        }

        private class LayoutForm : Form
        {
            private readonly Func<bool> _isEnabledProvider;
            private readonly Action _onToggle;
            private readonly FluentButton _btnToggle;
            private readonly Label _lblStatus;

            public LayoutForm(Func<bool> isEnabledProvider, Action onToggle)
            {
                _isEnabledProvider = isEnabledProvider;
                _onToggle = onToggle;

                Text = "Xbox Controller — Desktop Layout (Steam Reference)";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                ShowIcon = false;
                MaximizeBox = false;
                MinimizeBox = false;
                AutoScaleMode = AutoScaleMode.Dpi;
                StartPosition = FormStartPosition.CenterScreen;
                ClientSize = new Size(760, 460);
                BackColor = Color.FromArgb(26, 26, 29);
                ForeColor = Color.White;
                Font = new Font("Segoe UI Variable Text", 9f);
                DoubleBuffered = true;

                Panel canvas = new Panel
                {
                    Location = new Point(0, 0),
                    Size = new Size(760, 404),
                    BackColor = Color.Transparent
                };
                canvas.Paint += OnCanvasPaint;

                _lblStatus = new Label
                {
                    Location = new Point(18, 416),
                    Size = new Size(380, 24),
                    Font = new Font("Segoe UI Variable Text", 9.5f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(170, 170, 178)
                };

                _btnToggle = new FluentButton
                {
                    Location = new Point(470, 412),
                    Size = new Size(170, 32),
                    ForeColor = Color.White
                };
                _btnToggle.Click += (s, e) => { _onToggle(); RefreshStatus(); };

                FluentButton btnClose = new FluentButton
                {
                    Text = "Close",
                    Location = new Point(652, 412),
                    Size = new Size(90, 32),
                    ForeColor = Color.White,
                    IsPrimary = true
                };
                btnClose.Click += (s, e) => Hide();

                Controls.AddRange(new Control[] { canvas, _lblStatus, _btnToggle, btnClose });

                FormClosing += (s, e) =>
                {
                    if (e.CloseReason == CloseReason.UserClosing)
                    {
                        e.Cancel = true;
                        Hide();
                    }
                };
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                try
                {
                    int dark = 1;
                    DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                    int round = DWMWCP_ROUND;
                    DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
                    int borderColor = 0x00424246;
                    DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
                }
                catch { }
            }

            public void RefreshStatus()
            {
                bool on = _isEnabledProvider();
                _lblStatus.Text = on ? "Desktop Control: ON  (Auto-pauses in Steam / Fullscreen)" : "Desktop Control: OFF";
                _lblStatus.ForeColor = on ? Color.FromArgb(134, 239, 172) : Color.FromArgb(170, 170, 178);
                _btnToggle.Text = on ? "Disable Desktop Control" : "Enable Desktop Control";
            }

            private void OnCanvasPaint(object sender, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                using (Font titleFont = new Font("Segoe UI Variable Display", 12f, FontStyle.Bold))
                using (SolidBrush titleBrush = new SolidBrush(Color.White))
                {
                    g.DrawString("Official Steam Desktop Layout (Xbox Controller)", titleFont, titleBrush, 18, 14);
                }

                float cx = 380f;
                float cy = 212f;

                using (SolidBrush trigBrush = new SolidBrush(Color.FromArgb(54, 54, 60)))
                using (Pen trigPen = new Pen(Color.FromArgb(95, 95, 105), 1.5f))
                {
                    using (GraphicsPath lt = MonitorContext.CreateRoundedRect(new RectangleF(cx - 108, cy - 118, 44, 24), 6))
                    { g.FillPath(trigBrush, lt); g.DrawPath(trigPen, lt); }
                    using (GraphicsPath rt = MonitorContext.CreateRoundedRect(new RectangleF(cx + 64, cy - 118, 44, 24), 6))
                    { g.FillPath(trigBrush, rt); g.DrawPath(trigPen, rt); }
                }

                using (SolidBrush bmpBrush = new SolidBrush(Color.FromArgb(62, 62, 68)))
                using (Pen bmpPen = new Pen(Color.FromArgb(105, 105, 115), 1.5f))
                {
                    using (GraphicsPath lb = MonitorContext.CreateRoundedRect(new RectangleF(cx - 116, cy - 90, 62, 18), 7))
                    { g.FillPath(bmpBrush, lb); g.DrawPath(bmpPen, lb); }
                    using (GraphicsPath rb = MonitorContext.CreateRoundedRect(new RectangleF(cx + 54, cy - 90, 62, 18), 7))
                    { g.FillPath(bmpBrush, rb); g.DrawPath(bmpPen, rb); }
                }

                using (GraphicsPath body = new GraphicsPath())
                {
                    body.AddArc(cx - 132, cy - 76, 56, 56, 180, 90);
                    body.AddLine(cx - 104, cy - 76, cx + 104, cy - 76);
                    body.AddArc(cx + 76, cy - 76, 56, 56, 270, 90);
                    body.AddLine(cx + 132, cy - 48, cx + 148, cy + 66);
                    body.AddArc(cx + 96, cy + 44, 52, 52, 0, 155);
                    body.AddLine(cx + 106, cy + 88, cx + 62, cy + 46);
                    body.AddLine(cx + 62, cy + 46, cx - 62, cy + 46);
                    body.AddLine(cx - 62, cy + 46, cx - 106, cy + 88);
                    body.AddArc(cx - 148, cy + 44, 52, 52, 25, 155);
                    body.AddLine(cx - 148, cy + 66, cx - 132, cy - 48);
                    body.CloseFigure();

                    using (SolidBrush bodyBrush = new SolidBrush(Color.FromArgb(44, 44, 48)))
                    using (Pen bodyPen = new Pen(Color.FromArgb(110, 110, 120), 2f) { LineJoin = LineJoin.Round })
                    {
                        g.FillPath(bodyBrush, body);
                        g.DrawPath(bodyPen, body);
                    }
                }

                using (SolidBrush guideBrush = new SolidBrush(Color.FromArgb(72, 72, 80)))
                using (Pen guidePen = new Pen(Color.FromArgb(180, 180, 190), 1.5f))
                {
                    g.FillEllipse(guideBrush, cx - 12, cy - 58, 24, 24);
                    g.DrawEllipse(guidePen, cx - 12, cy - 58, 24, 24);
                }

                using (SolidBrush smallBtn = new SolidBrush(Color.FromArgb(80, 80, 88)))
                {
                    g.FillEllipse(smallBtn, cx - 36, cy - 30, 14, 14);
                    g.FillEllipse(smallBtn, cx + 22, cy - 30, 14, 14);
                }

                DrawThumbstick(g, cx - 78, cy - 26, 20);
                DrawThumbstick(g, cx + 42, cy + 12, 20);

                using (SolidBrush dpadBrush = new SolidBrush(Color.FromArgb(68, 68, 74)))
                using (Pen dpadPen = new Pen(Color.FromArgb(120, 120, 130), 1.2f))
                {
                    g.FillRectangle(dpadBrush, cx - 48, cy - 4, 14, 38);
                    g.FillRectangle(dpadBrush, cx - 60, cy + 8, 38, 14);
                    g.DrawRectangle(dpadPen, cx - 48, cy - 4, 14, 38);
                    g.DrawRectangle(dpadPen, cx - 60, cy + 8, 38, 14);
                }

                DrawFaceButton(g, cx + 78, cy - 46, "Y", Color.FromArgb(250, 204, 21));
                DrawFaceButton(g, cx + 56, cy - 26, "X", Color.FromArgb(56, 189, 248));
                DrawFaceButton(g, cx + 100, cy - 26, "B", Color.FromArgb(248, 113, 113));
                DrawFaceButton(g, cx + 78, cy - 6, "A", Color.FromArgb(74, 222, 128));

                DrawCallout(g, new PointF(cx - 86, cy - 106), new PointF(18, 68), "LT", "Right Click", true);
                DrawCallout(g, new PointF(cx - 86, cy - 81), new PointF(18, 114), "LB", "Browser Back", true);
                DrawCallout(g, new PointF(cx - 78, cy - 26), new PointF(18, 168), "Left Stick", "Mouse Cursor", true);
                DrawCallout(g, new PointF(cx - 78, cy - 14), new PointF(18, 214), "L3 (Press)", "Middle Click", true);
                DrawCallout(g, new PointF(cx - 41, cy + 15), new PointF(18, 268), "D-Pad", "Arrow Keys", true);
                DrawCallout(g, new PointF(cx - 29, cy - 23), new PointF(18, 322), "View (Back)", "Alt + Tab", true);

                DrawCallout(g, new PointF(cx + 86, cy - 106), new PointF(568, 68), "RT", "Left Click", false);
                DrawCallout(g, new PointF(cx + 86, cy - 81), new PointF(568, 114), "RB", "Browser Forward", false);
                DrawCallout(g, new PointF(cx + 86, cy - 38), new PointF(568, 160), "Y", "Space", false);
                DrawCallout(g, new PointF(cx + 64, cy - 18), new PointF(568, 206), "X", "On-Screen Keyboard", false);
                DrawCallout(g, new PointF(cx + 108, cy - 18), new PointF(568, 252), "B", "Right Click", false);
                DrawCallout(g, new PointF(cx + 86, cy + 2), new PointF(568, 298), "A", "Left Click", false);
                DrawCallout(g, new PointF(cx + 52, cy + 22), new PointF(568, 344), "Right Stick / R3", "Scroll / Enter", false);
                DrawCallout(g, new PointF(cx + 29, cy - 23), new PointF(305, 368), "Menu (Start)", "Esc", false, isBottomCenter: true);
            }

            private static void DrawThumbstick(Graphics g, float x, float y, float radius)
            {
                using (SolidBrush outer = new SolidBrush(Color.FromArgb(66, 66, 74)))
                using (SolidBrush inner = new SolidBrush(Color.FromArgb(48, 48, 54)))
                using (Pen pen = new Pen(Color.FromArgb(130, 130, 140), 1.5f))
                {
                    g.FillEllipse(outer, x - radius, y - radius, radius * 2, radius * 2);
                    g.DrawEllipse(pen, x - radius, y - radius, radius * 2, radius * 2);
                    g.FillEllipse(inner, x - radius * 0.6f, y - radius * 0.6f, radius * 1.2f, radius * 1.2f);
                }
            }

            private static void DrawFaceButton(Graphics g, float x, float y, string letter, Color color)
            {
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(54, 54, 60)))
                using (Pen ring = new Pen(color, 1.8f))
                using (Font f = new Font("Segoe UI Variable Text", 7.5f, FontStyle.Bold))
                using (SolidBrush textBrush = new SolidBrush(color))
                {
                    g.FillEllipse(fill, x, y, 16, 16);
                    g.DrawEllipse(ring, x, y, 16, 16);
                    g.DrawString(letter, f, textBrush, x + 3.2f, y + 1.5f);
                }
            }

            private static void DrawCallout(Graphics g, PointF anchor, PointF boxPos, string btnName, string action, bool isLeft, bool isBottomCenter = false)
            {
                float boxW = isBottomCenter ? 150f : 174f;
                float boxH = 34f;
                RectangleF rect = new RectangleF(boxPos.X, boxPos.Y, boxW, boxH);

                PointF lineStart = isBottomCenter
                    ? new PointF(rect.X + rect.Width / 2f, rect.Y)
                    : isLeft
                        ? new PointF(rect.Right, rect.Y + boxH / 2f)
                        : new PointF(rect.X, rect.Y + boxH / 2f);

                using (Pen linePen = new Pen(Color.FromArgb(100, 125, 211, 252), 1.2f))
                {
                    g.DrawLine(linePen, lineStart, anchor);
                }
                using (SolidBrush dot = new SolidBrush(Color.FromArgb(125, 211, 252)))
                {
                    g.FillEllipse(dot, anchor.X - 2.5f, anchor.Y - 2.5f, 5, 5);
                }

                using (GraphicsPath path = MonitorContext.CreateRoundedRect(rect, 6f))
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(40, 40, 44)))
                using (Pen border = new Pen(Color.FromArgb(65, 65, 72), 1f))
                {
                    g.FillPath(bg, path);
                    g.DrawPath(border, path);
                }

                using (Font fKey = new Font("Segoe UI Variable Text", 8f, FontStyle.Bold))
                using (Font fVal = new Font("Segoe UI Variable Text", 8.25f))
                using (SolidBrush bKey = new SolidBrush(Color.FromArgb(125, 211, 252)))
                using (SolidBrush bVal = new SolidBrush(Color.White))
                {
                    g.DrawString(btnName, fKey, bKey, rect.X + 8, rect.Y + 4);
                    g.DrawString(action, fVal, bVal, rect.X + 8, rect.Y + 17);
                }
            }
        }

        internal class OsdKeyboardForm : Form
        {
            private readonly string[][] _alphaRows = new string[][]
            {
                new[] { "q","w","e","r","t","y","u","i","o","p","⌫" },
                new[] { "a","s","d","f","g","h","j","k","l","'" },
                new[] { "⇧","z","x","c","v","b","n","m",",","." },
                new[] { "123","Space","↵","✕" }
            };

            private readonly string[][] _symRows = new string[][]
            {
                new[] { "1","2","3","4","5","6","7","8","9","0","⌫" },
                new[] { "@","#","$","%","&","*","-","+","=","/" },
                new[] { "⇧","!","?",":",";","(",")","_","\"","\\" },
                new[] { "ABC","Space","↵","✕" }
            };

            private bool _shift = false;
            private bool _symbols = false;
            private int _selRow = 1;
            private int _selCol = 0;
            private readonly RectangleF[][] _keyRects = new RectangleF[4][];

            protected override bool ShowWithoutActivation { get { return true; } }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE (never steals focus from target app)
                    cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST
                    return cp;
                }
            }

            public OsdKeyboardForm()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                AutoScaleMode = AutoScaleMode.Dpi;
                StartPosition = FormStartPosition.Manual;
                ClientSize = new Size(540, 206);
                BackColor = Color.FromArgb(26, 26, 30);
                DoubleBuffered = true;

                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Bottom - Height - 18);

                MouseDown += (s, e) =>
                {
                    if (e.Button != MouseButtons.Left) return;
                    var rows = CurrentRows();
                    for (int r = 0; r < rows.Length; r++)
                    {
                        for (int c = 0; c < rows[r].Length; c++)
                        {
                            if (_keyRects[r] != null && c < _keyRects[r].Length && _keyRects[r][c].Contains(e.Location))
                            {
                                _selRow = r;
                                _selCol = c;
                                Invalidate();
                                PressKey(rows[r][c]);
                                return;
                            }
                        }
                    }
                };
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                try
                {
                    int dark = 1;
                    DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                    int round = DWMWCP_ROUND;
                    DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
                    int borderColor = 0x004A4A52;
                    DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
                }
                catch { }
            }

            public void ToggleFromController()
            {
                if (Visible) HideKeyboard();
                else ShowKeyboard();
                DesktopController.PulseVibration(0, 14000, 30);
            }

            public void ShowKeyboard()
            {
                if (!Visible) Show();
                Invalidate();
            }

            public void HideKeyboard()
            {
                if (Visible) Hide();
            }

            internal void MoveSelection(int dx, int dy)
            {
                var rows = CurrentRows();
                _selRow = (_selRow + dy + rows.Length) % rows.Length;
                int cols = rows[_selRow].Length;
                if (dx != 0)
                {
                    _selCol = (_selCol + dx + cols) % cols;
                }
                else if (_selCol >= cols)
                {
                    _selCol = cols - 1;
                }
                Invalidate();
            }

            internal int SelectedRow { get { return _selRow; } }
            internal int SelectedCol { get { return _selCol; } }

            public void ActivateSelected()
            {
                var rows = CurrentRows();
                if (_selRow >= 0 && _selRow < rows.Length && _selCol >= 0 && _selCol < rows[_selRow].Length)
                {
                    PressKey(rows[_selRow][_selCol]);
                }
            }

            private string[][] CurrentRows() { return _symbols ? _symRows : _alphaRows; }

            private void PressKey(string key)
            {
                DesktopController.PulseVibration(14000, 24000, 45);

                if (key == "⌫") { DesktopController.SendKeyTap(0x08); return; }
                if (key == "↵") { DesktopController.SendKeyTap(0x0D); return; }
                if (key == "Space") { DesktopController.SendKeyTap(0x20); return; }
                if (key == "✕") { HideKeyboard(); return; }
                if (key == "⇧") { _shift = !_shift; Invalidate(); return; }
                if (key == "123" || key == "ABC") { _symbols = !_symbols; _selCol = 0; Invalidate(); return; }

                string outStr = (_shift && !_symbols) ? key.ToUpperInvariant() : key;
                DesktopController.SendUnicodeChar(outStr[0]);
                if (_shift) { _shift = false; Invalidate(); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                var rows = CurrentRows();
                float pad = 10f;
                float gap = 5f;
                float y = 10f;
                float keyH = 38f;

                using (Font keyFont = new Font("Segoe UI Variable Text", 10.5f, FontStyle.Bold))
                using (Font hintFont = new Font("Segoe UI Variable Text", 7.8f))
                using (SolidBrush hintBrush = new SolidBrush(Color.FromArgb(145, 145, 155)))
                {
                    for (int r = 0; r < rows.Length; r++)
                    {
                        string[] row = rows[r];
                        _keyRects[r] = new RectangleF[row.Length];
                        float totalW = ClientSize.Width - pad * 2 - gap * (row.Length - 1);

                        // Compute relative weights (Space is wider)
                        float totalWeight = 0;
                        float[] weights = new float[row.Length];
                        for (int c = 0; c < row.Length; c++)
                        {
                            weights[c] = row[c] == "Space" ? 4.5f : (row[c] == "↵" || row[c] == "123" || row[c] == "ABC") ? 1.5f : 1f;
                            totalWeight += weights[c];
                        }

                        float x = pad;
                        for (int c = 0; c < row.Length; c++)
                        {
                            float w = totalW * (weights[c] / totalWeight);
                            RectangleF rect = new RectangleF(x, y, w, keyH);
                            _keyRects[r][c] = rect;

                            bool selected = (r == _selRow && c == _selCol);
                            bool activeMod = (row[c] == "⇧" && _shift);
                            Color bg = selected ? Color.FromArgb(0, 120, 212)
                                     : activeMod ? Color.FromArgb(72, 95, 130)
                                     : Color.FromArgb(46, 46, 52);
                            Color border = selected ? Color.FromArgb(125, 211, 252) : Color.FromArgb(68, 68, 76);

                            using (GraphicsPath p = MonitorContext.CreateRoundedRect(rect, 6f))
                            using (SolidBrush b = new SolidBrush(bg))
                            using (Pen pen = new Pen(border, selected ? 1.5f : 1f))
                            {
                                g.FillPath(b, p);
                                g.DrawPath(pen, p);
                            }

                            string label = (_shift && !_symbols && row[c].Length == 1) ? row[c].ToUpperInvariant() : row[c];
                            TextRenderer.DrawText(
                                g,
                                label,
                                keyFont,
                                Rectangle.Round(rect),
                                Color.White,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                            x += w + gap;
                        }
                        y += keyH + gap;
                    }

                    g.DrawString("A: Press   •   Y: Space   •   LB: Backspace   •   RB: Enter   •   B / X: Close", hintFont, hintBrush, 12f, ClientSize.Height - 20f);
                }
            }
        }
    }
}
