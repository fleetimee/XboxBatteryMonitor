using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using XboxBatteryMonitor.Battery;
using XboxBatteryMonitor.Core;

namespace XboxBatteryMonitor.UI
{
    internal class DetailsForm : Form
    {
        private readonly Func<List<ControllerSnapshot>> _refreshProvider;
        private readonly Action _onOpenSettings;
        private readonly Action _onShowLayout;
        private readonly Func<int> _getSensitivity;
        private readonly Func<int> _getLowThreshold;
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
            Action onOpenSettings,
            Action onShowLayout,
            Func<int> getSensitivity,
            Func<int> getLowThreshold,
            Action<int> setSensitivity)
        {
            _refreshProvider = refreshProvider;
            _onOpenSettings = onOpenSettings;
            _onShowLayout = onShowLayout;
            _getSensitivity = getSensitivity;
            _getLowThreshold = getLowThreshold;
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

            FluentCard headerCard = new FluentCard { Location = new Point(14, 14), Size = new Size(348, 62) };

            Label lblIcon = new Label
            {
                Text = "\uE7FC",
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
                Size = new Size(274, 24),
                Font = new Font("Segoe UI Variable Display", 11f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent
            };

            _lblConnection = new Label
            {
                Location = new Point(62, 34),
                Size = new Size(274, 18),
                Font = new Font("Segoe UI Variable Text", 8.75f),
                ForeColor = Color.FromArgb(170, 170, 178),
                BackColor = Color.Transparent
            };
            headerCard.Controls.AddRange(new Control[] { lblIcon, _lblModel, _lblConnection });

            FluentCard batteryCard = new FluentCard { Location = new Point(14, 86), Size = new Size(348, 82) };

            _lblBatteryValue = new Label
            {
                Location = new Point(14, 10),
                Size = new Size(320, 22),
                Font = new Font("Segoe UI Variable Display", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(134, 239, 172),
                BackColor = Color.Transparent
            };

            _progressBar = new FluentProgressBar { Location = new Point(14, 38), Size = new Size(320, 8) };

            _lblBatterySub = new Label
            {
                Location = new Point(14, 54),
                Size = new Size(320, 18),
                Font = new Font("Segoe UI Variable Text", 8.5f),
                ForeColor = Color.FromArgb(170, 170, 178),
                BackColor = Color.Transparent
            };
            batteryCard.Controls.AddRange(new Control[] { _lblBatteryValue, _progressBar, _lblBatterySub });

            FluentCard sensCard = new FluentCard { Location = new Point(14, 176), Size = new Size(348, 58) };

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
            sensCard.Controls.AddRange(new Control[] { _lblSensValue, _sensSlider });

            FluentButton btnRefresh = new FluentButton { Text = "Refresh", Location = new Point(14, 246), Size = new Size(78, 32), ForeColor = Color.White };
            btnRefresh.Click += (s, e) => UpdateData(_refreshProvider());

            FluentButton btnSettings = new FluentButton { Text = "Settings", Location = new Point(98, 246), Size = new Size(84, 32), ForeColor = Color.White };
            btnSettings.Click += (s, e) => _onOpenSettings();

            FluentButton btnLayout = new FluentButton { Text = "Layout", Location = new Point(188, 246), Size = new Size(78, 32), ForeColor = Color.White };
            btnLayout.Click += (s, e) => _onShowLayout();

            FluentButton btnClose = new FluentButton { Text = "Close", Location = new Point(284, 246), Size = new Size(78, 32), ForeColor = Color.White, IsPrimary = true };
            btnClose.Click += (s, e) => Hide();

            Controls.AddRange(new Control[] { headerCard, batteryCard, sensCard, btnRefresh, btnSettings, btnLayout, btnClose });

            FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                }
            };
        }

        public void SyncSensitivity(int sensPct)
        {
            _sensSlider.Value = sensPct;
            _lblSensValue.Text = string.Format("Desktop Cursor Sensitivity: {0}%", sensPct);
            UpdateData(_refreshProvider());
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            FluentTheme.ApplyWin11WindowStyle(Handle);
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
                _lblBatterySub.Text = "Waiting for controller reconnect.";
                return;
            }

            var c = controllers[0];
            _lblModel.Text = c.ModelName;
            _lblConnection.Text = string.Format("{0}  •  Controller #{1}", c.ConnectionType, c.Slot);

            int lowThreshold = _getLowThreshold();
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
                Color cColor = pct <= lowThreshold
                    ? Color.FromArgb(248, 113, 113)
                    : pct <= 40 ? Color.FromArgb(252, 211, 77) : Color.FromArgb(134, 239, 172);
                _lblBatteryValue.Text = c.IsChargingInferred
                    ? string.Format("Battery: {0}%  (Charging ↑)", pct)
                    : string.Format("Battery: {0}%", pct);
                _lblBatteryValue.ForeColor = cColor;
                _progressBar.FillColor = cColor;
                _progressBar.Value = pct;
                _lblBatterySub.Text = string.Format("{0}  •  Alert at ≤ {1}%", c.BatteryTypeLabel, lowThreshold);
            }
            else
            {
                int approx = c.XInputLevel == NativeMethods.BATTERY_LEVEL_FULL ? 90
                           : c.XInputLevel == NativeMethods.BATTERY_LEVEL_MEDIUM ? 55
                           : c.XInputLevel == NativeMethods.BATTERY_LEVEL_LOW ? 15 : 5;
                Color cColor = c.XInputLevel <= NativeMethods.BATTERY_LEVEL_LOW
                    ? Color.FromArgb(248, 113, 113) : Color.FromArgb(134, 239, 172);
                _lblBatteryValue.Text = string.Format("Battery: {0}", BatteryReader.LevelText(c.XInputLevel));
                _lblBatteryValue.ForeColor = cColor;
                _progressBar.FillColor = cColor;
                _progressBar.Value = approx;
                _lblBatterySub.Text = string.Format("{0}  •  Alert at ≤ {1}%", c.BatteryTypeLabel, lowThreshold);
            }
        }
    }
}
