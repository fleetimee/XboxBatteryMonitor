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

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

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

        private class MonitorContext : ApplicationContext
        {
            private readonly NotifyIcon _trayIcon;
            private readonly System.Windows.Forms.Timer _pollTimer;
            private readonly System.Windows.Forms.Timer _idleCheckTimer;
            private readonly System.Windows.Forms.Timer _hideTimer;
            private readonly bool[] _notifiedLow = new bool[4];
            private readonly int?[] _lastPercent = new int?[4];
            private readonly bool[] _isChargingInferred = new bool[4];
            private bool _wasConnected = false;
            private int _connectedTicks = 0;
            private IntPtr _currentHIcon = IntPtr.Zero;
            private DetailsForm _detailsForm;

            public MonitorContext(bool testMode)
            {
                ContextMenuStrip menu = new ContextMenuStrip();
                menu.Items.Add("Show Controller Details", null, (s, e) => ShowDetails());
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
                    Interval = 4500
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
                    _connectedTicks = 0;
                    if (!_idleCheckTimer.Enabled) _idleCheckTimer.Start();

                    if (_wasConnected)
                    {
                        _wasConnected = false;
                        SetTrayTooltip("Xbox Controller: Disconnected");
                        if (!IsSteamRunning())
                        {
                            _trayIcon.ShowBalloonTip(4000, "Xbox Controller", "Controller disconnected.", ToolTipIcon.Info);
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
                foreach (var c in controllers)
                {
                    int idx = c.Slot - 1;
                    if (!c.IsWired && c.ExactPercent.HasValue)
                    {
                        if (_lastPercent[idx].HasValue)
                        {
                            if (c.ExactPercent.Value > _lastPercent[idx].Value)
                                _isChargingInferred[idx] = true;
                            else if (c.ExactPercent.Value < _lastPercent[idx].Value)
                                _isChargingInferred[idx] = false;
                        }
                        _lastPercent[idx] = c.ExactPercent.Value;
                        c.IsChargingInferred = _isChargingInferred[idx];
                    }
                    else
                    {
                        _lastPercent[idx] = null;
                        _isChargingInferred[idx] = false;
                    }
                }

                ControllerSnapshot primary = controllers[0];

                SetTrayIcon(primary.ExactPercent, primary.IsWired, primary.XInputLevel);
                if (!_trayIcon.Visible) _trayIcon.Visible = true;

                string tooltip = primary.IsWired
                    ? string.Format("{0} (USB)", primary.ModelName)
                    : primary.ExactPercent.HasValue
                        ? string.Format("{0}: {1}%", primary.ModelName, primary.ExactPercent.Value)
                        : string.Format("{0}: {1}", primary.ModelName, LevelText(primary.XInputLevel));
                SetTrayTooltip(tooltip);

                if (justConnected && checkAlerts && !IsSteamRunning())
                {
                    string connectMsg = primary.IsWired
                        ? string.Format("{0} connected (USB Powered).", primary.ModelName)
                        : primary.ExactPercent.HasValue
                            ? string.Format("{0} connected — Battery: {1}% ({2}).", primary.ModelName, primary.ExactPercent.Value, primary.ConnectionType)
                            : string.Format("{0} connected — Battery: {1} ({2}).", primary.ModelName, LevelText(primary.XInputLevel), primary.ConnectionType);
                    _trayIcon.ShowBalloonTip(4000, "Xbox Controller Connected", connectMsg, ToolTipIcon.Info);
                }

                if (_detailsForm != null && !_detailsForm.IsDisposed && _detailsForm.Visible)
                {
                    _detailsForm.UpdateData(controllers);
                }

                if (!checkAlerts) return;

                foreach (var c in controllers)
                {
                    int idx = c.Slot - 1;
                    if (c.IsWired)
                    {
                        _notifiedLow[idx] = false;
                        continue;
                    }

                    bool isLow = c.ExactPercent.HasValue
                        ? c.ExactPercent.Value <= LOW_THRESHOLD_PERCENT
                        : (c.XInputLevel <= BATTERY_LEVEL_LOW);

                    bool isRecovered = c.ExactPercent.HasValue
                        ? c.ExactPercent.Value >= RESET_THRESHOLD_PERCENT
                        : (c.XInputLevel > BATTERY_LEVEL_LOW);

                    if (isLow && !_notifiedLow[idx])
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
                    else if (isRecovered)
                    {
                        _notifiedLow[idx] = false;
                    }
                }
            }

            private static bool IsSteamRunning()
            {
                return Process.GetProcessesByName("steam").Length > 0;
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

            private void ShowDetails()
            {
                if (_detailsForm == null || _detailsForm.IsDisposed)
                {
                    _detailsForm = new DetailsForm(
                        () => GetConnectedControllers(),
                        () => ShowTestNotification());
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

        private class DetailsForm : Form
        {
            private readonly Func<List<ControllerSnapshot>> _refreshProvider;
            private readonly Action _onTestNotify;
            private readonly Label _lblModel;
            private readonly Label _lblConnection;
            private readonly Label _lblBatteryValue;
            private readonly Label _lblBatterySub;
            private readonly FluentProgressBar _progressBar;

            public DetailsForm(Func<List<ControllerSnapshot>> refreshProvider, Action onTestNotify)
            {
                _refreshProvider = refreshProvider;
                _onTestNotify = onTestNotify;

                Text = "Xbox Controller";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                ShowIcon = false;
                MaximizeBox = false;
                MinimizeBox = false;
                AutoScaleMode = AutoScaleMode.Dpi;
                StartPosition = FormStartPosition.Manual;
                ClientSize = new Size(348, 224);
                BackColor = Color.FromArgb(32, 32, 34);
                ForeColor = Color.FromArgb(245, 245, 247);
                Font = new Font("Segoe UI Variable Text", 9.5f);

                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.Right - Width - 14, wa.Bottom - Height - 14);

                FluentCard headerCard = new FluentCard
                {
                    Location = new Point(14, 14),
                    Size = new Size(320, 62)
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
                    Size = new Size(320, 82)
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

                FluentButton btnRefresh = new FluentButton
                {
                    Text = "Refresh",
                    Location = new Point(14, 180),
                    Size = new Size(92, 32),
                    ForeColor = Color.White
                };
                btnRefresh.Click += (s, e) => UpdateData(_refreshProvider());

                FluentButton btnTest = new FluentButton
                {
                    Text = "Test Alert",
                    Location = new Point(114, 180),
                    Size = new Size(98, 32),
                    ForeColor = Color.White
                };
                btnTest.Click += (s, e) => _onTestNotify();

                FluentButton btnClose = new FluentButton
                {
                    Text = "Close",
                    Location = new Point(242, 180),
                    Size = new Size(92, 32),
                    ForeColor = Color.White,
                    IsPrimary = true
                };
                btnClose.Click += (s, e) => Hide();

                Controls.AddRange(new Control[] { headerCard, batteryCard, btnRefresh, btnTest, btnClose });

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
    }
}
