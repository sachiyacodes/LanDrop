// Models/TransferHistoryEntry.cs
using System;
using System.Windows.Media;

namespace LanDrop.Models
{
    public class TransferHistoryEntry
    {
        private static readonly SolidColorBrush FailedBrush   = CreateFrozenBrush("#EF4444");
        private static readonly SolidColorBrush SentBrush     = CreateFrozenBrush("#4F8EF7");
        private static readonly SolidColorBrush ReceivedBrush = CreateFrozenBrush("#10B981");

        private static SolidColorBrush CreateFrozenBrush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        public string   FileName     { get; set; } = string.Empty;
        public long     SizeBytes    { get; set; }
        public bool     IsSent       { get; set; }
        public bool     Success      { get; set; }
        public string   PeerName     { get; set; } = string.Empty;
        public DateTime Timestamp    { get; set; } = DateTime.Now;
        public double   SpeedMbps    { get; set; }

        public string DisplaySize => LanDrop.Helpers.FormatHelper.FormatBytes(SizeBytes);
        public string DisplayTime => Timestamp.ToString("MMM d, h:mm tt");
        public string Direction => !Success
            ? (IsSent ? "↑ Failed" : "↓ Failed")
            : (IsSent ? "↑ Sent" : "↓ Received");

        public string DirectionColor => !Success
            ? "#EF4444"
            : (IsSent ? "#4F8EF7" : "#10B981");

        public SolidColorBrush DirectionBrush => !Success
            ? FailedBrush
            : (IsSent ? SentBrush : ReceivedBrush);
    }
}
