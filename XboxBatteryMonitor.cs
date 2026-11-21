using System;
using System.Collections.Generic;
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

        [STAThread]
        static void Main(string[] args)
        {
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

                // Wait briefly on startup in case PnP event fired slightly before XInput is ready
                if (!testMode)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        if (IsAnyControllerConnected()) break;
                        Thread.Sleep(1000);
                    }
                }

                if (!IsAnyControllerConnected() && !testMode)
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
            private readonly bool[] _notifiedLow = new bool[4];
            private int _disconnectedChecks = 0;
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
                    Visible = true,
                    ContextMenuStrip = menu
                };
                _trayIcon.DoubleClick += (s, e) => ShowDetails();

                UpdateStatus(checkAlerts: !testMode);

                if (testMode)
                {
                    ShowTestNotification();
                }

                _pollTimer = new System.Windows.Forms.Timer
                {
                    Interval = POLL_INTERVAL_MS
                };
                _pollTimer.Tick += (s, e) => UpdateStatus(checkAlerts: true);
                _pollTimer.Start();
            }

            private void UpdateStatus(bool checkAlerts)
            {
                List<ControllerSnapshot> controllers = GetConnectedControllers();

                if (controllers.Count == 0)
                {
                    _disconnectedChecks++;
                    for (int i = 0; i < 4; i++) _notifiedLow[i] = false;

                    SetTrayIcon(null, false);
                    SetTrayTooltip("Xbox Controller: Disconnected");

                    if (_detailsForm != null && !_detailsForm.IsDisposed && _detailsForm.Visible)
                    {
                        _detailsForm.UpdateData(controllers);
                    }

                    if (_disconnectedChecks >= 2)
                    {
                        ExitApplication();
                    }
                    return;
                }

                _disconnectedChecks = 0;
                ControllerSnapshot primary = controllers[0];

                SetTrayIcon(primary.ExactPercent, primary.IsWired, primary.XInputLevel);

                string tooltip = primary.IsWired
                    ? string.Format("{0} (USB)", primary.ModelName)
                    : primary.ExactPercent.HasValue
                        ? string.Format("{0}: {1}%", primary.ModelName, primary.ExactPercent.Value)
                        : string.Format("{0}: {1}", primary.ModelName, LevelText(primary.XInputLevel));
                SetTrayTooltip(tooltip);

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
                        string msg = c.ExactPercent.HasValue
                            ? string.Format("{0} (Slot {1}) is at {2}%. Please charge or swap batteries.", c.ModelName, c.Slot, c.ExactPercent.Value)
                            : string.Format("{0} (Slot {1}) battery is low (below ~20%).", c.ModelName, c.Slot);

                        _trayIcon.ShowBalloonTip(8000, "Low Xbox Controller Battery", msg, ToolTipIcon.Warning);
                        _notifiedLow[idx] = true;
                    }
                    else if (isRecovered)
                    {
                        _notifiedLow[idx] = false;
                    }
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

            private void ShowDetails()
            {
                if (_detailsForm == null || _detailsForm.IsDisposed)
                {
                    _detailsForm = new DetailsForm(
                        () => GetConnectedControllers(),
                        () => ShowTestNotification());
                }

                _detailsForm.UpdateData(GetConnectedControllers());
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

            private static GraphicsPath CreateRoundedRect(RectangleF bounds, float radius)
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

        private class DetailsForm : Form
        {
            private readonly Func<List<ControllerSnapshot>> _refreshProvider;
            private readonly Action _onTestNotify;
            private readonly Label _lblModel;
            private readonly Label _lblConnection;
            private readonly Label _lblBatteryText;
            private readonly Label _lblBatteryType;
            private readonly ProgressBar _progressBar;

            public DetailsForm(Func<List<ControllerSnapshot>> refreshProvider, Action onTestNotify)
            {
                _refreshProvider = refreshProvider;
                _onTestNotify = onTestNotify;

                Text = "Xbox Controller Details";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                StartPosition = FormStartPosition.Manual;
                ClientSize = new Size(340, 220);
                BackColor = Color.FromArgb(28, 28, 30);
                ForeColor = Color.White;
                Font = new Font("Segoe UI", 9.5f);

                // Position near bottom-right above taskbar
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.Right - Width - 16, wa.Bottom - Height - 16);

                _lblModel = new Label
                {
                    Location = new Point(16, 14),
                    Size = new Size(308, 24),
                    Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                    ForeColor = Color.White
                };

                _lblConnection = new Label
                {
                    Location = new Point(16, 42),
                    Size = new Size(308, 20),
                    ForeColor = Color.FromArgb(180, 180, 185)
                };

                _lblBatteryText = new Label
                {
                    Location = new Point(16, 74),
                    Size = new Size(308, 22),
                    Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(74, 222, 128)
                };

                _progressBar = new ProgressBar
                {
                    Location = new Point(16, 100),
                    Size = new Size(308, 18),
                    Minimum = 0,
                    Maximum = 100,
                    Style = ProgressBarStyle.Continuous
                };

                _lblBatteryType = new Label
                {
                    Location = new Point(16, 126),
                    Size = new Size(308, 38),
                    ForeColor = Color.FromArgb(180, 180, 185)
                };

                Button btnRefresh = new Button
                {
                    Text = "Refresh",
                    Location = new Point(16, 174),
                    Size = new Size(90, 30),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(50, 50, 55),
                    ForeColor = Color.White
                };
                btnRefresh.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 88);
                btnRefresh.Click += (s, e) => UpdateData(_refreshProvider());

                Button btnTest = new Button
                {
                    Text = "Test Alert",
                    Location = new Point(114, 174),
                    Size = new Size(96, 30),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(50, 50, 55),
                    ForeColor = Color.White
                };
                btnTest.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 88);
                btnTest.Click += (s, e) => _onTestNotify();

                Button btnClose = new Button
                {
                    Text = "Close",
                    Location = new Point(234, 174),
                    Size = new Size(90, 30),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(50, 50, 55),
                    ForeColor = Color.White
                };
                btnClose.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 88);
                btnClose.Click += (s, e) => Hide();

                Controls.AddRange(new Control[] { _lblModel, _lblConnection, _lblBatteryText, _progressBar, _lblBatteryType, btnRefresh, btnTest, btnClose });

                FormClosing += (s, e) =>
                {
                    if (e.CloseReason == CloseReason.UserClosing)
                    {
                        e.Cancel = true;
                        Hide();
                    }
                };
            }

            public void UpdateData(List<ControllerSnapshot> controllers)
            {
                if (controllers == null || controllers.Count == 0)
                {
                    _lblModel.Text = "No Xbox Controller Connected";
                    _lblConnection.Text = "Connection: Disconnected";
                    _lblBatteryText.Text = "Battery: --";
                    _lblBatteryText.ForeColor = Color.Gray;
                    _progressBar.Value = 0;
                    _lblBatteryType.Text = "Monitor will auto-exit shortly.";
                    return;
                }

                var c = controllers[0];
                _lblModel.Text = c.ModelName;
                _lblConnection.Text = string.Format("Connection: {0}  •  Slot #{1}", c.ConnectionType, c.Slot);

                if (c.IsWired)
                {
                    _lblBatteryText.Text = "Power: USB Cable (Wired)";
                    _lblBatteryText.ForeColor = Color.FromArgb(56, 189, 248);
                    _progressBar.Value = 100;
                }
                else if (c.ExactPercent.HasValue)
                {
                    int pct = Math.Max(0, Math.Min(100, c.ExactPercent.Value));
                    _lblBatteryText.Text = string.Format("Battery: {0}%", pct);
                    _lblBatteryText.ForeColor = pct <= LOW_THRESHOLD_PERCENT
                        ? Color.FromArgb(248, 113, 113)
                        : pct <= 40 ? Color.FromArgb(251, 191, 36) : Color.FromArgb(74, 222, 128);
                    _progressBar.Value = pct;
                }
                else
                {
                    int approx = c.XInputLevel == BATTERY_LEVEL_FULL ? 90
                               : c.XInputLevel == BATTERY_LEVEL_MEDIUM ? 55
                               : c.XInputLevel == BATTERY_LEVEL_LOW ? 15 : 5;
                    _lblBatteryText.Text = string.Format("Battery: {0}", LevelText(c.XInputLevel));
                    _lblBatteryText.ForeColor = c.XInputLevel <= BATTERY_LEVEL_LOW
                        ? Color.FromArgb(248, 113, 113) : Color.FromArgb(74, 222, 128);
                    _progressBar.Value = approx;
                }

                _lblBatteryType.Text = string.Format("Type: {0}\nLow-battery alert: ≤ {1}%", c.BatteryTypeLabel, LOW_THRESHOLD_PERCENT);
            }
        }
    }
}
