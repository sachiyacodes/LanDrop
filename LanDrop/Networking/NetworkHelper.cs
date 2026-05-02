// Networking/NetworkHelper.cs
// Utility methods for discovering local network addresses

using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LanDrop.Networking
{
    public static class NetworkHelper
    {
        /// <summary>
        /// Returns all active IPv4 addresses on non-loopback adapters.
        /// Useful for showing the user which address to share.
        /// </summary>
        public static IEnumerable<IPAddress> GetLocalIPv4Addresses()
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback) continue;

                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        yield return addr.Address;
                }
            }
        }

        /// <summary>
        /// Returns the "best" local IP: prefers the address whose first
        /// three octets match the most common LAN prefixes (192.168, 10., 172.16-31).
        /// Falls back to the first available address.
        /// </summary>
        public static string GetPreferredLocalIP()
        {
            IPAddress? preferred = null;
            IPAddress? fallback  = null;

            foreach (var ip in GetLocalIPv4Addresses())
            {
                var bytes = ip.GetAddressBytes();
                fallback ??= ip;

                bool isPrivate =
                    bytes[0] == 192 && bytes[1] == 168 ||
                    bytes[0] == 10                     ||
                    bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31;

                if (isPrivate)
                {
                    preferred = ip;
                    break;
                }
            }

            return (preferred ?? fallback)?.ToString() ?? "127.0.0.1";
        }
    }
}
