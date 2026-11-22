using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using XboxBatteryMonitor.Core;

namespace XboxBatteryMonitor.UI
{
    internal static class FluentTheme
    {
        public static GraphicsPath CreateRoundedRect(RectangleF bounds, float radius)
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

        public static void ApplyWin11WindowStyle(IntPtr handle, int borderColor = 0x00424246)
        {
            try
            {
                int dark = 1;
                NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                int round = NativeMethods.DWMWCP_ROUND;
                NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
                NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
            }
            catch { }
        }
    }

    internal class FluentCard : Panel
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
            using (GraphicsPath path = FluentTheme.CreateRoundedRect(rect, CornerRadius))
            using (SolidBrush fill = new SolidBrush(CardColor))
            using (Pen border = new Pen(BorderColor, 1f))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }
        }
    }

    internal class FluentProgressBar : Control
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
            using (GraphicsPath trackPath = FluentTheme.CreateRoundedRect(trackRect, r))
            using (SolidBrush trackBrush = new SolidBrush(TrackColor))
            {
                e.Graphics.FillPath(trackBrush, trackPath);
            }

            if (_value > 0)
            {
                float fillW = Math.Max(Height, (Width - 1) * (_value / 100f));
                RectangleF fillRect = new RectangleF(0, 0, fillW, Height - 1);
                using (GraphicsPath fillPath = FluentTheme.CreateRoundedRect(fillRect, r))
                using (SolidBrush fillBrush = new SolidBrush(FillColor))
                {
                    e.Graphics.FillPath(fillBrush, fillPath);
                }
            }
        }
    }

    internal class FluentButton : Control
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
            using (GraphicsPath path = FluentTheme.CreateRoundedRect(rect, 6f))
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

    internal class FluentSlider : Control
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

            using (GraphicsPath trackPath = FluentTheme.CreateRoundedRect(new RectangleF(pad, trackY, usable, trackH), 2f))
            using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(66, 66, 72)))
            {
                g.FillPath(trackBrush, trackPath);
            }

            if (thumbX > pad)
            {
                using (GraphicsPath fillPath = FluentTheme.CreateRoundedRect(new RectangleF(pad, trackY, thumbX - pad, trackH), 2f))
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

    internal class FluentToggle : Control
    {
        private bool _checked = false;
        public event Action<bool> CheckedChanged;

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked != value)
                {
                    _checked = value;
                    Invalidate();
                }
            }
        }

        public FluentToggle()
        {
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            Size = new Size(44, 22);
        }

        protected override void OnClick(EventArgs e)
        {
            _checked = !_checked;
            Invalidate();
            if (CheckedChanged != null) CheckedChanged(_checked);
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            RectangleF track = new RectangleF(1, 1, Width - 3, Height - 3);
            Color bg = _checked ? Color.FromArgb(0, 120, 212) : Color.FromArgb(52, 52, 58);
            Color border = _checked ? bg : Color.FromArgb(120, 120, 130);

            using (GraphicsPath path = FluentTheme.CreateRoundedRect(track, (Height - 3) / 2f))
            using (SolidBrush fill = new SolidBrush(bg))
            using (Pen pen = new Pen(border, 1.3f))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            float knobSize = Height - 9;
            float knobX = _checked ? Width - knobSize - 5 : 4;
            using (SolidBrush knob = new SolidBrush(Color.White))
            {
                g.FillEllipse(knob, knobX, 4f, knobSize, knobSize);
            }
        }
    }
}
