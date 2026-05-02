// Services/WiFiDirectService.cs
using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using LanDrop.Models;
using Microsoft.Extensions.Logging;

namespace LanDrop.Services
{
    public enum HotspotState { Stopped, Starting, Running, Stopping, Failed }

    public class WiFiDirectService
    {
        private readonly ILogger<WiFiDirectService> _logger;
        private readonly AppSettings _settings;

        public string       Ssid      { get; set; }
        public string       Password  { get; set; }
        public HotspotState State     { get; private set; } = HotspotState.Stopped;
        public string       HotspotIp { get; private set; } = string.Empty;

        public event Action<HotspotState>? StateChanged;
        public event Action<string>?       IpAssigned;
        public event Action<string>?       ErrorOccurred;

        public WiFiDirectService(AppSettings settings, ILogger<WiFiDirectService> logger)
        {
            _settings = settings;
            _logger   = logger;
            Ssid      = settings.WifiDirectSsid;
            Password  = settings.WifiDirectPass;
        }

        // ── Start ─────────────────────────────────────────────────────────

        public async Task<bool> StartHotspotAsync()
        {
            if (State == HotspotState.Running) return true;
            SetState(HotspotState.Starting);

            _settings.WifiDirectSsid = Ssid;
            _settings.WifiDirectPass = Password;
            App.SettingsSvc.Save(_settings);

            bool ok = await TryMobileHotspotAsync();
            if (!ok) ok = await TryNetshHotspotAsync();

            if (ok)
            {
                await Task.Delay(2500);
                HotspotIp = GetHotspotIp();
                SetState(HotspotState.Running);
                IpAssigned?.Invoke(HotspotIp);
            }
            else
            {
                HandleError("Could not start hotspot. Run LanDrop as Administrator.");
            }
            return ok;
        }

        // ── Stop ──────────────────────────────────────────────────────────

        public async Task StopHotspotAsync()
        {
            if (State == HotspotState.Stopped) return;
            SetState(HotspotState.Stopping);

            // Stop Windows Mobile Hotspot (WinRT API)
            await StopMobileHotspotAsync();

            // Also stop netsh legacy hotspot
            await RunCmd("netsh", "wlan stop hostednetwork");

            HotspotIp = string.Empty;
            SetState(HotspotState.Stopped);
        }

        // ── Mobile Hotspot start (WinRT via PowerShell) ───────────────────

        private async Task<bool> TryMobileHotspotAsync()
        {
            try
            {
                // Build PS script without C# string interpolation inside the PS body
                // Use string.Replace to inject SSID/Password safely
                string psTemplate =
                    "Add-Type -AssemblyName System.Runtime.WindowsRuntime\r\n" +
                    "$asTaskG = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]\r\n" +
                    "function Await($t,$r){ $n=$asTaskG.MakeGenericMethod($r); $k=$n.Invoke($null,@($t)); $k.Wait(-1)|Out-Null; $k.Result }\r\n" +
                    "function AwaitA($t){ $m=([System.WindowsRuntimeSystemExtensions].GetMethods()|Where-Object{$_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction'})[0]; $n=$m.Invoke($null,@($t)); $n.Wait(-1)|Out-Null }\r\n" +
                    "[Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager,Windows.Networking.NetworkOperators,ContentType=WindowsRuntime]|Out-Null\r\n" +
                    "$cp=[Windows.Networking.Connectivity.NetworkInformation,Windows.Networking.Connectivity,ContentType=WindowsRuntime]::GetInternetConnectionProfile()\r\n" +
                    "if($cp -eq $null){ $cp=[Windows.Networking.Connectivity.NetworkInformation,Windows.Networking.Connectivity,ContentType=WindowsRuntime]::GetConnectionProfiles()|Select-Object -First 1 }\r\n" +
                    "if($cp -eq $null){ Write-Output 'FAILED:noprofile'; exit 1 }\r\n" +
                    "$tm=[Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager,Windows.Networking.NetworkOperators,ContentType=WindowsRuntime]::CreateFromConnectionProfile($cp)\r\n" +
                    "$cfg=$tm.GetCurrentAccessPointConfiguration()\r\n" +
                    "$cfg.Ssid='__SSID__'\r\n" +
                    "$cfg.Passphrase='__PASS__'\r\n" +
                    "AwaitA($tm.ConfigureAccessPointAsync($cfg))\r\n" +
                    "$res=Await($tm.StartTetheringAsync()) ([Windows.Networking.NetworkOperators.NetworkOperatorTetheringOperationResult])\r\n" +
                    "if($res.Status -eq [Windows.Networking.NetworkOperators.TetheringOperationStatus]::Success){ Write-Output 'SUCCESS' }else{ Write-Output ('FAILED:'+$res.Status.ToString()) }\r\n";

                string ps = psTemplate
                    .Replace("__SSID__", Ssid.Replace("'", "''"))
                    .Replace("__PASS__", Password.Replace("'", "''"));

                string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ld_start.ps1");
                await System.IO.File.WriteAllTextAsync(tmp, ps);
                var r = await RunCmd("powershell", "-ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + tmp + "\"");
                System.IO.File.Delete(tmp);

                _logger.LogInformation("MobileHotspot PS output: {Out}", r.Out);
                return r.Out.Contains("SUCCESS");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MobileHotspot start failed");
                return false;
            }
        }

