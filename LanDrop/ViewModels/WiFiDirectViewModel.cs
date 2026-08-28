using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LanDrop.Services;
using Microsoft.Extensions.Logging;

namespace LanDrop.ViewModels
{
    public partial class WiFiDirectViewModel : ObservableObject
    {
        private static readonly SolidColorBrush GreenBrush = CreateFrozenBrush("#22C55E");
        private static readonly SolidColorBrush AmberBrush = CreateFrozenBrush("#F59E0B");
        private static readonly SolidColorBrush RedBrush   = CreateFrozenBrush("#EF4444");
        private static readonly SolidColorBrush GrayBrush  = CreateFrozenBrush("#7A9090");

        private static SolidColorBrush CreateFrozenBrush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        private readonly WiFiDirectService            _svc;
        private readonly ILogger<WiFiDirectViewModel> _log;

        [ObservableProperty] private HotspotState _hotspotState    = HotspotState.Stopped;
        [ObservableProperty] private string  _ssid           = "LanDrop-Direct";
        [ObservableProperty] private string  _password       = "11111111";
        [ObservableProperty] private string  _hotspotIp      = string.Empty;
        [ObservableProperty] private string  _statusText     = "Off — click Start to create a direct connection";
        [ObservableProperty] private bool    _isRunning      = false;
        [ObservableProperty] private bool    _isStopped      = true;
        [ObservableProperty] private bool    _isBusy         = false;
        [ObservableProperty] private string  _toggleBtnText  = "Start Hotspot";
        [ObservableProperty] private bool    _isEditingPass  = false;
        [ObservableProperty] private bool    _isEditingSsid  = false;
        [ObservableProperty] private bool    _showPassword   = false;
        [ObservableProperty] private bool    _isSupported    = true;

        public string ToggleButtonText => ToggleBtnText;
        public string StateColor => HotspotState switch
        {
            HotspotState.Running => "#22C55E",
            HotspotState.Starting or HotspotState.Stopping => "#F59E0B",
            HotspotState.Failed => "#EF4444",
            _ => "#7A9090"
        };

        public SolidColorBrush StateBrush => HotspotState switch
        {
            HotspotState.Running => GreenBrush,
            HotspotState.Starting or HotspotState.Stopping => AmberBrush,
            HotspotState.Failed => RedBrush,
            _ => GrayBrush
        };

        partial void OnHotspotStateChanged(HotspotState value)
        {
            OnPropertyChanged(nameof(StateColor));
            OnPropertyChanged(nameof(StateBrush));
        }

        partial void OnToggleBtnTextChanged(string value)
        {
            OnPropertyChanged(nameof(ToggleButtonText));
        }

        public WiFiDirectViewModel(WiFiDirectService svc, ILogger<WiFiDirectViewModel> log)
        {
            _svc      = svc;
            _log      = log;
            Ssid      = svc.Ssid;
            Password  = svc.Password;

            _svc.StateChanged  += s => Application.Current?.Dispatcher?.Invoke(() => OnState(s));
            _svc.IpAssigned    += ip => Application.Current?.Dispatcher?.Invoke(() =>
            {
                HotspotIp  = ip;
                StatusText = $"Running  ·  IP {ip}  ·  Network: {Ssid}";
            });
            _svc.ErrorOccurred += m => Application.Current?.Dispatcher?.Invoke(() =>
            {
                StatusText = m;
                IsStopped  = true; IsBusy = false; IsRunning = false;
                ToggleBtnText = "Start Hotspot";
                OnPropertyChanged(nameof(ToggleButtonText));
                OnPropertyChanged(nameof(StateColor));
                OnPropertyChanged(nameof(StateBrush));
            });

            _ = CheckSupportAsync();
        }

        private async Task CheckSupportAsync()
        {
            try
            {
                IsSupported = await WiFiDirectService.IsHostedNetworkSupportedAsync();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to check hosted network support");
                IsSupported = true;
            }
        }

        [RelayCommand]
        private async Task ToggleHotspotAsync()
        {
            if (IsBusy) return;
            if (IsRunning) await _svc.StopHotspotAsync();
            else           await _svc.StartHotspotAsync();
        }

        [RelayCommand] private void ToggleShowPassword() => ShowPassword = !ShowPassword;

        [RelayCommand]
        private void RegeneratePassword()
        {
            if (!IsStopped) return;
            const string chars = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var rng = new Random();
            var pass = new string(Enumerable.Range(0, 10).Select(_ => chars[rng.Next(chars.Length)]).ToArray());
            Password = pass;
            _svc.Password = pass;
            App.Settings.WifiDirectPass = pass;
            App.SettingsSvc?.Save(App.Settings);
        }

        [RelayCommand] private void EditPassword() => IsEditingPass = true;
        [RelayCommand] private void EditSsid()     => IsEditingSsid = true;

        [RelayCommand]
        private void SavePassword(string val)
        {
            if (!string.IsNullOrWhiteSpace(val) && val.Length >= 8)
            {
                Password      = val;
                _svc.Password = val;
                App.Settings.WifiDirectPass = val;
                App.SettingsSvc?.Save(App.Settings);
            }
            IsEditingPass = false;
        }

        [RelayCommand]
        private void SaveSsid(string val)
        {
            if (!string.IsNullOrWhiteSpace(val))
            {
                Ssid      = val;
                _svc.Ssid = val;
                App.Settings.WifiDirectSsid = val;
                App.SettingsSvc?.Save(App.Settings);
            }
            IsEditingSsid = false;
        }

        [RelayCommand]
        private void CopyPassword()
        {
            if (!string.IsNullOrEmpty(Password))
                System.Windows.Clipboard.SetText(Password);
        }

        [RelayCommand]
        private void CopySsid()
        {
            if (!string.IsNullOrEmpty(Ssid))
                System.Windows.Clipboard.SetText(Ssid);
        }

        private void OnState(HotspotState s)
        {
            HotspotState  = s;
            IsRunning     = s == HotspotState.Running;
            IsStopped     = s == HotspotState.Stopped || s == HotspotState.Failed;
            IsBusy        = s == HotspotState.Starting || s == HotspotState.Stopping;
            ToggleBtnText = s switch
            {
                HotspotState.Running  => "Stop Hotspot",
                HotspotState.Starting => "Starting…",
                HotspotState.Stopping => "Stopping…",
                _                     => "Start Hotspot"
            };
            OnPropertyChanged(nameof(ToggleButtonText));
            OnPropertyChanged(nameof(StateColor));
            OnPropertyChanged(nameof(StateBrush));
            StatusText = s switch
            {
                HotspotState.Starting => "Creating hotspot…",
                HotspotState.Running  => $"Running  ·  Network: {Ssid}  ·  IP: {HotspotIp}",
                HotspotState.Stopping => "Stopping…",
                HotspotState.Failed   => "Failed — run LanDrop as Administrator",
                _                     => "Off — click Start to create a direct connection"
            };
        }
    }
}
