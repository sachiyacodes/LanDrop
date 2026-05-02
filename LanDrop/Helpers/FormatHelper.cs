// Helpers/FormatHelper.cs
// Human-readable formatting for bytes, speeds, and time spans

using System;

namespace LanDrop.Helpers
{
    public static class FormatHelper
    {
        private static readonly string[] SizeUnits = { "B", "KB", "MB", "GB", "TB" };

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            double value = bytes;
            int    unit  = 0;
            while (value >= 1024 && unit < SizeUnits.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return $"{value:F1} {SizeUnits[unit]}";
        }

        public static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0) return "— MB/s";
            double mbs = bytesPerSecond / (1024 * 1024);
            return mbs >= 1 ? $"{mbs:F1} MB/s" : $"{bytesPerSecond / 1024:F0} KB/s";
        }

        public static string FormatEta(TimeSpan eta)
        {
            if (eta == TimeSpan.Zero) return "—";
            if (eta.TotalSeconds < 60) return $"{(int)eta.TotalSeconds}s";
            if (eta.TotalMinutes < 60) return $"{(int)eta.TotalMinutes}m {eta.Seconds}s";
            return $"{(int)eta.TotalHours}h {eta.Minutes}m";
        }

        public static string FormatPercent(double percent) =>
            $"{percent:F0}%";
    }
}
