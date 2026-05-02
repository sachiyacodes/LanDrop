// Models/TransferSession.cs
// Represents a single file or folder transfer session

using System;
using System.Collections.Generic;

namespace LanDrop.Models
{
    /// <summary>
    /// Represents the overall state of a transfer session.
    /// A session can contain one or more file entries.
    /// </summary>
    public enum TransferState
    {
        Idle,
        Connecting,
        Transferring,
        Paused,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// Metadata for a single file within a transfer session.
    /// </summary>
    public class FileEntry
    {
        /// <summary>Relative path (used for folder transfers to reconstruct hierarchy).</summary>
        public string RelativePath { get; set; } = string.Empty;

        /// <summary>Full local path on the sending side; save path on receiving side.</summary>
        public string FullPath { get; set; } = string.Empty;

        /// <summary>Size in bytes.</summary>
        public long SizeBytes { get; set; }

        /// <summary>SHA-256 hex digest of the complete file, computed on the sender.</summary>
        public string? Sha256Hash { get; set; }

        /// <summary>Bytes already sent/received for this file.</summary>
        public long TransferredBytes { get; set; }

        /// <summary>Whether this file's transfer has finished.</summary>
        public bool IsCompleted { get; set; }
    }

    /// <summary>
    /// A full transfer session — either sending or receiving.
    /// </summary>
    public class TransferSession
    {
        public Guid Id { get; } = Guid.NewGuid();

        /// <summary>True = we are sending; False = we are receiving.</summary>
        public bool IsSender { get; set; }

        public TransferState State { get; set; } = TransferState.Idle;

        public List<FileEntry> Files { get; set; } = new();

        /// <summary>Remote peer IP address.</summary>
        public string RemoteAddress { get; set; } = string.Empty;

        /// <summary>Friendly name of the remote device (received via discovery).</summary>
        public string RemoteDeviceName { get; set; } = string.Empty;

        /// <summary>Root save directory (receiver side).</summary>
        public string SaveDirectory { get; set; } = string.Empty;

        // ── Aggregate progress ───────────────────────────────────────────────

        public long TotalBytes => Files.Count > 0 ? GetTotalBytes() : 0;
        public long TransferredBytes { get; set; }

        public double ProgressPercent =>
            TotalBytes > 0 ? Math.Min(100.0, TransferredBytes / (double)TotalBytes * 100.0) : 0;

        public double SpeedBytesPerSecond { get; set; }

        public TimeSpan EstimatedTimeRemaining
        {
            get
            {
                if (SpeedBytesPerSecond <= 0) return TimeSpan.Zero;
                long remaining = TotalBytes - TransferredBytes;
                if (remaining <= 0) return TimeSpan.Zero;
                return TimeSpan.FromSeconds(remaining / SpeedBytesPerSecond);
            }
        }

        // ── Error info ───────────────────────────────────────────────────────
        public string? ErrorMessage { get; set; }

        // ── Timing ───────────────────────────────────────────────────────────
        public DateTime StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }

        private long GetTotalBytes()
        {
            long total = 0;
            foreach (var f in Files) total += f.SizeBytes;
            return total;
        }
    }
}
