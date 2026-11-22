using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using XboxBatteryMonitor.Core;

namespace XboxBatteryMonitor.Battery
{
    internal class ControllerSnapshot
    {
        public int Slot;
        public string ModelName;
        public string ConnectionType;
        public string BatteryTypeLabel;
        public int? ExactPercent;
        public byte XInputLevel;
        public bool IsWired;
        public bool IsChargingInferred;
    }

    internal static class BatteryReader
    {
        public static bool IsAnyControllerConnected()
        {
            NativeMethods.XINPUT_STATE state = new NativeMethods.XINPUT_STATE();
            for (int i = 0; i < 4; i++)
            {
                if (NativeMethods.XInputGetState(i, ref state) == NativeMethods.ERROR_SUCCESS)
                {
                    return true;
                }
            }
            return false;
        }

        public static List<ControllerSnapshot> GetConnectedControllers()
        {
            var list = new List<ControllerSnapshot>();
            string bleName;
            int? blePct = GetBluetoothXboxInfo(out bleName);
            string usbModel = GetUsbXboxModelName();

            for (int i = 0; i < 4; i++)
            {
                NativeMethods.XINPUT_STATE state = new NativeMethods.XINPUT_STATE();
                if (NativeMethods.XInputGetState(i, ref state) != NativeMethods.ERROR_SUCCESS)
                {
                    continue;
                }

                NativeMethods.XINPUT_BATTERY_INFORMATION bat = new NativeMethods.XINPUT_BATTERY_INFORMATION();
                NativeMethods.XInputGetBatteryInformation(i, NativeMethods.BATTERY_DEVTYPE_GAMEPAD, ref bat);

                bool isWired = (bat.BatteryType == NativeMethods.BATTERY_TYPE_WIRED);
                string model = isWired
                    ? (!string.IsNullOrEmpty(usbModel) ? usbModel : "Xbox Controller (USB)")
                    : (!string.IsNullOrEmpty(bleName) ? bleName : "Xbox Wireless Controller");

                list.Add(new ControllerSnapshot
                {
                    Slot = i + 1,
                    ModelName = model,
                    ConnectionType = isWired ? "USB Cable" : "Bluetooth LE",
                    BatteryTypeLabel = GetBatteryTypeLabel(bat.BatteryType),
                    ExactPercent = isWired ? null : blePct,
                    XInputLevel = bat.BatteryLevel,
                    IsWired = isWired
                });
            }

            return list;
        }

        public static string LevelText(byte level)
        {
            switch (level)
            {
                case NativeMethods.BATTERY_LEVEL_FULL: return "Full";
                case NativeMethods.BATTERY_LEVEL_MEDIUM: return "Medium";
                case NativeMethods.BATTERY_LEVEL_LOW: return "Low";
                default: return "Empty";
            }
        }

        private static string GetBatteryTypeLabel(byte batteryType)
        {
            switch (batteryType)
            {
                case NativeMethods.BATTERY_TYPE_WIRED: return "USB Powered";
                case NativeMethods.BATTERY_TYPE_ALKALINE: return "Alkaline (AA)";
                case NativeMethods.BATTERY_TYPE_NIMH: return "NiMH / Rechargeable";
                default: return "Wireless Battery";
            }
        }

        private static int? GetBluetoothXboxInfo(out string modelName)
        {
            modelName = null;
            IntPtr devInfoSet = NativeMethods.SetupDiGetClassDevs(IntPtr.Zero, "BTHLE", IntPtr.Zero, NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_ALLCLASSES);
            if (devInfoSet == IntPtr.Zero || devInfoSet == new IntPtr(-1))
            {
                return null;
            }

            try
            {
                NativeMethods.DEVPROPKEY batteryKey = new NativeMethods.DEVPROPKEY
                {
                    fmtid = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"),
                    pid = 2
                };

                NativeMethods.SP_DEVINFO_DATA devInfoData = new NativeMethods.SP_DEVINFO_DATA();
                devInfoData.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.SP_DEVINFO_DATA));

