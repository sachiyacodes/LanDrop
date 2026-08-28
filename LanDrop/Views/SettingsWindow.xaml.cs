using System;
using System.IO;
using System.Windows;
using LanDrop.Helpers;
using LanDrop.Models;

namespace LanDrop.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;

        public SettingsWindow(AppSettings settings)
        {
            _settings = settings;
            InitializeComponent();

            TransferPortBox.Text  = _settings.TransferPort.ToString();
            DiscoveryPortBox.Text = _settings.DiscoveryPort.ToString();
            SavePathBox.Text      = _settings.ReceiveSavePath;
            AutoAcceptBox.IsChecked = _settings.AutoAccept;

            Loaded += (_, _) =>
            {
                WindowHelper.ApplyTitleBarColor(this, App.Settings?.DarkMode ?? false);
            };
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                InitialDirectory = SavePathBox.Text.Trim(),
                Title = "Select Default Save Folder"
            };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
            {
                SavePathBox.Text = dlg.FolderName;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TransferPortBox.Text.Trim(), out int tp) && tp is >= 1024 and <= 65535)
                _settings.TransferPort = tp;
            else
            {
                MessageBox.Show("Transfer Port must be a valid port number between 1024 and 65535.", "Invalid Port", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (int.TryParse(DiscoveryPortBox.Text.Trim(), out int dp) && dp is >= 1024 and <= 65535)
                _settings.DiscoveryPort = dp;
            else
            {
                MessageBox.Show("Discovery Port must be a valid port number between 1024 and 65535.", "Invalid Port", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string savePath = SavePathBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(savePath))
            {
                try
                {
                    Directory.CreateDirectory(savePath);
                    _settings.ReceiveSavePath = savePath;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not use save folder: {ex.Message}", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            _settings.AutoAccept = AutoAcceptBox.IsChecked == true;
            App.SettingsSvc.Save(_settings);
            DialogResult = true;
            Close();
        }
    }
}
