// Models/TransferHistoryEntry.cs
using System;

namespace LanDrop.Models
{
    public class TransferHistoryEntry
    {
        public string   FileName     { get; set; } = string.Empty;
        public long     SizeBytes    { get; set; }
        public bool     IsSent       { get; set; }
        public bool     Success      { get; set; }
        public string   PeerName     { get; set; } = string.Empty;
        public DateTime Timestamp    { get; set; } = DateTime.Now;
        public double   SpeedMbps    { get; set; }

        public string DisplaySize => LanDrop.Helpers.FormatHelper.FormatBytes(SizeBytes);
        public string DisplayTime => Timestamp.ToString("MMM d, h:mm tt");
        public string Direction   => IsSent ? "↑ Sent" : "↓ Received";
        public string DirectionColor => IsSent ? "#4F8EF7" : "#3FB950";
    }
}
