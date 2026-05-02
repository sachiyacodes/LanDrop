// ViewModels/WiFiDirectViewModel.cs
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LanDrop.Services;
using Microsoft.Extensions.Logging;

namespace LanDrop.ViewModels
{
    public partial class WiFiDirectViewModel : ObservableObject
    {
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

        public WiFiDirectViewModel(WiFiDirectService svc, ILogger<WiFiDirectViewModel> log)
        {
            _svc      = svc;
            _log      = log;
            Ssid      = svc.Ssid;
            Password  = svc.Password;

            _svc.StateChanged  += s => Application.Current.Dispatcher.Invoke(() => OnState(s));
            _svc.IpAssigned    += ip => Application.Current.Dispatcher.Invoke(() =>
            {
                HotspotIp  = ip;
                StatusText = $"Running  ·  IP {ip}  ·  Network: {Ssid}";
            });
            _svc.ErrorOccurred += m => Application.Current.Dispatcher.Invoke(() =>
            {
                StatusText = m;
                IsStopped  = true; IsBusy = false; IsRunning = false;
                ToggleBtnText = "Start Hotspot";
            });
        }

        [RelayCommand]
        private async Task ToggleHotspotAsync()
        {
            if (IsRunning) await _svc.StopHotspotAsync();
            else           await _svc.StartHotspotAsync();
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
            }
            IsEditingSsid = false;
        }

        [RelayCommand]
        private void CopyPassword() => System.Windows.Clipboard.SetText(Password);

        [RelayCommand]
        private void CopySsid() => System.Windows.Clipboard.SetText(Ssid);

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
