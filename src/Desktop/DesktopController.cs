using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using XboxBatteryMonitor.Core;
using XboxBatteryMonitor.UI;

namespace XboxBatteryMonitor.Desktop
{
    internal class DesktopController
    {
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
        private volatile float _scrollMultiplier = 1.0f;
        private volatile bool _vibrationEnabled = true;
        private bool _prevLeftClick = false;
        private bool _prevRightClick = false;
        private bool _prevMiddleClick = false;
        private ushort _prevButtons = 0;
        private double _scrollAccumY = 0;
        private double _scrollAccumX = 0;
        private double _cursorAccumX = 0;
        private double _cursorAccumY = 0;
        private long _lastNavTickMs = 0;

        public DesktopController(OsdKeyboardForm osdKeyboard = null, SynchronizationContext uiContext = null, float sensitivityMultiplier = 1.0f, float scrollMultiplier = 1.0f, bool vibrationEnabled = true)
        {
            _osdKeyboard = osdKeyboard;
            _uiContext = uiContext;
            _sensitivityMultiplier = Math.Max(0.25f, Math.Min(3.0f, sensitivityMultiplier));
            _scrollMultiplier = Math.Max(0.25f, Math.Min(3.0f, scrollMultiplier));
            _vibrationEnabled = vibrationEnabled;
        }

        public float SensitivityMultiplier
        {
            get { return _sensitivityMultiplier; }
            set { _sensitivityMultiplier = Math.Max(0.25f, Math.Min(3.0f, value)); }
        }

        public float ScrollMultiplier
        {
            get { return _scrollMultiplier; }
            set { _scrollMultiplier = Math.Max(0.25f, Math.Min(3.0f, value)); }
        }

