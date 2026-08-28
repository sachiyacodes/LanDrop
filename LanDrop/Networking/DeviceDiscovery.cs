// Networking/DeviceDiscovery.cs
// UDP broadcast-based LAN peer discovery (no internet required)

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanDrop.Models;
using Microsoft.Extensions.Logging;

namespace LanDrop.Networking
{
    /// <summary>
    /// Periodically broadcasts a UDP beacon and listens for beacons from peers.
    /// Raises <see cref="DeviceDiscovered"/> and <see cref="DeviceLost"/> events.
    /// </summary>
    public class DeviceDiscovery : IDisposable
    {
        // ── Config ────────────────────────────────────────────────────────────
        private const int BroadcastIntervalMs = 3_000;
        private const int CleanupIntervalMs   = 5_000;
        private const string MagicHeader      = "LANDROP_BEACON_V1";

        // ── Fields ────────────────────────────────────────────────────────────
        private readonly int              _port;
        private readonly AppSettings      _settings;
        private readonly ILogger          _logger;
        private UdpClient?                _udp;
        private CancellationTokenSource?  _cts;
        private readonly ConcurrentDictionary<string, DeviceInfo> _devices = new();

        // ── Events ────────────────────────────────────────────────────────────
        public event Action<DeviceInfo>? DeviceDiscovered;
        public event Action<DeviceInfo>? DeviceLost;

        // ── Constructor ───────────────────────────────────────────────────────
        public DeviceDiscovery(AppSettings settings, ILogger<DeviceDiscovery> logger)
        {
            _settings = settings;
            _port     = settings.DiscoveryPort;
            _logger   = logger;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Return a snapshot of currently-alive devices.</summary>
        public IEnumerable<DeviceInfo> KnownDevices => _devices.Values;

        /// <summary>Start broadcasting and listening.</summary>
        public void Start()
        {
            _cts = new CancellationTokenSource();
            _udp = new UdpClient();
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, _port));
            _udp.EnableBroadcast = true;

            _ = BroadcastLoopAsync(_cts.Token);
            _ = ListenLoopAsync(_cts.Token);
            _ = CleanupLoopAsync(_cts.Token);

            _logger.LogInformation("Discovery started on UDP port {Port}", _port);
        }

        /// <summary>Stop broadcasting and listening.</summary>
        public void Stop()
        {
            _cts?.Cancel();
            try
            {
                _udp?.Close();
            }
            catch { }
            _logger.LogInformation("Discovery stopped.");
        }

        // ── Loops ─────────────────────────────────────────────────────────────

        private async Task BroadcastLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var udp = _udp;
                    if (udp == null) break;

                    var beacon = BuildBeacon();
                    var broadcastAddresses = GetBroadcastAddresses();

                    foreach (var ip in broadcastAddresses)
                    {
                        if (ct.IsCancellationRequested) break;
                        try
                        {
                            var endpoint = new IPEndPoint(ip, _port);
                            await udp.SendAsync(beacon, beacon.Length, endpoint);
                        }
                        catch (Exception ex) when (!ct.IsCancellationRequested)
                        {
                            _logger.LogTrace(ex, "Broadcast send failed to {IP}", ip);
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Broadcast loop error.");
                }

                try
                {
                    await Task.Delay(BroadcastIntervalMs, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task ListenLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var udp = _udp;
                    if (udp == null) break;

                    var result = await udp.ReceiveAsync(ct);
                    ProcessBeacon(result.Buffer, result.RemoteEndPoint.Address);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (SocketException ex) when (ct.IsCancellationRequested ||
                                                 ex.SocketErrorCode == SocketError.Interrupted ||
                                                 ex.SocketErrorCode == SocketError.OperationAborted)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested) break;
                    _logger.LogWarning(ex, "Discovery receive error.");
                }
            }
        }

        private async Task CleanupLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(CleanupIntervalMs, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                foreach (var kv in _devices)
                {
                    if (!kv.Value.IsAlive && _devices.TryRemove(kv.Key, out var lost))
                    {
                        _logger.LogInformation("Device lost: {Name}", lost.DeviceName);
                        DeviceLost?.Invoke(lost);
                    }
                }
            }
        }

        // ── Packet handling ───────────────────────────────────────────────────

        private record BeaconPayload(string Magic, string DeviceName, int Port, string Version);

        private byte[] BuildBeacon()
        {
            var payload = new BeaconPayload(
                MagicHeader,
                Environment.MachineName,
                _settings.TransferPort,
                App.Version
            );
            return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        }

        private void ProcessBeacon(byte[] data, IPAddress fromAddress)
        {
            try
            {
                var text    = Encoding.UTF8.GetString(data);
                var payload = JsonSerializer.Deserialize<BeaconPayload>(text);
                if (payload is null || payload.Magic != MagicHeader) return;

                // Ignore our own broadcasts
                if (IsOwnAddress(fromAddress)) return;

                string key = fromAddress.ToString();
                bool isNew = !_devices.ContainsKey(key);

                var device = _devices.AddOrUpdate(key,
                    _ => new DeviceInfo
                    {
                        DeviceName = payload.DeviceName,
                        Address    = fromAddress,
                        Port       = payload.Port,
                        AppVersion = payload.Version,
                        LastSeen   = DateTime.UtcNow
                    },
                    (_, existing) =>
                    {
                        existing.DeviceName = payload.DeviceName;
                        existing.Port       = payload.Port;
                        existing.AppVersion = payload.Version;
                        existing.LastSeen   = DateTime.UtcNow;
                        return existing;
                    });

                if (isNew)
                {
                    _logger.LogInformation("Device discovered: {Name} @ {IP}", device.DeviceName, fromAddress);
                    DeviceDiscovered?.Invoke(device);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Malformed beacon ignored.");
            }
        }

        private static HashSet<IPAddress> GetBroadcastAddresses()
        {
            var broadcastAddresses = new HashSet<IPAddress> { IPAddress.Broadcast };

            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    var ipProps = nic.GetIPProperties();
                    foreach (var unicast in ipProps.UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;

                        var mask = unicast.IPv4Mask;
                        if (mask == null || mask.Equals(IPAddress.Any) || mask.Equals(IPAddress.None))
                            continue;

                        var ipBytes = unicast.Address.GetAddressBytes();
                        var maskBytes = mask.GetAddressBytes();
                        if (ipBytes.Length != 4 || maskBytes.Length != 4) continue;

                        var broadcastBytes = new byte[4];
                        for (int i = 0; i < 4; i++)
                        {
                            broadcastBytes[i] = (byte)(ipBytes[i] | (~maskBytes[i]));
                        }

                        broadcastAddresses.Add(new IPAddress(broadcastBytes));
                    }
                }
            }
            catch
            {
                // Fallback to 255.255.255.255 if network query fails
            }

            return broadcastAddresses;
        }

        private static bool IsOwnAddress(IPAddress addr)
        {
            if (IPAddress.IsLoopback(addr)) return true;

            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;

                    var ipProps = nic.GetIPProperties();
                    foreach (var unicast in ipProps.UnicastAddresses)
                    {
                        if (unicast.Address.Equals(addr))
                            return true;
                    }
                }
            }
            catch
            {
                try
                {
                    var host = Dns.GetHostEntry(Dns.GetHostName());
                    foreach (var ip in host.AddressList)
                        if (ip.Equals(addr)) return true;
                }
                catch { }
            }

            return false;
        }

        // ── IDisposable ───────────────────────────────────────────────────────
        public void Dispose()
        {
            Stop();
            _udp?.Dispose();
            _cts?.Dispose();
        }
    }
}
