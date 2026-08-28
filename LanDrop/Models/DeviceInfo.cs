// Models/DeviceInfo.cs
// Represents a discovered peer device on the local network

using System;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LanDrop.Models
{
    /// <summary>
    /// Represents a remote LanDrop peer discovered via UDP broadcast.
    /// </summary>
    public partial class DeviceInfo : ObservableObject
    {
        /// <summary>Friendly name (typically Environment.MachineName).</summary>
        [ObservableProperty] private string _deviceName = string.Empty;

        /// <summary>IP address of the discovered device.</summary>
        [ObservableProperty] private IPAddress _address = IPAddress.None;

        /// <summary>TCP port the device is listening on.</summary>
        [ObservableProperty] private int _port;

        /// <summary>App version string for compatibility checks.</summary>
        [ObservableProperty] private string _appVersion = string.Empty;

        /// <summary>Last time a discovery beacon was received from this device.</summary>
        [ObservableProperty] private DateTime _lastSeen = DateTime.UtcNow;

        /// <summary>Whether the device is still considered reachable (seen within 15 s).</summary>
        public bool IsAlive => (DateTime.UtcNow - LastSeen).TotalSeconds < 15;

        public override string ToString() => $"{DeviceName} ({Address})";
    }
}
