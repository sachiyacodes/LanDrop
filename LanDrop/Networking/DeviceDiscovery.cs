// Networking/DeviceDiscovery.cs
// UDP broadcast-based LAN peer discovery (no internet required)

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
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
            _udp?.Close();
            _logger.LogInformation("Discovery stopped.");
        }

        // ── Loops ─────────────────────────────────────────────────────────────

        private async Task BroadcastLoopAsync(CancellationToken ct)
        {
            var beacon = BuildBeacon();
            var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, _port);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await _udp!.SendAsync(beacon, beacon.Length, broadcastEndpoint);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Broadcast send failed.");
                }

                await Task.Delay(BroadcastIntervalMs, ct).ContinueWith(_ => { });
            }
        }

        private async Task ListenLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await _udp!.ReceiveAsync(ct);
                    ProcessBeacon(result.Buffer, result.RemoteEndPoint.Address);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Discovery receive error.");
                }
            }
        }

        private async Task CleanupLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(CleanupIntervalMs, ct).ContinueWith(_ => { });
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
                        existing.LastSeen = DateTime.UtcNow;
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

        private static bool IsOwnAddress(IPAddress addr)
        {
            // Compare against all local IPs
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
                if (ip.Equals(addr)) return true;
            return IPAddress.IsLoopback(addr);
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
