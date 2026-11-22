using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using XboxBatteryMonitor.Desktop;

namespace XboxBatteryMonitor.UI
{
    internal class LayoutForm : Form
    {
        private class BufferedCanvas : Panel
        {
            public BufferedCanvas()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
            }
        }

        private class CalloutRegion
        {
            public string Key;
            public RectangleF Rect;
        }

        private readonly Func<bool> _isEnabledProvider;
        private readonly Action _onToggle;
        private readonly ButtonBindings _bindings;
        private readonly Action _onBindingsChanged;
        private readonly FluentButton _btnToggle;
        private readonly Label _lblStatus;
        private readonly Panel _canvas;
        private readonly List<CalloutRegion> _regions = new List<CalloutRegion>();
        private string _hoveredKey = null;

        public LayoutForm(Func<bool> isEnabledProvider, Action onToggle, ButtonBindings bindings, Action onBindingsChanged)
        {
            _isEnabledProvider = isEnabledProvider;
            _onToggle = onToggle;
            _bindings = bindings;
            _onBindingsChanged = onBindingsChanged;

            Text = "Xbox Controller — Desktop Layout (Click any button to remap)";
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

            _canvas = new BufferedCanvas
            {
                Location = new Point(0, 0),
                Size = new Size(760, 404),
                BackColor = Color.Transparent
            };
            _canvas.Paint += OnCanvasPaint;
            _canvas.MouseMove += OnCanvasMouseMove;
            _canvas.MouseLeave += (s, e) => { if (_hoveredKey != null) { _hoveredKey = null; _canvas.Cursor = Cursors.Default; _canvas.Invalidate(); } };
            _canvas.MouseClick += OnCanvasMouseClick;

            _lblStatus = new Label
            {
                Location = new Point(18, 416),
                Size = new Size(310, 24),
                Font = new Font("Segoe UI Variable Text", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(170, 170, 178)
            };

            FluentButton btnReset = new FluentButton
            {
                Text = "Reset Defaults",
                Location = new Point(342, 412),
                Size = new Size(118, 32),
                ForeColor = Color.White
            };
            btnReset.Click += (s, e) =>
            {
                _bindings.ResetDefaults();
                _onBindingsChanged();
                _canvas.Invalidate();
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

            Controls.AddRange(new Control[] { _canvas, _lblStatus, btnReset, _btnToggle, btnClose });

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
            _lblStatus.Text = on ? "Desktop Control: ON" : "Desktop Control: OFF";
            _lblStatus.ForeColor = on ? Color.FromArgb(134, 239, 172) : Color.FromArgb(170, 170, 178);
            _btnToggle.Text = on ? "Disable Desktop Control" : "Enable Desktop Control";
            _canvas.Invalidate();
        }

        private void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            string hit = HitTestRegion(e.Location);
            if (hit != _hoveredKey)
            {
                _hoveredKey = hit;
                _canvas.Cursor = hit != null ? Cursors.Hand : Cursors.Default;
                _canvas.Invalidate();
            }
        }

        private void OnCanvasMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            string key = HitTestRegion(e.Location);
            if (key == null) return;

            ContextMenuStrip picker = new ContextMenuStrip();
            DesktopAction current = _bindings.Get(key);
            foreach (DesktopAction act in Enum.GetValues(typeof(DesktopAction)))
            {
                DesktopAction captured = act;
                ToolStripMenuItem item = new ToolStripMenuItem(ButtonBindings.ActionLabel(captured))
                {
                    Checked = (captured == current)
                };
                item.Click += (s, ev) =>
                {
                    _bindings.Set(key, captured);
                    _onBindingsChanged();
                    _canvas.Invalidate();
                };
                picker.Items.Add(item);
            }
            picker.Show(_canvas, e.Location);
        }

        private string HitTestRegion(Point pt)
        {
            for (int i = 0; i < _regions.Count; i++)
            {
                if (_regions[i].Rect.Contains(pt)) return _regions[i].Key;
            }
            return null;
        }

        private void OnCanvasPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            _regions.Clear();