        // ── Mobile Hotspot stop (WinRT via PowerShell) ────────────────────

        private async Task StopMobileHotspotAsync()
        {
            try
            {
                string ps =
                    "Add-Type -AssemblyName System.Runtime.WindowsRuntime\r\n" +
                    "$asTaskG = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]\r\n" +
                    "function Await($t,$r){ $n=$asTaskG.MakeGenericMethod($r); $k=$n.Invoke($null,@($t)); $k.Wait(-1)|Out-Null; $k.Result }\r\n" +
                    "[Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager,Windows.Networking.NetworkOperators,ContentType=WindowsRuntime]|Out-Null\r\n" +
                    "$cp=[Windows.Networking.Connectivity.NetworkInformation,Windows.Networking.Connectivity,ContentType=WindowsRuntime]::GetInternetConnectionProfile()\r\n" +
                    "if($cp -eq $null){ $cp=[Windows.Networking.Connectivity.NetworkInformation,Windows.Networking.Connectivity,ContentType=WindowsRuntime]::GetConnectionProfiles()|Select-Object -First 1 }\r\n" +
                    "if($cp -eq $null){ Write-Output 'SKIPPED'; exit 0 }\r\n" +
                    "$tm=[Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager,Windows.Networking.NetworkOperators,ContentType=WindowsRuntime]::CreateFromConnectionProfile($cp)\r\n" +
                    "$res=Await($tm.StopTetheringAsync()) ([Windows.Networking.NetworkOperators.NetworkOperatorTetheringOperationResult])\r\n" +
                    "Write-Output ('STOP:'+$res.Status.ToString())\r\n";

                string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ld_stop.ps1");
                await System.IO.File.WriteAllTextAsync(tmp, ps);
                await RunCmd("powershell", "-ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + tmp + "\"");
                System.IO.File.Delete(tmp);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "StopMobileHotspot failed (non-critical)");
            }
        }

        // ── netsh legacy fallback ─────────────────────────────────────────

        private async Task<bool> TryNetshHotspotAsync()
        {
            await RunCmd("netsh", "wlan set hostednetwork mode=allow ssid=\"" + Ssid + "\" key=\"" + Password + "\"");
            var r = await RunCmd("netsh", "wlan start hostednetwork");
            return r.Out.Contains("started") || r.Code == 0;
        }

        // ── Helpers ───────────────────────────────────────────────────────

        public static async Task<bool> IsHostedNetworkSupportedAsync()
        {
            var r = await RunCmd("netsh", "wlan show drivers");
            return r.Out.Contains("Yes");
        }

        private static string GetHotspotIp()
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var a in nic.GetIPProperties().UnicastAddresses)
                {
                    if (a.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ip = a.Address.ToString();
                    if (ip.StartsWith("192.168.137.") || ip.StartsWith("192.168.173."))
                        return ip;
                }
            }
            return "192.168.137.1";
        }

        private void SetState(HotspotState s) { State = s; StateChanged?.Invoke(s); }

        private void HandleError(string m)
        {
            State = HotspotState.Failed;
            StateChanged?.Invoke(HotspotState.Failed);
            ErrorOccurred?.Invoke(m);
        }

        internal static async Task<(bool Ok, string Out, string Err, int Code)> RunCmd(string f, string a)
        {
            try
            {
                var p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName               = f,
                        Arguments              = a,
                        UseShellExecute        = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        CreateNoWindow         = true
                    }
                };
                p.Start();
                string o = await p.StandardOutput.ReadToEndAsync();
                string e = await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();
                return (p.ExitCode == 0, o, e, p.ExitCode);
            }
            catch (Exception ex) { return (false, "", ex.Message, -1); }
        }
    }
}