                uint index = 0;
                while (NativeMethods.SetupDiEnumDeviceInfo(devInfoSet, index++, ref devInfoData))
                {
                    string name = GetDeviceRegistryString(devInfoSet, ref devInfoData, 0x0000000C)
                               ?? GetDeviceRegistryString(devInfoSet, ref devInfoData, 0x00000000);

                    if (string.IsNullOrEmpty(name) || name.IndexOf("Xbox", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    modelName = name;

                    byte[] buffer = new byte[4];
                    uint propType;
                    uint size = (uint)buffer.Length;
                    int cr = NativeMethods.CM_Get_DevNode_Property(devInfoData.DevInst, ref batteryKey, out propType, buffer, ref size, 0);
                    if (cr == NativeMethods.CR_SUCCESS && propType == NativeMethods.DEVPROP_TYPE_BYTE && size >= 1)
                    {
                        return buffer[0];
                    }
                }
            }
            finally
            {
                NativeMethods.SetupDiDestroyDeviceInfoList(devInfoSet);
            }

            return null;
        }

        private static string GetUsbXboxModelName()
        {
            IntPtr devInfoSet = NativeMethods.SetupDiGetClassDevs(IntPtr.Zero, "USB", IntPtr.Zero, NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_ALLCLASSES);
            if (devInfoSet == IntPtr.Zero || devInfoSet == new IntPtr(-1))
            {
                return null;
            }

            try
            {
                NativeMethods.SP_DEVINFO_DATA devInfoData = new NativeMethods.SP_DEVINFO_DATA();
                devInfoData.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.SP_DEVINFO_DATA));

                uint index = 0;
                while (NativeMethods.SetupDiEnumDeviceInfo(devInfoSet, index++, ref devInfoData))
                {
                    StringBuilder idBuf = new StringBuilder(256);
                    uint reqSize;
                    if (!NativeMethods.SetupDiGetDeviceInstanceId(devInfoSet, ref devInfoData, idBuf, (uint)idBuf.Capacity, out reqSize))
                    {
                        continue;
                    }

                    string instanceId = idBuf.ToString();
                    if (instanceId.IndexOf("VID_045E", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    if (instanceId.IndexOf("PID_0B12", StringComparison.OrdinalIgnoreCase) >= 0) return "Xbox Series X|S Controller (USB)";
                    if (instanceId.IndexOf("PID_0B00", StringComparison.OrdinalIgnoreCase) >= 0) return "Xbox Elite Series 2 (USB)";
                    if (instanceId.IndexOf("PID_02EA", StringComparison.OrdinalIgnoreCase) >= 0) return "Xbox One S Controller (USB)";
                    if (instanceId.IndexOf("PID_02DD", StringComparison.OrdinalIgnoreCase) >= 0) return "Xbox One Controller (USB)";
                    if (instanceId.IndexOf("PID_028E", StringComparison.OrdinalIgnoreCase) >= 0) return "Xbox 360 Controller (USB)";

                    string name = GetDeviceRegistryString(devInfoSet, ref devInfoData, 0x0000000C)
                               ?? GetDeviceRegistryString(devInfoSet, ref devInfoData, 0x00000000);
                    if (!string.IsNullOrEmpty(name) && name.IndexOf("Xbox", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return name;
                    }
                }
            }
            finally
            {
                NativeMethods.SetupDiDestroyDeviceInfoList(devInfoSet);
            }

            return null;
        }

        private static string GetDeviceRegistryString(IntPtr devInfoSet, ref NativeMethods.SP_DEVINFO_DATA devInfoData, uint property)
        {
            byte[] buffer = new byte[512];
            uint regType;
            uint reqSize;
            if (NativeMethods.SetupDiGetDeviceRegistryProperty(devInfoSet, ref devInfoData, property, out regType, buffer, (uint)buffer.Length, out reqSize))
            {
                string val = Encoding.Unicode.GetString(buffer, 0, (int)reqSize).TrimEnd('\0');
                if (!string.IsNullOrEmpty(val))
                {
                    return val;
                }
            }
            return null;
        }
    }
}
