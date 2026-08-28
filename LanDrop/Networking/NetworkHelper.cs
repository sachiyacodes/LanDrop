// Networking/NetworkHelper.cs
// Utility methods for discovering local network addresses

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LanDrop.Networking
{
    public static class NetworkHelper
    {
        private static readonly string[] VirtualKeywords =
        {
            "vethernet", "wsl", "virtualbox", "vmware", "tunnel",
            "tap", "tun", "hyper-v", "virtual", "tailscale",
            "zerotier", "wireguard", "npcap", "vpn", "pseudo"
        };

        /// <summary>
        /// Checks if a network interface is a virtual, tunnel, or loopback adapter.
        /// </summary>
        public static bool IsVirtualAdapter(NetworkInterface nic)
        {
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                return true;

            var name = nic.Name.ToLowerInvariant();
            var desc = nic.Description.ToLowerInvariant();

            foreach (var kw in VirtualKeywords)
            {
                if (name.Contains(kw) || desc.Contains(kw))
                    return true;
            }

            return false;
        }

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
        /// Returns the "best" local IP: prioritizes physical LAN/WiFi adapters
        /// matching common private LAN prefixes (192.168., 10., 172.16-31.),
        /// filtering out virtual and loopback adapters (e.g. vEthernet, WSL, VirtualBox, VMware, Tunnel, TAP).
        /// Falls back to virtual or any available address if no physical adapter is present.
        /// </summary>
        public static string GetPreferredLocalIP()
        {
            IPAddress? physicalPrivate = null;
            IPAddress? physicalFallback = null;
            IPAddress? anyPrivate = null;
            IPAddress? anyFallback = null;

            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback) continue;

                    bool isVirtual = IsVirtualAdapter(nic);

                    foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;

                        var ip = addr.Address;
                        var bytes = ip.GetAddressBytes();

                        // Skip link-local 169.254.x.x
                        bool isLinkLocal = bytes[0] == 169 && bytes[1] == 254;

                        bool isPrivate =
                            bytes[0] == 192 && bytes[1] == 168 ||
                            bytes[0] == 10                     ||
                            bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31;

                        if (!isVirtual)
                        {
                            if (isPrivate && physicalPrivate == null)
                                physicalPrivate = ip;
                            else if (!isLinkLocal && physicalFallback == null)
                                physicalFallback = ip;
                        }
                        else
                        {
                            if (isPrivate && anyPrivate == null)
                                anyPrivate = ip;
                            else if (!isLinkLocal && anyFallback == null)
                                anyFallback = ip;
                        }
                    }
                }
            }
            catch
            {
                // Fallback on any error
            }

            var chosen = physicalPrivate ?? physicalFallback ?? anyPrivate ?? anyFallback;
            return chosen?.ToString() ?? "127.0.0.1";
        }
    }
}
