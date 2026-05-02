// Views/MainWindow.xaml.cs
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LanDrop.Services;
using LanDrop.Helpers;
using LanDrop.ViewModels;
using WpfDataFormats = System.Windows.DataFormats;

namespace LanDrop.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel       VM  => (MainViewModel)DataContext;
        private WiFiDirectViewModel WVM => App.WifiDirectVM;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = App.MainVM;

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Apply DWM title bar color to eliminate the white bar
            WindowHelper.ApplyTitleBarColor(this, App.Settings.DarkMode);

            // Re-apply on theme toggle
            App.MainVM.PropertyChanged += (s, pe) =>
            {
                if (pe.PropertyName == nameof(ViewModels.MainViewModel.IsDarkMode))
                    WindowHelper.ApplyTitleBarColor(this, App.MainVM.IsDarkMode);
            };

            // Set WiFi Direct grid DataContext
            if (WifiDirectGrid != null)
                WifiDirectGrid.DataContext = App.WifiDirectVM;

            // Populate SSID / Password textboxes from settings
            if (SsidBox != null) SsidBox.Text = App.WifiDirect.Ssid;
            if (PassBox != null) PassBox.Text  = App.WifiDirect.Password;

            // Listen for hotspot state changes to update button + status manually
            App.WifiDirect.StateChanged += OnHotspotStateChanged;
            App.WifiDirect.IpAssigned   += OnHotspotIpAssigned;
            App.WifiDirect.ErrorOccurred += OnHotspotError;
        }

        // ── Window chrome handlers ────────────────────────────────────────

        private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Double-click header = maximize/restore
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState.Minimized;

        private void BtnMaximize_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal : WindowState.Maximized;

        private void BtnClose_Click(object sender, RoutedEventArgs e) =>
            Close();

        // ── Drag & Drop ───────────────────────────────────────────────────

        private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(WpfDataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(WpfDataFormats.FileDrop)) return;
            var paths = (string[])e.Data.GetData(WpfDataFormats.FileDrop);
            if (paths?.Length > 0 && VM.IsSendMode)
            {
                VM.HandleDrop(paths);
                VM.ActiveTab = 0; // switch to send tab
            }
        }

        // ── WiFi Direct handlers ──────────────────────────────────────────

        private async void HotspotToggle_Click(object sender, RoutedEventArgs e)
        {
            if (App.WifiDirect == null) return;

            // Save any edits to SSID/Password before starting
            if (SsidBox != null && !string.IsNullOrWhiteSpace(SsidBox.Text))
                App.WifiDirect.Ssid = SsidBox.Text.Trim();
            if (PassBox != null && PassBox.Text.Length >= 8)
                App.WifiDirect.Password = PassBox.Text.Trim();

            if (App.WifiDirectVM.IsRunning)
            {
                UpdateHotspotBtn("Stopping…", "#F59E0B");
                await App.WifiDirect.StopHotspotAsync();
            }
            else
            {
                UpdateHotspotBtn("Starting…", "#F59E0B");
                bool ok = await App.WifiDirect.StartHotspotAsync();

                if (!ok)
                {
                    // Show helpful instructions
                    App.MainVM.ShowToast(
                        "Hotspot Failed",
                        "Run LanDrop as Administrator and try again",
                        "✕", "error");
                }
            }
        }

        private void CopyPassword_Click(object sender, RoutedEventArgs e)
        {
            var pwd = PassBox?.Text ?? App.WifiDirect.Password;
            System.Windows.Clipboard.SetText(pwd);
            App.MainVM.ShowToast("Copied", $"Password: {pwd}", "⧉", "info");
        }

        private void CopySsid_Click(object sender, RoutedEventArgs e)
        {
            var ssid = SsidBox?.Text ?? App.WifiDirect.Ssid;
            System.Windows.Clipboard.SetText(ssid);
            App.MainVM.ShowToast("Copied", $"Network: {ssid}", "⧉", "info");
        }

        // ── Hotspot state → UI updates ────────────────────────────────────

        private void OnHotspotStateChanged(HotspotState state)
        {
            Dispatcher.Invoke(() =>
            {
                switch (state)
                {
                    case HotspotState.Running:
                        UpdateHotspotBtn("Stop Hotspot", "#EF4444");
                        UpdateHotspotStatus(
                            $"Running  ·  Network: {App.WifiDirect.Ssid}",
                            "#3FB950");
                        App.MainVM.ShowToast(
                            "Hotspot Running",
                            $"Network: {App.WifiDirect.Ssid}  ·  Password: {App.WifiDirect.Password}",
                            "⊕", "success");
                        break;

                    case HotspotState.Stopped:
                        UpdateHotspotBtn("Start Hotspot", "#3FB950");
                        UpdateHotspotStatus("Off — click Start to create a direct connection", "#484F58");
                        break;

                    case HotspotState.Failed:
                        UpdateHotspotBtn("Start Hotspot", "#3FB950");
                        UpdateHotspotStatus("Failed — run as Administrator and try again", "#F85149");
                        break;

                    case HotspotState.Starting:
                        UpdateHotspotBtn("Starting…", "#F59E0B");
                        UpdateHotspotStatus("Creating hotspot…", "#D29922");
                        break;

                    case HotspotState.Stopping:
                        UpdateHotspotBtn("Stopping…", "#F59E0B");
                        UpdateHotspotStatus("Stopping…", "#D29922");
                        break;
                }
            });
        }

        private void OnHotspotIpAssigned(string ip)
        {
            Dispatcher.Invoke(() =>
                UpdateHotspotStatus(
                    $"Running  ·  IP: {ip}  ·  Network: {App.WifiDirect.Ssid}",
                    "#3FB950"));
        }

        private void OnHotspotError(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateHotspotBtn("Start Hotspot", "#3FB950");
                UpdateHotspotStatus($"Error: {msg}", "#F85149");
            });
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private void UpdateHotspotBtn(string label, string hexColor)
        {
            if (HotspotToggleBtn == null) return;
            HotspotToggleBtn.Background = HexBrush(hexColor);

            // Update the label TextBlock inside the template
            HotspotToggleBtn.ApplyTemplate();
            var tb = HotspotToggleBtn.Template?.FindName("BtnLabel", HotspotToggleBtn) as TextBlock;
            if (tb != null) tb.Text = label;
        }

        private void UpdateHotspotStatus(string text, string dotHexColor)
        {
            if (HotspotStatusText != null)
                HotspotStatusText.Text = text;
            if (HotspotStatusDot != null)
                HotspotStatusDot.Fill = HexBrush(dotHexColor);
        }

        private static SolidColorBrush HexBrush(string hex)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(color);
        }
    }
}
