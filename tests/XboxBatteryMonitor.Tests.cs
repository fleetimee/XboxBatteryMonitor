using System;
using System.Drawing;
using XboxBatteryMonitor.Battery;
using XboxBatteryMonitor.Desktop;
using XboxBatteryMonitor.UI;

namespace XboxBatteryMonitor.Tests
{
    static class TestRunner
    {
        private static int _passed = 0;
        private static int _failed = 0;

        static int Main()
        {
            Run("ScrollFromStick handles short.MinValue (-32768) without OverflowException", () =>
            {
                double accum = 0;
                int delta = DesktopController.ComputeScrollDelta(short.MinValue, ref accum);
                AssertTrue(delta < 0, "Expected negative scroll delta for short.MinValue, got " + delta);
            });

            Run("ScrollFromStick handles short.MaxValue (32767) and deadzone", () =>
            {
                double accum = 0;
                int dead = DesktopController.ComputeScrollDelta(3000, ref accum);
                AssertEqual(0, dead, "Deadzone should return 0");

                int maxDelta = DesktopController.ComputeScrollDelta(short.MaxValue, ref accum);
                AssertTrue(maxDelta > 0, "Expected positive scroll delta for short.MaxValue");
            });

            Run("MoveCursor handles short.MinValue (-32768), deadzone, and sub-pixel accumulation", () =>
            {
                double ax = 0, ay = 0;
                Point dead = DesktopController.ComputeStickCursorDelta(1000, -1000, 1.0 / 60.0, ref ax, ref ay);
                AssertEqual(Point.Empty, dead, "Deadzone should return Point.Empty");

                Point minPt = DesktopController.ComputeStickCursorDelta(short.MinValue, short.MinValue, 1.0 / 60.0, ref ax, ref ay);
                AssertTrue(minPt.X < 0 && minPt.Y > 0, "Expected negative X and positive Y (screen inverted)");

                ax = 0; ay = 0;
                int movedX = 0;
                for (int i = 0; i < 20; i++)
                {
                    movedX += DesktopController.ComputeStickCursorDelta(8500, 0, 1.0 / 120.0, ref ax, ref ay).X;
                }
                AssertTrue(movedX > 0, "Sub-pixel accumulation should move cursor smoothly on gentle tilt");
            });

            Run("Battery alert triggers at <= threshold, respects hysteresis, resets at >= threshold+5", () =>
            {
                bool notified = false;
                AssertFalse(BatteryAlertLogic.EvaluateBatteryAlert(50, 3, false, ref notified, 20), "50% should not alert");
                AssertTrue(BatteryAlertLogic.EvaluateBatteryAlert(20, 1, false, ref notified, 20), "20% should alert");
                notified = true;
                AssertFalse(BatteryAlertLogic.EvaluateBatteryAlert(19, 1, false, ref notified, 20), "Already notified should not re-alert");
                AssertFalse(BatteryAlertLogic.EvaluateBatteryAlert(22, 2, false, ref notified, 20), "22% stays in hysteresis band");
                AssertTrue(notified, "notified should still be true at 22%");

                AssertFalse(BatteryAlertLogic.EvaluateBatteryAlert(25, 2, false, ref notified, 20), "25% recovers");
                AssertFalse(notified, "notified should reset at >= 25%");
                AssertTrue(BatteryAlertLogic.EvaluateBatteryAlert(15, 1, false, ref notified, 20), "15% should alert again after recovery");
            });

            Run("Battery alert resets when wired", () =>
            {
                bool notified = true;
                AssertFalse(BatteryAlertLogic.EvaluateBatteryAlert(10, 1, true, ref notified), "Wired should not alert");
                AssertFalse(notified, "Wired should reset notifiedLow");
            });

            Run("Charging inference detects upward tick and clears on drop or wired", () =>
            {
                int? last = null;
                bool charging = false;
                BatteryAlertLogic.UpdateChargingInference(66, false, ref last, ref charging);
                AssertFalse(charging, "Initial reading should not infer charging");

                BatteryAlertLogic.UpdateChargingInference(67, false, ref last, ref charging);
                AssertTrue(charging, "66 -> 67 should infer charging");

                BatteryAlertLogic.UpdateChargingInference(67, false, ref last, ref charging);
                AssertTrue(charging, "67 -> 67 should preserve charging state");

                BatteryAlertLogic.UpdateChargingInference(66, false, ref last, ref charging);
                AssertFalse(charging, "67 -> 66 should clear charging");

                BatteryAlertLogic.UpdateChargingInference(67, false, ref last, ref charging);
                AssertTrue(charging, "66 -> 67 should infer charging again");
                BatteryAlertLogic.UpdateChargingInference(null, true, ref last, ref charging);
                AssertFalse(charging, "Wired should clear wireless charging inference");
            });

            Run("Tray tooltip never exceeds 63 characters", () =>
            {
                string longName = new string('X', 80);
                string tip = BatteryAlertLogic.FormatTrayTooltip(longName, false, 75, 3);
                AssertTrue(tip.Length <= 63, "Tooltip exceeded 63 chars: " + tip.Length);
            });

            Run("OSD Keyboard MoveSelection reaches leftmost column 0 and wraps cleanly", () =>
            {
                using (var kbd = new OsdKeyboardForm())
                {
                    kbd.MoveSelection(1, 0);
                    AssertEqual(1, kbd.SelectedCol, "Should move right to col 1");
                    kbd.MoveSelection(-1, 0);
                    AssertEqual(0, kbd.SelectedCol, "Should reach leftmost col 0 ('a'), not wrap!");
                    kbd.MoveSelection(-1, 0);
                    AssertTrue(kbd.SelectedCol > 0, "Should wrap from col 0 to last column");
                }
            });

            Run("Steam Xbox support detection parses config.vdf accurately", () =>
            {
                AssertFalse(FocusDetector.IsSteamXboxSupportEnabled(""), "Empty config should be false");
                AssertFalse(FocusDetector.IsSteamXboxSupportEnabled("\"controller_xbox_support\"\t\t\"0\""), "Disabled should be false");
                AssertTrue(FocusDetector.IsSteamXboxSupportEnabled("\"controller_xbox_support\"\t\t\"1\""), "Enabled with tabs should be true");
                AssertTrue(FocusDetector.IsSteamXboxSupportEnabled("\"controller_xbox_support\" \"1\""), "Enabled with space should be true");
            });

            Run("Cursor sensitivity multiplier scales speed proportionally", () =>
            {
                double ax1 = 0, ay1 = 0, ax2 = 0, ay2 = 0;
                Point basePt = DesktopController.ComputeStickCursorDelta(short.MaxValue, 0, 1.0 / 60.0, ref ax1, ref ay1, 1.0);
                Point fastPt = DesktopController.ComputeStickCursorDelta(short.MaxValue, 0, 1.0 / 60.0, ref ax2, ref ay2, 2.0);
                AssertTrue(fastPt.X >= basePt.X * 2 - 1, "2.0x sensitivity should roughly double delta");
            });

            Run("ButtonBindings defaults, custom remapping, and ResetDefaults work", () =>
            {
                var b = new ButtonBindings();
                AssertEqual(DesktopAction.LeftClick, b.Get("A"), "Default A should be LeftClick");
                AssertEqual(DesktopAction.ToggleOsdKeyboard, b.Get("X"), "Default X should be ToggleOsdKeyboard");

                b.Set("Y", DesktopAction.VolumeUp);
                AssertEqual(DesktopAction.VolumeUp, b.Get("Y"), "Remapped Y should be VolumeUp");

                b.ResetDefaults();
                AssertEqual(DesktopAction.Space, b.Get("Y"), "ResetDefaults should restore Y to Space");
            });

            Console.WriteLine();
            Console.WriteLine(string.Format("Tests passed: {0}, failed: 0", _passed));
            return _failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action body)
        {
            try
            {
                body();
                _passed++;
                Console.WriteLine("[PASS] " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("[FAIL] " + name + " -> " + ex.Message);
            }
        }

        private static void AssertTrue(bool cond, string msg)
        {
            if (!cond) throw new Exception(msg);
        }

        private static void AssertFalse(bool cond, string msg)
        {
            if (cond) throw new Exception(msg);
        }

        private static void AssertEqual<T>(T expected, T actual, string msg)
        {
            if (!Equals(expected, actual))
                throw new Exception(string.Format("{0} (expected {1}, got {2})", msg, expected, actual));
        }
    }
}
