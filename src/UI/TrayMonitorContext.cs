using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using XboxBatteryMonitor.Battery;
using XboxBatteryMonitor.Core;
using XboxBatteryMonitor.Desktop;

namespace XboxBatteryMonitor.UI
{
    internal class TrayMonitorContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly System.Windows.Forms.Timer _pollTimer;
        private readonly System.Windows.Forms.Timer _idleCheckTimer;
        private readonly System.Windows.Forms.Timer _hideTimer;
        private readonly OsdKeyboardForm _osdKeyboard;
        private readonly DesktopController _desktopController;
        private readonly AppSettings _settings;
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
        private SettingsForm _settingsForm;

        public TrayMonitorContext(bool testMode)
        {
            _settings = AppSettings.Load();
            _osdKeyboard = new OsdKeyboardForm(() => _settings.OsdVibration);
            _desktopController = new DesktopController(
                _osdKeyboard,
                SynchronizationContext.Current,
                _settings.Bindings,
                _settings.CursorSensitivity / 100.0f,
                _settings.ScrollSpeed / 100.0f,
                _settings.OsdVibration);

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Show Controller Details", null, (s, e) => ShowDetails());
            _desktopMenuItem = new ToolStripMenuItem("Desktop Control (Steam Layout)");
            _desktopMenuItem.CheckOnClick = true;
            _desktopMenuItem.Click += (s, e) => ToggleDesktopMode(_desktopMenuItem.Checked);
            menu.Items.Add(_desktopMenuItem);
            menu.Items.Add("On-Screen Keyboard...", null, (s, e) => _osdKeyboard.ShowKeyboard());
            menu.Items.Add("View Desktop Layout...", null, (s, e) => ShowLayoutMap());
            menu.Items.Add("Settings...", null, (s, e) => ShowSettings());
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

            _hideTimer = new System.Windows.Forms.Timer { Interval = 8500 };
            _hideTimer.Tick += (s, e) =>
            {
                _hideTimer.Stop();
                if (!_wasConnected) _trayIcon.Visible = false;
            };

            _pollTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            _pollTimer.Tick += (s, e) =>
            {
                if (!BatteryReader.IsAnyControllerConnected())
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

            _idleCheckTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            _idleCheckTimer.Tick += (s, e) =>
            {
                if (BatteryReader.IsAnyControllerConnected())
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
            List<ControllerSnapshot> controllers = BatteryReader.GetConnectedControllers();

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
                    if (!(_settings.MuteWhenSteamOpen && FocusDetector.IsSteamRunning()))
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
            if (!_pollTimer.Enabled) _pollTimer.Start();
            if (_desktopModeEnabled && !_desktopThreadRunning) StartDesktopThread();

            foreach (var c in controllers)
            {
                int idx = c.Slot - 1;
                BatteryAlertLogic.UpdateChargingInference(c.ExactPercent, c.IsWired, ref _lastPercent[idx], ref _isChargingInferred[idx]);
                c.IsChargingInferred = _isChargingInferred[idx];
            }

            ControllerSnapshot primary = controllers[0];
            SetTrayIcon(primary.ExactPercent, primary.IsWired, primary.XInputLevel);
            if (!_trayIcon.Visible) _trayIcon.Visible = true;

            string tooltip = BatteryAlertLogic.FormatTrayTooltip(primary.ModelName, primary.IsWired, primary.ExactPercent, primary.XInputLevel);
            SetTrayTooltip(tooltip);

            if (justConnected && checkAlerts && !(_settings.MuteWhenSteamOpen && FocusDetector.IsSteamRunning()))
            {
                string connectMsg = primary.IsWired
                    ? string.Format("{0} connected (USB Powered).", primary.ModelName)
                    : primary.ExactPercent.HasValue
                        ? string.Format("{0} connected — Battery: {1}% ({2}).", primary.ModelName, primary.ExactPercent.Value, primary.ConnectionType)
                        : string.Format("{0} connected — Battery: {1} ({2}).", primary.ModelName, BatteryReader.LevelText(primary.XInputLevel), primary.ConnectionType);
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
                if (BatteryAlertLogic.EvaluateBatteryAlert(c.ExactPercent, c.XInputLevel, c.IsWired, ref _notifiedLow[idx], _settings.LowBatteryThreshold))
                {
                    if (!(_settings.MuteWhenSteamOpen && FocusDetector.IsSteamRunning()))
                    {
                        string msg = c.ExactPercent.HasValue
                            ? string.Format("{0} (Slot {1}) is at {2}%. Please charge or swap batteries.", c.ModelName, c.Slot, c.ExactPercent.Value)
                            : string.Format("{0} (Slot {1}) battery is low (below ~{2}%).", c.ModelName, c.Slot, _settings.LowBatteryThreshold);

                        _trayIcon.ShowBalloonTip(8000, "Low Xbox Controller Battery", msg, ToolTipIcon.Warning);
                        _notifiedLow[idx] = true;
                    }
                }
            }
        }

        private void StartDesktopThread()
        {
            if (_desktopThreadRunning) return;
            _desktopThreadRunning = true;
            _desktopThread = new Thread(() =>
            {
                NativeMethods.timeBeginPeriod(1);
                try
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    Stopwatch steamCheckSw = Stopwatch.StartNew();
                    IntPtr lastHwnd = IntPtr.Zero;
                    bool lastWindowPaused = false;
                    bool lastSteamPaused = FocusDetector.IsSteamDesktopLayoutActive();
                    bool lastOsdOpen = false;

                    while (_desktopThreadRunning)
                    {
                        double dt = sw.Elapsed.TotalSeconds;
                        sw.Restart();

                        bool osdOpen = (_osdKeyboard != null && _osdKeyboard.Visible);
                        IntPtr fgHwnd = NativeMethods.GetForegroundWindow();

                        if (fgHwnd != lastHwnd)
                        {
                            lastHwnd = fgHwnd;
                            lastWindowPaused = FocusDetector.IsFullscreenOrBorderlessAppFocused() || FocusDetector.IsUwpGamepadAppFocused();
                        }

                        if (steamCheckSw.ElapsedMilliseconds >= 2000)
                        {
                            steamCheckSw.Restart();
                            lastSteamPaused = FocusDetector.IsSteamDesktopLayoutActive();
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
                    NativeMethods.timeEndPeriod(1);
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
                _steamPausedDesktop = !osdOpen && (FocusDetector.IsSteamDesktopLayoutActive() || FocusDetector.IsFullscreenOrBorderlessAppFocused() || FocusDetector.IsUwpGamepadAppFocused());
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
                    },
                    _settings.Bindings,
                    () => _settings.Save());
            }
            _layoutForm.RefreshStatus();
            if (!_layoutForm.Visible) _layoutForm.Show();
            _layoutForm.Activate();
        }

        private void ShowSettings()
        {
            if (_settingsForm == null || _settingsForm.IsDisposed)
            {
                _settingsForm = new SettingsForm(_settings, () =>
                {
                    _desktopController.SensitivityMultiplier = _settings.CursorSensitivity / 100.0f;
                    _desktopController.ScrollMultiplier = _settings.ScrollSpeed / 100.0f;
                    _desktopController.VibrationEnabled = _settings.OsdVibration;
                    _settings.Save();
                    if (_detailsForm != null && !_detailsForm.IsDisposed && _detailsForm.Visible)
                    {
                        _detailsForm.SyncSensitivity(_settings.CursorSensitivity);
                    }
                });
            }
            _settingsForm.LoadFromSettings();
            if (!_settingsForm.Visible) _settingsForm.Show();
            _settingsForm.Activate();
        }

        private void ShowDetails()
        {
            if (_detailsForm == null || _detailsForm.IsDisposed)
            {
                _detailsForm = new DetailsForm(
                    () => BatteryReader.GetConnectedControllers(),
                    () => ShowSettings(),
                    () => ShowLayoutMap(),
                    () => _settings.CursorSensitivity,
                    () => _settings.LowBatteryThreshold,
                    pct =>
                    {
                        _settings.CursorSensitivity = pct;
                        _desktopController.SensitivityMultiplier = pct / 100.0f;
                        _settings.Save();
                    });
            }

            var list = BatteryReader.GetConnectedControllers();
            for (int i = 0; i < list.Count; i++)
            {
                int idx = list[i].Slot - 1;
                list[i].IsChargingInferred = _isChargingInferred[idx];
            }
            _detailsForm.UpdateData(list);
            if (!_detailsForm.Visible) _detailsForm.Show();
            _detailsForm.Activate();
        }

        private void ShowTestNotification()
        {
            var controllers = BatteryReader.GetConnectedControllers();
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
                        : string.Format("{0} — Battery: {1} ({2}).", c.ModelName, BatteryReader.LevelText(c.XInputLevel), c.ConnectionType);
            }
            _trayIcon.ShowBalloonTip(6000, "Xbox Battery Monitor", msg, ToolTipIcon.Info);
        }

        private void SetTrayTooltip(string text)
        {
            if (text.Length > 63) text = text.Substring(0, 60) + "...";
            _trayIcon.Text = text;
        }

        private void SetTrayIcon(int? exactPercent, bool isWired, byte xinputLevel = NativeMethods.BATTERY_LEVEL_MEDIUM)
        {
            int fillPct = isWired ? 100
                : exactPercent.HasValue ? Math.Max(0, Math.Min(100, exactPercent.Value))
                : xinputLevel == NativeMethods.BATTERY_LEVEL_FULL ? 90
                : xinputLevel == NativeMethods.BATTERY_LEVEL_MEDIUM ? 55
                : xinputLevel == NativeMethods.BATTERY_LEVEL_LOW ? 15
                : 5;

            Color fillColor = isWired ? Color.FromArgb(125, 211, 252)
                : fillPct <= 20 ? Color.FromArgb(248, 113, 113)
                : fillPct <= 40 ? Color.FromArgb(252, 211, 77)
                : Color.FromArgb(134, 239, 172);

            Color strokeColor = IsLightTaskbar() ? Color.FromArgb(32, 32, 32) : Color.FromArgb(242, 242, 242);

            using (Bitmap bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

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

                using (SolidBrush accentBrush = new SolidBrush(strokeColor))
                {
                    g.FillEllipse(accentBrush, 7.5f, 8.2f, 2.8f, 2.8f);
                    g.FillEllipse(accentBrush, 21.5f, 8.2f, 2.8f, 2.8f);
                }

                RectangleF pillRect = new RectangleF(4f, 22.5f, 22f, 6.5f);
                using (GraphicsPath pillPath = FluentTheme.CreateRoundedRect(pillRect, 2.5f))
                using (Pen pillPen = new Pen(strokeColor, 1.6f))
                {
                    g.DrawPath(pillPen, pillPath);
                }

                using (SolidBrush nubBrush = new SolidBrush(strokeColor))
                {
                    g.FillRectangle(nubBrush, 27f, 24.5f, 1.8f, 2.6f);
                }

                float maxInnerW = 18.4f;
                float fillW = Math.Max(2.5f, maxInnerW * (fillPct / 100.0f));
                RectangleF fillRect = new RectangleF(5.8f, 24.1f, fillW, 3.3f);
                using (GraphicsPath fillPath = FluentTheme.CreateRoundedRect(fillRect, 1.2f))
                using (SolidBrush fillBrush = new SolidBrush(fillColor))
                {
                    g.FillPath(fillBrush, fillPath);
                }

                IntPtr newHIcon = bmp.GetHicon();
                Icon newIcon = Icon.FromHandle(newHIcon);
                _trayIcon.Icon = newIcon;

                if (_currentHIcon != IntPtr.Zero)
                {
                    NativeMethods.DestroyIcon(_currentHIcon);
                }
                _currentHIcon = newHIcon;
            }
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
                NativeMethods.DestroyIcon(_currentHIcon);
                _currentHIcon = IntPtr.Zero;
            }
            Application.Exit();
        }
    }
}
