// Models/DeviceInfo.cs
// Represents a discovered peer device on the local network

using System;
using System.Net;

namespace LanDrop.Models
{
    /// <summary>
    /// Represents a remote LanDrop peer discovered via UDP broadcast.
    /// </summary>
    public class DeviceInfo
    {
        /// <summary>Friendly name (typically Environment.MachineName).</summary>
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>IP address of the discovered device.</summary>
        public IPAddress Address { get; set; } = IPAddress.None;

        /// <summary>TCP port the device is listening on.</summary>
        public int Port { get; set; }

        /// <summary>App version string for compatibility checks.</summary>
        public string AppVersion { get; set; } = string.Empty;

        /// <summary>Last time a discovery beacon was received from this device.</summary>
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;

        /// <summary>Whether the device is still considered reachable (seen within 15 s).</summary>
        public bool IsAlive => (DateTime.UtcNow - LastSeen).TotalSeconds < 15;

        public override string ToString() => $"{DeviceName} ({Address})";
    }
}