        public bool VibrationEnabled
        {
            get { return _vibrationEnabled; }
            set { _vibrationEnabled = value; }
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
                    NativeMethods.XINPUT_VIBRATION vib = new NativeMethods.XINPUT_VIBRATION { wLeftMotorSpeed = leftMotor, wRightMotorSpeed = rightMotor };
                    NativeMethods.XInputSetState(0, ref vib);
                    Thread.Sleep(durationMs);
                    NativeMethods.XINPUT_VIBRATION stop = new NativeMethods.XINPUT_VIBRATION { wLeftMotorSpeed = 0, wRightMotorSpeed = 0 };
                    NativeMethods.XInputSetState(0, ref stop);
                }
                catch { }
            });
        }

        public void Reset()
        {
            if (_prevLeftClick) SendMouseButton(NativeMethods.MOUSEEVENTF_LEFTUP);
            if (_prevRightClick) SendMouseButton(NativeMethods.MOUSEEVENTF_RIGHTUP);
            if (_prevMiddleClick) SendMouseButton(NativeMethods.MOUSEEVENTF_MIDDLEUP);
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
            NativeMethods.XINPUT_STATE state = new NativeMethods.XINPUT_STATE();
            if (NativeMethods.XInputGetState(0, ref state) != NativeMethods.ERROR_SUCCESS)
            {
                Reset();
                return;
            }

            var gp = state.Gamepad;
            ushort pressed = (ushort)(gp.wButtons & ~_prevButtons);

            if ((pressed & BTN_X) != 0 && _osdKeyboard != null)
            {
                RunOnUi(() => _osdKeyboard.ToggleFromController());
                _prevButtons = gp.wButtons;
                return;
            }

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
                    if (_vibrationEnabled) PulseVibration(0, 9000, 20);
                }

                if ((pressed & BTN_A) != 0) RunOnUi(() => _osdKeyboard.ActivateSelected());
                if ((pressed & BTN_LB) != 0) { SendKeyTap(0x08); if (_vibrationEnabled) PulseVibration(10000, 18000, 40); }
                if ((pressed & BTN_Y) != 0) { SendKeyTap(0x20); if (_vibrationEnabled) PulseVibration(10000, 18000, 40); }
                if ((pressed & BTN_RB) != 0) { SendKeyTap(0x0D); if (_vibrationEnabled) PulseVibration(12000, 22000, 45); }
                if ((pressed & BTN_START) != 0 || (pressed & BTN_B) != 0) RunOnUi(() => _osdKeyboard.HideKeyboard());

                _prevButtons = gp.wButtons;
                return;
            }

            MoveCursorFromStick(gp.sThumbLX, gp.sThumbLY, dtSeconds, _sensitivityMultiplier);
            ScrollFromStick(gp.sThumbRX, gp.sThumbRY, dtSeconds, _scrollMultiplier);

            bool leftDown = ((gp.wButtons & BTN_A) != 0) || (gp.bRightTrigger > TRIGGER_THRESHOLD);
            if (leftDown != _prevLeftClick)
            {
                SendMouseButton(leftDown ? NativeMethods.MOUSEEVENTF_LEFTDOWN : NativeMethods.MOUSEEVENTF_LEFTUP);
                _prevLeftClick = leftDown;
            }

            bool rightDown = ((gp.wButtons & BTN_B) != 0) || (gp.bLeftTrigger > TRIGGER_THRESHOLD);
            if (rightDown != _prevRightClick)
            {
                SendMouseButton(rightDown ? NativeMethods.MOUSEEVENTF_RIGHTDOWN : NativeMethods.MOUSEEVENTF_RIGHTUP);
                _prevRightClick = rightDown;
            }

            bool midDown = (gp.wButtons & BTN_LS) != 0;
            if (midDown != _prevMiddleClick)
            {
                SendMouseButton(midDown ? NativeMethods.MOUSEEVENTF_MIDDLEDOWN : NativeMethods.MOUSEEVENTF_MIDDLEUP);
                _prevMiddleClick = midDown;
            }

            SyncKey(gp.wButtons, DPAD_UP, 0x26);
            SyncKey(gp.wButtons, DPAD_DOWN, 0x28);
            SyncKey(gp.wButtons, DPAD_LEFT, 0x25);
            SyncKey(gp.wButtons, DPAD_RIGHT, 0x27);

            if ((pressed & BTN_Y) != 0) SendKeyTap(0x20);
            if ((pressed & BTN_START) != 0) SendKeyTap(0x1B);
            if ((pressed & BTN_RS) != 0) SendKeyTap(0x0D);
            if ((pressed & BTN_BACK) != 0) SendKeyCombo(0x12, 0x09);
            if ((pressed & BTN_LB) != 0) SendKeyTap(0xA6);
            if ((pressed & BTN_RB) != 0) SendKeyTap(0xA7);

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

        public static Point ComputeStickCursorDelta(short rawX, short rawY, double dtSeconds, ref double accumX, ref double accumY, double sensitivityMultiplier = 1.0)
        {
            double mag = Math.Sqrt((double)rawX * rawX + (double)rawY * rawY);
            if (mag <= STICK_DEADZONE)
            {
                accumX = 0;
                accumY = 0;
                return Point.Empty;
            }

            double normMag = Math.Min(1.0, (mag - STICK_DEADZONE) / (32767.0 - STICK_DEADZONE));
            double pxPerSec = (0.20 * normMag + 0.80 * normMag * normMag) * 1200.0 * sensitivityMultiplier;
            double step = pxPerSec * Math.Min(dtSeconds, 0.05);

            accumX += (rawX / mag) * step;
            accumY += (-rawY / mag) * step;

            int dx = (int)Math.Truncate(accumX);
            int dy = (int)Math.Truncate(accumY);
            accumX -= dx;
            accumY -= dy;
            return new Point(dx, dy);
        }

        public static int ComputeScrollDelta(short rawAxis, ref double accum, double dtSeconds = 1.0 / 60.0, double scrollMultiplier = 1.0)
        {
            int absAxis = Math.Abs((int)rawAxis);
            if (absAxis <= STICK_DEADZONE) return 0;

            double norm = Math.Min(1.0, (absAxis - STICK_DEADZONE) / (32767.0 - STICK_DEADZONE));
            accum += Math.Sign((int)rawAxis) * norm * 2100.0 * scrollMultiplier * Math.Min(dtSeconds, 0.05);
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

            NativeMethods.INPUT[] inp = new NativeMethods.INPUT[1];
            inp[0].type = NativeMethods.INPUT_MOUSE;
            inp[0].u.mi = new NativeMethods.MOUSEINPUT { dx = delta.X, dy = delta.Y, dwFlags = NativeMethods.MOUSEEVENTF_MOVE };
            NativeMethods.SendInput(1, inp, Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        }

        private void ScrollFromStick(short rawX, short rawY, double dtSeconds, double scrollMultiplier)
        {
            int deltaY = ComputeScrollDelta(rawY, ref _scrollAccumY, dtSeconds, scrollMultiplier);
            if (deltaY != 0)
            {
                NativeMethods.INPUT[] inp = new NativeMethods.INPUT[1];
                inp[0].type = NativeMethods.INPUT_MOUSE;
                inp[0].u.mi = new NativeMethods.MOUSEINPUT { mouseData = unchecked((uint)deltaY), dwFlags = NativeMethods.MOUSEEVENTF_WHEEL };
                NativeMethods.SendInput(1, inp, Marshal.SizeOf(typeof(NativeMethods.INPUT)));
            }

            int deltaX = ComputeScrollDelta(rawX, ref _scrollAccumX, dtSeconds, scrollMultiplier);
            if (deltaX != 0)
            {
                NativeMethods.INPUT[] inp = new NativeMethods.INPUT[1];
                inp[0].type = NativeMethods.INPUT_MOUSE;
                inp[0].u.mi = new NativeMethods.MOUSEINPUT { mouseData = unchecked((uint)deltaX), dwFlags = NativeMethods.MOUSEEVENTF_HWHEEL };
                NativeMethods.SendInput(1, inp, Marshal.SizeOf(typeof(NativeMethods.INPUT)));
            }
        }

        private static void SendMouseButton(uint flag)
        {
            NativeMethods.INPUT[] inp = new NativeMethods.INPUT[1];
            inp[0].type = NativeMethods.INPUT_MOUSE;
            inp[0].u.mi = new NativeMethods.MOUSEINPUT { dwFlags = flag };
            NativeMethods.SendInput(1, inp, Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        }

        private static void SendKey(ushort vk, bool keyUp)
        {
            NativeMethods.INPUT[] inp = new NativeMethods.INPUT[1];
            inp[0].type = NativeMethods.INPUT_KEYBOARD;
            inp[0].u.ki = new NativeMethods.KEYBDINPUT { wVk = vk, dwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0 };
            NativeMethods.SendInput(1, inp, Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        }

        public static void SendKeyTap(ushort vk)
        {
            SendKey(vk, false);
            SendKey(vk, true);
        }

        public static void SendUnicodeChar(char ch)
        {
            NativeMethods.INPUT[] inp = new NativeMethods.INPUT[2];
            inp[0].type = NativeMethods.INPUT_KEYBOARD;
            inp[0].u.ki = new NativeMethods.KEYBDINPUT { wScan = ch, dwFlags = NativeMethods.KEYEVENTF_UNICODE };
            inp[1].type = NativeMethods.INPUT_KEYBOARD;
            inp[1].u.ki = new NativeMethods.KEYBDINPUT { wScan = ch, dwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP };
            NativeMethods.SendInput(2, inp, Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        }

        private static void SendKeyCombo(params ushort[] vks)
        {
            for (int i = 0; i < vks.Length; i++) SendKey(vks[i], false);
            for (int i = vks.Length - 1; i >= 0; i--) SendKey(vks[i], true);
        }
    }
}