            using (Font titleFont = new Font("Segoe UI Variable Display", 12f, FontStyle.Bold))
            using (Font subFont = new Font("Segoe UI Variable Text", 8.5f))
            using (SolidBrush titleBrush = new SolidBrush(Color.White))
            using (SolidBrush subBrush = new SolidBrush(Color.FromArgb(150, 150, 160)))
            {
                g.DrawString("Desktop Layout — Click any button card to remap", titleFont, titleBrush, 18, 12);
                g.DrawString("Left Stick: Mouse Cursor   •   Right Stick: Scroll Wheel   •   D-Pad: Arrow Keys", subFont, subBrush, 18, 36);
            }

            float cx = 380f;
            float cy = 224f;

            // Curved Triggers (LT / RT) peeking above bumpers
            using (SolidBrush trigBrush = new SolidBrush(Color.FromArgb(46, 48, 54)))
            using (Pen trigPen = new Pen(Color.FromArgb(95, 100, 112), 1.4f))
            {
                using (GraphicsPath lt = new GraphicsPath())
                {
                    lt.AddBezier(cx - 104, cy - 92, cx - 96, cy - 116, cx - 72, cy - 118, cx - 64, cy - 102);
                    lt.AddLine(cx - 64, cy - 102, cx - 104, cy - 92);
                    g.FillPath(trigBrush, lt);
                    g.DrawPath(trigPen, lt);
                }
                using (GraphicsPath rt = new GraphicsPath())
                {
                    rt.AddBezier(cx + 104, cy - 92, cx + 96, cy - 116, cx + 72, cy - 118, cx + 64, cy - 102);
                    rt.AddLine(cx + 64, cy - 102, cx + 104, cy - 92);
                    g.FillPath(trigBrush, rt);
                    g.DrawPath(trigPen, rt);
                }
            }

            // Curved Bumpers (LB / RB) hugging the upper shoulder
            using (SolidBrush bmpBrush = new SolidBrush(Color.FromArgb(52, 54, 60)))
            using (Pen bmpPen = new Pen(Color.FromArgb(115, 120, 132), 1.5f))
            {
                using (GraphicsPath lb = new GraphicsPath())
                {
                    lb.AddBezier(cx - 118, cy - 72, cx - 108, cy - 96, cx - 72, cy - 104, cx - 46, cy - 94);
                    lb.AddLine(cx - 46, cy - 94, cx - 50, cy - 84);
                    lb.AddBezier(cx - 50, cy - 84, cx - 74, cy - 92, cx - 100, cy - 86, cx - 112, cy - 68);
                    lb.CloseFigure();
                    g.FillPath(bmpBrush, lb);
                    g.DrawPath(bmpPen, lb);
                }
                using (GraphicsPath rb = new GraphicsPath())
                {
                    rb.AddBezier(cx + 118, cy - 72, cx + 108, cy - 96, cx + 72, cy - 104, cx + 46, cy - 94);
                    rb.AddLine(cx + 46, cy - 94, cx + 50, cy - 84);
                    rb.AddBezier(cx + 50, cy - 84, cx + 74, cy - 92, cx + 100, cy - 86, cx + 112, cy - 68);
                    rb.CloseFigure();
                    g.FillPath(bmpBrush, rb);
                    g.DrawPath(bmpPen, rb);
                }
            }

