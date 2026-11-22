using System;
using System.Drawing;
using System.Windows.Forms;
using XboxBatteryMonitor.Core;

namespace XboxBatteryMonitor.UI
{
    internal class SettingsForm : Form
    {
        private readonly AppSettings _settings;
        private readonly Action _onChanged;
        private readonly Label _lblCursor;
        private readonly FluentSlider _sldCursor;
        private readonly Label _lblScroll;
        private readonly FluentSlider _sldScroll;
        private readonly Label _lblLowBat;
        private readonly FluentSlider _sldLowBat;
        private readonly FluentToggle _togVib;
        private readonly FluentToggle _togSteamMute;
        private readonly FluentToggle _togAutoStart;

        public SettingsForm(AppSettings settings, Action onChanged)
        {
            _settings = settings;
            _onChanged = onChanged;

            Text = "Xbox Battery Monitor — Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ShowIcon = false;
            MaximizeBox = false;
            MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(384, 392);
            BackColor = Color.FromArgb(28, 28, 30);
            ForeColor = Color.White;
            Font = new Font("Segoe UI Variable Text", 9f);

            FluentCard sliderCard = new FluentCard { Location = new Point(14, 14), Size = new Size(356, 176) };

            _lblCursor = new Label { Location = new Point(14, 10), Size = new Size(328, 18), ForeColor = Color.White, BackColor = Color.Transparent, Font = new Font("Segoe UI Variable Text", 9f, FontStyle.Bold) };
            _sldCursor = new FluentSlider { Location = new Point(12, 30), Size = new Size(332, 22), Minimum = 25, Maximum = 300 };
            _sldCursor.ValueChanged += v => { _settings.CursorSensitivity = v; _lblCursor.Text = string.Format("Desktop Cursor Sensitivity: {0}%", v); _onChanged(); };

            _lblScroll = new Label { Location = new Point(14, 62), Size = new Size(328, 18), ForeColor = Color.White, BackColor = Color.Transparent, Font = new Font("Segoe UI Variable Text", 9f, FontStyle.Bold) };
            _sldScroll = new FluentSlider { Location = new Point(12, 82), Size = new Size(332, 22), Minimum = 25, Maximum = 300 };
            _sldScroll.ValueChanged += v => { _settings.ScrollSpeed = v; _lblScroll.Text = string.Format("Right Stick Scroll Speed: {0}%", v); _onChanged(); };

            _lblLowBat = new Label { Location = new Point(14, 114), Size = new Size(328, 18), ForeColor = Color.White, BackColor = Color.Transparent, Font = new Font("Segoe UI Variable Text", 9f, FontStyle.Bold) };
            _sldLowBat = new FluentSlider { Location = new Point(12, 134), Size = new Size(332, 22), Minimum = 10, Maximum = 30 };
            _sldLowBat.ValueChanged += v => { _settings.LowBatteryThreshold = v; _lblLowBat.Text = string.Format("Low Battery Alert Threshold: {0}%", v); _onChanged(); };

            sliderCard.Controls.AddRange(new Control[] { _lblCursor, _sldCursor, _lblScroll, _sldScroll, _lblLowBat, _sldLowBat });

            FluentCard toggleCard = new FluentCard { Location = new Point(14, 200), Size = new Size(356, 134) };

            Label lblVib = new Label { Text = "OSD Keyboard Vibration", Location = new Point(14, 14), Size = new Size(260, 20), ForeColor = Color.White, BackColor = Color.Transparent };
            _togVib = new FluentToggle { Location = new Point(296, 12) };
            _togVib.CheckedChanged += v => { _settings.OsdVibration = v; _onChanged(); };

            Label lblSteam = new Label { Text = "Mute Alerts When Steam Is Open", Location = new Point(14, 54), Size = new Size(260, 20), ForeColor = Color.White, BackColor = Color.Transparent };
            _togSteamMute = new FluentToggle { Location = new Point(296, 52) };
            _togSteamMute.CheckedChanged += v => { _settings.MuteWhenSteamOpen = v; _onChanged(); };

            Label lblAuto = new Label { Text = "Auto-Start with Windows", Location = new Point(14, 94), Size = new Size(260, 20), ForeColor = Color.White, BackColor = Color.Transparent };
            _togAutoStart = new FluentToggle { Location = new Point(296, 92) };
            _togAutoStart.CheckedChanged += v => { AppSettings.SetAutoStart(v); };

            toggleCard.Controls.AddRange(new Control[] { lblVib, _togVib, lblSteam, _togSteamMute, lblAuto, _togAutoStart });

            FluentButton btnClose = new FluentButton { Text = "Done", Location = new Point(280, 346), Size = new Size(90, 32), ForeColor = Color.White, IsPrimary = true };
            btnClose.Click += (s, e) => Hide();

            Controls.AddRange(new Control[] { sliderCard, toggleCard, btnClose });

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
            FluentTheme.ApplyWin11WindowStyle(Handle);
        }

        public void LoadFromSettings()
        {
            _sldCursor.Value = _settings.CursorSensitivity;
            _lblCursor.Text = string.Format("Desktop Cursor Sensitivity: {0}%", _settings.CursorSensitivity);
            _sldScroll.Value = _settings.ScrollSpeed;
            _lblScroll.Text = string.Format("Right Stick Scroll Speed: {0}%", _settings.ScrollSpeed);
            _sldLowBat.Value = _settings.LowBatteryThreshold;
            _lblLowBat.Text = string.Format("Low Battery Alert Threshold: {0}%", _settings.LowBatteryThreshold);
            _togVib.Checked = _settings.OsdVibration;
            _togSteamMute.Checked = _settings.MuteWhenSteamOpen;
            _togAutoStart.Checked = AppSettings.IsAutoStartEnabled();
        }
    }
}
