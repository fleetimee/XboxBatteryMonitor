using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using XboxBatteryMonitor.UI;

namespace XboxBatteryMonitor.Core
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Ensure Normal process priority even when launched via Task Scheduler
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal; } catch { }
            try { NativeMethods.SetProcessDpiAwarenessContext(new IntPtr(-4)); } // Per-Monitor V2
            catch { try { NativeMethods.SetProcessDPIAware(); } catch { } }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool testMode = args.Length > 0 && args[0].Equals("--test", StringComparison.OrdinalIgnoreCase);

            bool createdNew;
            using (Mutex mutex = new Mutex(true, @"Local\XboxBatteryMonitor_SingleInstance", out createdNew))
            {
                if (!createdNew && !testMode)
                {
                    return;
                }

                Application.Run(new TrayMonitorContext(testMode));
            }
        }
    }
}
