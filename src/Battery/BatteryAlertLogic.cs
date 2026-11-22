using System;
using XboxBatteryMonitor.Core;

namespace XboxBatteryMonitor.Battery
{
    internal static class BatteryAlertLogic
    {
        public const int DefaultLowThreshold = 20;

        public static bool EvaluateBatteryAlert(int? exactPercent, byte xinputLevel, bool isWired, ref bool notifiedLow, int lowThreshold = DefaultLowThreshold)
        {
            if (isWired)
            {
                notifiedLow = false;
                return false;
            }

            int resetThreshold = Math.Min(95, lowThreshold + 5);
            bool isLow = exactPercent.HasValue
                ? exactPercent.Value <= lowThreshold
                : (xinputLevel <= NativeMethods.BATTERY_LEVEL_LOW);

            bool isRecovered = exactPercent.HasValue
                ? exactPercent.Value >= resetThreshold
                : (xinputLevel > NativeMethods.BATTERY_LEVEL_LOW);

            if (isLow && !notifiedLow)
            {
                return true;
            }
            if (isRecovered)
            {
                notifiedLow = false;
            }
            return false;
        }

        public static void UpdateChargingInference(int? exactPercent, bool isWired, ref int? lastPercent, ref bool isCharging)
        {
            if (!isWired && exactPercent.HasValue)
            {
                if (lastPercent.HasValue)
                {
                    if (exactPercent.Value > lastPercent.Value)
                        isCharging = true;
                    else if (exactPercent.Value < lastPercent.Value)
                        isCharging = false;
                }
                lastPercent = exactPercent.Value;
            }
            else
            {
                lastPercent = null;
                isCharging = false;
            }
        }

        public static string FormatTrayTooltip(string modelName, bool isWired, int? exactPercent, byte xinputLevel)
        {
            string tooltip = isWired
                ? string.Format("{0} (USB)", modelName)
                : exactPercent.HasValue
                    ? string.Format("{0}: {1}%", modelName, exactPercent.Value)
                    : string.Format("{0}: {1}", modelName, BatteryReader.LevelText(xinputLevel));
            return tooltip.Length > 63 ? tooltip.Substring(0, 60) + "..." : tooltip;
        }
    }
}