            // Smooth Steam-style Xbox Controller Silhouette
            using (GraphicsPath body = new GraphicsPath())
            {
                // Top center dip -> right shoulder -> right grip -> bottom arch -> left grip -> left shoulder
                body.AddBezier(cx - 44, cy - 86, cx - 18, cy - 80, cx + 18, cy - 80, cx + 44, cy - 86);
                body.AddBezier(cx + 44, cy - 86, cx + 88, cy - 92, cx + 124, cy - 68, cx + 136, cy - 22);
                body.AddBezier(cx + 136, cy - 22, cx + 150, cy + 32, cx + 154, cy + 84, cx + 122, cy + 94);
                body.AddBezier(cx + 122, cy + 94, cx + 96, cy + 100, cx + 76, cy + 62, cx + 52, cy + 44);
                body.AddBezier(cx + 52, cy + 44, cx + 22, cy + 42, cx - 22, cy + 42, cx - 52, cy + 44);
                body.AddBezier(cx - 52, cy + 44, cx - 76, cy + 62, cx - 96, cy + 100, cx - 122, cy + 94);
                body.AddBezier(cx - 122, cy + 94, cx - 154, cy + 84, cx - 150, cy + 32, cx - 136, cy - 22);
                body.AddBezier(cx - 136, cy - 22, cx - 124, cy - 68, cx - 88, cy - 92, cx - 44, cy - 86);
                body.CloseFigure();

                using (SolidBrush bodyBrush = new SolidBrush(Color.FromArgb(36, 38, 44)))
                using (Pen bodyPen = new Pen(Color.FromArgb(120, 126, 138), 1.8f) { LineJoin = LineJoin.Round })
                {
                    g.FillPath(bodyBrush, body);
                    g.DrawPath(bodyPen, body);
                }
            }

