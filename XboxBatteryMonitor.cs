using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace XboxBatteryMonitor
{
    static class Program
    {
        // XInput constants
        private const int ERROR_SUCCESS = 0;
        private const byte BATTERY_DEVTYPE_GAMEPAD = 0x00;
        private const byte BATTERY_TYPE_DISCONNECTED = 0x00;
        private const byte BATTERY_TYPE_WIRED = 0x01;
        private const byte BATTERY_LEVEL_EMPTY = 0x00;
        private const byte BATTERY_LEVEL_LOW = 0x01;

        private const int LOW_THRESHOLD_PERCENT = 20;
        private const int RESET_THRESHOLD_PERCENT = 25;
        private const int POLL_INTERVAL_MS = 60000;

        // SetupAPI / CfgMgr32 constants for reading Bluetooth LE exact battery %
        private const int DIGCF_PRESENT = 0x02;
        private const int DIGCF_ALLCLASSES = 0x04;
        private const int CR_SUCCESS = 0;
        private const int DEVPROP_TYPE_BYTE = 0x00000003;

        [StructLayout(LayoutKind.Sequential)]
        struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XINPUT_BATTERY_INFORMATION
        {
            public byte BatteryType;
            public byte BatteryLevel;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DEVPROPKEY
        {
            public Guid fmtid;
            public uint pid;
        }

        [DllImport("xinput1_4.dll")]
        private static extern int XInputGetState(int dwUserIndex, ref XINPUT_STATE pState);

        [DllImport("xinput1_4.dll")]
        private static extern int XInputGetBatteryInformation(int dwUserIndex, byte devType, ref XINPUT_BATTERY_INFORMATION pBatteryInformation);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(IntPtr ClassGuid, string Enumerator, IntPtr hwndParent, int Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceRegistryProperty(
            IntPtr DeviceInfoSet,
            ref SP_DEVINFO_DATA DeviceInfoData,
            uint Property,
            out uint PropertyRegDataType,
            byte[] PropertyBuffer,
            uint PropertyBufferSize,
            out uint RequiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Get_DevNode_Property(
            uint dnDevInst,
            ref DEVPROPKEY PropertyKey,
            out uint PropertyType,
            byte[] PropertyBuffer,
            ref uint PropertyBufferSize,
            uint ulFlags);

        [STAThread]
        static void Main(string[] args)
        {
            bool testMode = args.Length > 0 && args[0].Equals("--test", StringComparison.OrdinalIgnoreCase);

            bool createdNew;
            using (Mutex mutex = new Mutex(true, @"Local\XboxBatteryMonitor_SingleInstance", out createdNew))
            {
                if (!createdNew && !testMode)
                {
                    return;
                }

                // Wait briefly on startup in case PnP event fired slightly before XInput is ready
                if (!testMode)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        if (IsAnyControllerConnected()) break;
                        Thread.Sleep(1000);
                    }
                }

                if (!IsAnyControllerConnected() && !testMode)
                {
                    return;
                }

                bool[] notifiedLow = new bool[4];
                int disconnectedChecks = 0;

                using (NotifyIcon trayIcon = new NotifyIcon())
                {
                    trayIcon.Icon = SystemIcons.Warning;
                    trayIcon.Visible = true;
                    trayIcon.Text = "Xbox Controller Battery Monitor";

                    if (testMode)
                    {
                        int? blePct = GetBluetoothXboxBatteryPercent();
                        string info = blePct.HasValue
                            ? string.Format("Controller connected! Current Bluetooth battery: {0}%", blePct.Value)
                            : "Controller connected! Monitoring battery level.";
                        ShowNotification(trayIcon, "Xbox Battery Monitor Ready", info, ToolTipIcon.Info);
                        Thread.Sleep(5000);
                        trayIcon.Visible = false;
                        return;
                    }

                    while (true)
                    {
                        bool anyConnected = false;

                        for (int i = 0; i < 4; i++)
                        {
                            XINPUT_STATE state = new XINPUT_STATE();
                            if (XInputGetState(i, ref state) != ERROR_SUCCESS)
                            {
                                notifiedLow[i] = false;
                                continue;
                            }

                            anyConnected = true;

                            XINPUT_BATTERY_INFORMATION bat = new XINPUT_BATTERY_INFORMATION();
                            if (XInputGetBatteryInformation(i, BATTERY_DEVTYPE_GAMEPAD, ref bat) != ERROR_SUCCESS)
                            {
                                continue;
                            }

                            if (bat.BatteryType == BATTERY_TYPE_DISCONNECTED || bat.BatteryType == BATTERY_TYPE_WIRED)
                            {
                                notifiedLow[i] = false;
                                continue;
                            }

                            int? exactPercent = GetBluetoothXboxBatteryPercent();
                            bool isLow = false;
                            bool isRecovered = false;

                            if (exactPercent.HasValue)
                            {
                                isLow = exactPercent.Value <= LOW_THRESHOLD_PERCENT;
                                isRecovered = exactPercent.Value >= RESET_THRESHOLD_PERCENT;
                            }
                            else
                            {
                                isLow = (bat.BatteryLevel == BATTERY_LEVEL_LOW || bat.BatteryLevel == BATTERY_LEVEL_EMPTY);
                                isRecovered = (bat.BatteryLevel > BATTERY_LEVEL_LOW);
                            }

                            if (isLow && !notifiedLow[i])
                            {
                                string msg = exactPercent.HasValue
                                    ? string.Format("Xbox Controller {0} battery is at {1}%. Please charge or swap batteries.", i + 1, exactPercent.Value)
                                    : string.Format("Xbox Controller {0} battery is low (below ~20%). Please charge or swap batteries.", i + 1);

                                ShowNotification(trayIcon, "Low Xbox Controller Battery", msg, ToolTipIcon.Warning);
                                notifiedLow[i] = true;
                            }
                            else if (isRecovered)
                            {
                                notifiedLow[i] = false;
                            }
                        }

                        if (!anyConnected)
                        {
                            disconnectedChecks++;
                            if (disconnectedChecks >= 2)
                            {
                                break;
                            }
                        }
                        else
                        {
                            disconnectedChecks = 0;
                        }

                        Thread.Sleep(POLL_INTERVAL_MS);
                    }

                    trayIcon.Visible = false;
                }
            }
        }

        private static bool IsAnyControllerConnected()
        {
            XINPUT_STATE state = new XINPUT_STATE();
            for (int i = 0; i < 4; i++)
            {
                if (XInputGetState(i, ref state) == ERROR_SUCCESS)
                {
                    return true;
                }
            }
            return false;
        }

        private static void ShowNotification(NotifyIcon trayIcon, string title, string message, ToolTipIcon icon)
        {
            trayIcon.BalloonTipTitle = title;
            trayIcon.BalloonTipText = message;
            trayIcon.BalloonTipIcon = icon;
            trayIcon.ShowBalloonTip(8000);
            Application.DoEvents();
        }

        private static int? GetBluetoothXboxBatteryPercent()
        {
            IntPtr devInfoSet = SetupDiGetClassDevs(IntPtr.Zero, "BTHLE", IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (devInfoSet == IntPtr.Zero || devInfoSet == new IntPtr(-1))
            {
                return null;
            }

            try
            {
                DEVPROPKEY batteryKey = new DEVPROPKEY
                {
                    fmtid = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"),
                    pid = 2
                };

                SP_DEVINFO_DATA devInfoData = new SP_DEVINFO_DATA();
                devInfoData.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));

                uint index = 0;
                while (SetupDiEnumDeviceInfo(devInfoSet, index++, ref devInfoData))
                {
                    string name = GetDeviceFriendlyName(devInfoSet, ref devInfoData);
                    if (string.IsNullOrEmpty(name) || name.IndexOf("Xbox", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    byte[] buffer = new byte[4];
                    uint propType;
                    uint size = (uint)buffer.Length;
                    int cr = CM_Get_DevNode_Property(devInfoData.DevInst, ref batteryKey, out propType, buffer, ref size, 0);
                    if (cr == CR_SUCCESS && propType == DEVPROP_TYPE_BYTE && size >= 1)
                    {
                        return buffer[0];
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devInfoSet);
            }

            return null;
        }

        private static string GetDeviceFriendlyName(IntPtr devInfoSet, ref SP_DEVINFO_DATA devInfoData)
        {
            const uint SPDRP_FRIENDLYNAME = 0x0000000C;
            const uint SPDRP_DEVICEDESC = 0x00000000;

            foreach (uint prop in new uint[] { SPDRP_FRIENDLYNAME, SPDRP_DEVICEDESC })
            {
                byte[] buffer = new byte[512];
                uint regType;
                uint reqSize;
                if (SetupDiGetDeviceRegistryProperty(devInfoSet, ref devInfoData, prop, out regType, buffer, (uint)buffer.Length, out reqSize))
                {
                    string val = System.Text.Encoding.Unicode.GetString(buffer, 0, (int)reqSize).TrimEnd('\0');
                    if (!string.IsNullOrEmpty(val))
                    {
                        return val;
                    }
                }
            }

            return null;
        }
    }
}
