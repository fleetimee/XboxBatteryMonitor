using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace XboxBatteryMonitor.UI
{
    internal class LayoutForm : Form
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
            FluentTheme.ApplyWin11WindowStyle(Handle);
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
                using (GraphicsPath lt = FluentTheme.CreateRoundedRect(new RectangleF(cx - 108, cy - 118, 44, 24), 6))
                { g.FillPath(trigBrush, lt); g.DrawPath(trigPen, lt); }
                using (GraphicsPath rt = FluentTheme.CreateRoundedRect(new RectangleF(cx + 64, cy - 118, 44, 24), 6))
                { g.FillPath(trigBrush, rt); g.DrawPath(trigPen, rt); }
            }

            using (SolidBrush bmpBrush = new SolidBrush(Color.FromArgb(62, 62, 68)))
            using (Pen bmpPen = new Pen(Color.FromArgb(105, 105, 115), 1.5f))
            {
                using (GraphicsPath lb = FluentTheme.CreateRoundedRect(new RectangleF(cx - 116, cy - 90, 62, 18), 7))
                { g.FillPath(bmpBrush, lb); g.DrawPath(bmpPen, lb); }
                using (GraphicsPath rb = FluentTheme.CreateRoundedRect(new RectangleF(cx + 54, cy - 90, 62, 18), 7))
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

            using (GraphicsPath path = FluentTheme.CreateRoundedRect(rect, 6f))
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
}
