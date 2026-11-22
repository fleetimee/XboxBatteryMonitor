using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using XboxBatteryMonitor.Desktop;

namespace XboxBatteryMonitor.UI
{
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

        private readonly Func<bool> _isVibrationEnabled;
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
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST
                return cp;
            }
        }

        public OsdKeyboardForm(Func<bool> isVibrationEnabled = null)
        {
            _isVibrationEnabled = isVibrationEnabled;
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
            FluentTheme.ApplyWin11WindowStyle(Handle, 0x004A4A52);
        }

        public void ToggleFromController()
        {
            if (Visible) HideKeyboard();
            else ShowKeyboard();
            if (_isVibrationEnabled == null || _isVibrationEnabled())
            {
                DesktopController.PulseVibration(0, 14000, 30);
            }
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

        public void MoveSelection(int dx, int dy)
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

        public int SelectedRow { get { return _selRow; } }
        public int SelectedCol { get { return _selCol; } }

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
            if (_isVibrationEnabled == null || _isVibrationEnabled())
            {
                DesktopController.PulseVibration(14000, 24000, 45);
            }

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

                        using (GraphicsPath p = FluentTheme.CreateRoundedRect(rect, 6f))
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