            // Xbox Guide Button with 'X' logo
            using (SolidBrush guideBrush = new SolidBrush(Color.FromArgb(52, 56, 64)))
            using (Pen guidePen = new Pen(Color.FromArgb(160, 168, 180), 1.5f))
            using (Pen xPen = new Pen(Color.FromArgb(220, 226, 235), 2.0f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.FillEllipse(guideBrush, cx - 13, cy - 68, 26, 26);
                g.DrawEllipse(guidePen, cx - 13, cy - 68, 26, 26);
                g.DrawLine(xPen, cx - 6, cy - 61, cx + 6, cy - 49);
                g.DrawLine(xPen, cx + 6, cy - 61, cx - 6, cy - 49);
            }

            // View (Back) & Menu (Start) buttons with subtle icons
            using (SolidBrush smallBtn = new SolidBrush(Color.FromArgb(54, 58, 66)))
            using (Pen smallPen = new Pen(Color.FromArgb(130, 136, 148), 1.2f))
            {
                g.FillEllipse(smallBtn, cx - 35, cy - 32, 15, 15);
                g.DrawEllipse(smallPen, cx - 35, cy - 32, 15, 15);
                g.FillEllipse(smallBtn, cx + 20, cy - 32, 15, 15);
                g.DrawEllipse(smallPen, cx + 20, cy - 32, 15, 15);
            }

            // Asymmetric Thumbsticks (Left upper, Right lower)
            DrawThumbstick(g, cx - 78, cy - 30, 21);
            DrawThumbstick(g, cx + 40, cy + 12, 21);

            // Circular Dish D-Pad with '+' cross
            using (SolidBrush dishBrush = new SolidBrush(Color.FromArgb(44, 46, 54)))
            using (Pen dishPen = new Pen(Color.FromArgb(95, 100, 112), 1.2f))
            using (SolidBrush crossBrush = new SolidBrush(Color.FromArgb(74, 78, 88)))
            using (Pen crossPen = new Pen(Color.FromArgb(135, 140, 152), 1.2f))
            {
                g.FillEllipse(dishBrush, cx - 65, cy - 10, 48, 48);
                g.DrawEllipse(dishPen, cx - 65, cy - 10, 48, 48);
                g.FillRectangle(crossBrush, cx - 47, cy - 3, 12, 34);
                g.FillRectangle(crossBrush, cx - 58, cy + 8, 34, 12);
                g.DrawRectangle(crossPen, cx - 47, cy - 3, 12, 34);
                g.DrawRectangle(crossPen, cx - 58, cy + 8, 34, 12);
            }

            // ABXY Face Buttons
            DrawFaceButton(g, cx + 72, cy - 52, "Y", Color.FromArgb(250, 204, 21));
            DrawFaceButton(g, cx + 52, cy - 32, "X", Color.FromArgb(56, 189, 248));
            DrawFaceButton(g, cx + 92, cy - 32, "B", Color.FromArgb(248, 113, 113));
            DrawFaceButton(g, cx + 72, cy - 12, "A", Color.FromArgb(74, 222, 128));

            // Left remappable callouts
            DrawCallout(g, "LT", new PointF(cx - 84, cy - 106), new PointF(18, 66), "LT", ButtonBindings.ActionLabel(_bindings.Get("LT")), true);
            DrawCallout(g, "LB", new PointF(cx - 84, cy - 86), new PointF(18, 114), "LB", ButtonBindings.ActionLabel(_bindings.Get("LB")), true);
            DrawCallout(g, "L3", new PointF(cx - 78, cy - 30), new PointF(18, 168), "L3 (Stick Press)", ButtonBindings.ActionLabel(_bindings.Get("L3")), true);
            DrawCallout(g, "View", new PointF(cx - 28, cy - 24), new PointF(18, 222), "View (Back)", ButtonBindings.ActionLabel(_bindings.Get("View")), true);
            DrawCallout(g, "Menu", new PointF(cx + 28, cy - 24), new PointF(18, 276), "Menu (Start)", ButtonBindings.ActionLabel(_bindings.Get("Menu")), true);

            // Right remappable callouts
            DrawCallout(g, "RT", new PointF(cx + 84, cy - 106), new PointF(568, 66), "RT", ButtonBindings.ActionLabel(_bindings.Get("RT")), false);
            DrawCallout(g, "RB", new PointF(cx + 84, cy - 86), new PointF(568, 114), "RB", ButtonBindings.ActionLabel(_bindings.Get("RB")), false);
            DrawCallout(g, "Y", new PointF(cx + 80, cy - 44), new PointF(568, 162), "Y", ButtonBindings.ActionLabel(_bindings.Get("Y")), false);
            DrawCallout(g, "X", new PointF(cx + 60, cy - 24), new PointF(568, 210), "X", ButtonBindings.ActionLabel(_bindings.Get("X")), false);
            DrawCallout(g, "B", new PointF(cx + 100, cy - 24), new PointF(568, 258), "B", ButtonBindings.ActionLabel(_bindings.Get("B")), false);
            DrawCallout(g, "A", new PointF(cx + 80, cy - 4), new PointF(568, 306), "A", ButtonBindings.ActionLabel(_bindings.Get("A")), false);
            DrawCallout(g, "R3", new PointF(cx + 40, cy + 12), new PointF(568, 354), "R3 (Stick Press)", ButtonBindings.ActionLabel(_bindings.Get("R3")), false);
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

        private void DrawCallout(Graphics g, string bindKey, PointF anchor, PointF boxPos, string btnName, string action, bool isLeft)
        {
            float boxW = 174f;
            float boxH = 36f;
            RectangleF rect = new RectangleF(boxPos.X, boxPos.Y, boxW, boxH);
            _regions.Add(new CalloutRegion { Key = bindKey, Rect = rect });

            bool hovered = string.Equals(_hoveredKey, bindKey, StringComparison.OrdinalIgnoreCase);

            PointF lineStart = isLeft
                ? new PointF(rect.Right, rect.Y + boxH / 2f)
                : new PointF(rect.X, rect.Y + boxH / 2f);

            using (Pen linePen = new Pen(Color.FromArgb(hovered ? 200 : 100, 125, 211, 252), 1.2f))
            {
                g.DrawLine(linePen, lineStart, anchor);
            }
            using (SolidBrush dot = new SolidBrush(Color.FromArgb(125, 211, 252)))
            {
                g.FillEllipse(dot, anchor.X - 2.5f, anchor.Y - 2.5f, 5, 5);
            }

            Color bgCol = hovered ? Color.FromArgb(54, 58, 68) : Color.FromArgb(40, 40, 44);
            Color borderCol = hovered ? Color.FromArgb(125, 211, 252) : Color.FromArgb(65, 65, 72);

            using (GraphicsPath path = FluentTheme.CreateRoundedRect(rect, 6f))
            using (SolidBrush bg = new SolidBrush(bgCol))
            using (Pen border = new Pen(borderCol, hovered ? 1.4f : 1f))
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
                g.DrawString(action + " ▾", fVal, bVal, rect.X + 8, rect.Y + 18);
            }
        }
    }
}
