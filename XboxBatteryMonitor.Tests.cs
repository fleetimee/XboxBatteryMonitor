using System;
using System.Drawing;

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
                int delta = Program.DesktopController.ComputeScrollDelta(short.MinValue, ref accum);
                AssertTrue(delta < 0, "Expected negative scroll delta for short.MinValue, got " + delta);
            });

            Run("ScrollFromStick handles short.MaxValue (32767) and deadzone", () =>
            {
                double accum = 0;
                int dead = Program.DesktopController.ComputeScrollDelta(3000, ref accum);
                AssertEqual(0, dead, "Deadzone should return 0");

                int maxDelta = Program.DesktopController.ComputeScrollDelta(short.MaxValue, ref accum);
                AssertTrue(maxDelta > 0, "Expected positive scroll delta for short.MaxValue");
            });

            Run("MoveCursor handles short.MinValue (-32768), deadzone, and sub-pixel accumulation", () =>
            {
                double ax = 0, ay = 0;
                Point dead = Program.DesktopController.ComputeStickCursorDelta(1000, -1000, 1.0 / 60.0, ref ax, ref ay);
                AssertEqual(Point.Empty, dead, "Deadzone should return Point.Empty");

                Point minPt = Program.DesktopController.ComputeStickCursorDelta(short.MinValue, short.MinValue, 1.0 / 60.0, ref ax, ref ay);
                AssertTrue(minPt.X < 0 && minPt.Y > 0, "Expected negative X and positive Y (screen inverted)");

                // Sub-pixel accumulation: small tilt should eventually produce a 1px step instead of staying 0 forever
                ax = 0; ay = 0;
                int movedX = 0;
                for (int i = 0; i < 20; i++)
                {
                    movedX += Program.DesktopController.ComputeStickCursorDelta(8500, 0, 1.0 / 120.0, ref ax, ref ay).X;
                }
                AssertTrue(movedX > 0, "Sub-pixel accumulation should move cursor smoothly on gentle tilt");
            });

            Run("Battery alert triggers at <= 20%, respects hysteresis, resets at >= 25%", () =>
            {
                bool notified = false;
                AssertFalse(Program.EvaluateBatteryAlert(50, 3, false, ref notified), "50% should not alert");
                AssertTrue(Program.EvaluateBatteryAlert(20, 1, false, ref notified), "20% should alert");
                notified = true;
                AssertFalse(Program.EvaluateBatteryAlert(19, 1, false, ref notified), "Already notified should not re-alert");
                AssertFalse(Program.EvaluateBatteryAlert(22, 2, false, ref notified), "22% stays in hysteresis band");
                AssertFalse(notified == false, "notified should still be true at 22%");

                AssertFalse(Program.EvaluateBatteryAlert(25, 2, false, ref notified), "25% recovers");
                AssertFalse(notified, "notified should reset at >= 25%");
                AssertTrue(Program.EvaluateBatteryAlert(15, 1, false, ref notified), "15% should alert again after recovery");
            });

            Run("Battery alert resets when wired", () =>
            {
                bool notified = true;
                AssertFalse(Program.EvaluateBatteryAlert(10, 1, true, ref notified), "Wired should not alert");
                AssertFalse(notified, "Wired should reset notifiedLow");
            });

            Run("Charging inference detects upward tick and clears on drop or wired", () =>
            {
                int? last = null;
                bool charging = false;
                Program.UpdateChargingInference(66, false, ref last, ref charging);
                AssertFalse(charging, "Initial reading should not infer charging");

                Program.UpdateChargingInference(67, false, ref last, ref charging);
                AssertTrue(charging, "66 -> 67 should infer charging");

                Program.UpdateChargingInference(67, false, ref last, ref charging);
                AssertTrue(charging, "67 -> 67 should preserve charging state");

                Program.UpdateChargingInference(66, false, ref last, ref charging);
                AssertFalse(charging, "67 -> 66 should clear charging");

                Program.UpdateChargingInference(67, false, ref last, ref charging);
                AssertTrue(charging, "66 -> 67 should infer charging again");
                Program.UpdateChargingInference(null, true, ref last, ref charging);
                AssertFalse(charging, "Wired should clear wireless charging inference");
            });

            Run("Tray tooltip never exceeds 63 characters", () =>
            {
                string longName = new string('X', 80);
                string tip = Program.FormatTrayTooltip(longName, false, 75, 3);
                AssertTrue(tip.Length <= 63, "Tooltip exceeded 63 chars: " + tip.Length);
            });

            Run("OSD Keyboard MoveSelection reaches leftmost column 0 and wraps cleanly", () =>
            {
                using (var kbd = new Program.OsdKeyboardForm())
                {
                    // Start at (row=1, col=0 'a') -> move right to col=1 ('s') -> move left back to col=0 ('a')
                    kbd.MoveSelection(1, 0);
                    AssertEqual(1, kbd.SelectedCol, "Should move right to col 1");
                    kbd.MoveSelection(-1, 0);
                    AssertEqual(0, kbd.SelectedCol, "Should reach leftmost col 0 ('a'), not wrap!");
                    // Moving left from col 0 should wrap to last key in row 1
                    kbd.MoveSelection(-1, 0);
                    AssertTrue(kbd.SelectedCol > 0, "Should wrap from col 0 to last column");
                }
            });

            Run("Steam Xbox support detection parses config.vdf accurately", () =>
            {
                AssertFalse(Program.IsSteamXboxSupportEnabled(""), "Empty config should be false");
                AssertFalse(Program.IsSteamXboxSupportEnabled("\"controller_xbox_support\"\t\t\"0\""), "Disabled should be false");
                AssertTrue(Program.IsSteamXboxSupportEnabled("\"controller_xbox_support\"\t\t\"1\""), "Enabled with tabs should be true");
                AssertTrue(Program.IsSteamXboxSupportEnabled("\"controller_xbox_support\" \"1\""), "Enabled with space should be true");
            });

            Run("Cursor sensitivity multiplier scales speed proportionally", () =>
            {
                double ax1 = 0, ay1 = 0, ax2 = 0, ay2 = 0;
                Point basePt = Program.DesktopController.ComputeStickCursorDelta(short.MaxValue, 0, 1.0 / 60.0, ref ax1, ref ay1, 1.0);
                Point fastPt = Program.DesktopController.ComputeStickCursorDelta(short.MaxValue, 0, 1.0 / 60.0, ref ax2, ref ay2, 2.0);
                AssertTrue(fastPt.X >= basePt.X * 2 - 1, "2.0x sensitivity should roughly double delta");
            });

            Console.WriteLine();
            Console.WriteLine(string.Format("Tests passed: {0}, failed: {1}", _passed, _failed));
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
