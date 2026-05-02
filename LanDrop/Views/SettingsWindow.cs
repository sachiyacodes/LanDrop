// Views/SettingsWindow.cs
// Lightweight settings dialog — fully code-behind, no XAML.

using System.Windows;
using System.Windows.Controls;
using LanDrop.Models;

// Explicit aliases to avoid WPF vs WinForms ambiguity
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfHAlign     = System.Windows.HorizontalAlignment;

namespace LanDrop.Views
{
    public class SettingsWindow : Window
    {
        private readonly AppSettings _settings;

        private System.Windows.Controls.TextBox  _transferPortBox  = null!;
        private System.Windows.Controls.TextBox  _discoveryPortBox = null!;
        private System.Windows.Controls.TextBox  _savePathBox      = null!;
        private System.Windows.Controls.CheckBox _autoAcceptBox    = null!;

        public SettingsWindow(AppSettings settings)
        {
            _settings             = settings;
            Title                 = "LanDrop — Settings";
            Width                 = 440;
            Height                = 300;
            ResizeMode            = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            BuildUI();
        }

        private void BuildUI()
        {
            var grid = new Grid { Margin = new Thickness(24, 20, 24, 20) };

            for (int i = 0; i < 6; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int row = 0;
            _transferPortBox  = new System.Windows.Controls.TextBox { Text = _settings.TransferPort.ToString() };
            _discoveryPortBox = new System.Windows.Controls.TextBox { Text = _settings.DiscoveryPort.ToString() };
            _savePathBox      = new System.Windows.Controls.TextBox { Text = _settings.ReceiveSavePath };
            _autoAcceptBox    = new System.Windows.Controls.CheckBox
            {
                IsChecked         = _settings.AutoAccept,
                VerticalAlignment = VerticalAlignment.Center
            };

            AddRow(grid, row++, "Transfer Port:",    _transferPortBox);
            AddRow(grid, row++, "Discovery Port:",   _discoveryPortBox);
            AddRow(grid, row++, "Default Save Path:", _savePathBox);
            AddRow(grid, row++, "Auto-accept:",      _autoAcceptBox);

            // Button row — right-aligned via DockPanel
            var btnDock = new DockPanel
            {
                LastChildFill = false,
                Margin        = new Thickness(0, 16, 0, 0)
            };
            Grid.SetRow(btnDock, 7);
            Grid.SetColumnSpan(btnDock, 2);

            var saveBtn = new System.Windows.Controls.Button
            {
                Content   = "Save",
                Padding   = new Thickness(20, 7, 20, 7),
                IsDefault = true,
                Margin    = new Thickness(8, 0, 0, 0)
            };
            saveBtn.Click += Save_Click;

            var cancelBtn = new System.Windows.Controls.Button
            {
                Content  = "Cancel",
                Padding  = new Thickness(14, 7, 14, 7),
                IsCancel = true
            };

            DockPanel.SetDock(saveBtn,   Dock.Right);
            DockPanel.SetDock(cancelBtn, Dock.Right);

            btnDock.Children.Add(saveBtn);
            btnDock.Children.Add(cancelBtn);
            grid.Children.Add(btnDock);

            Content = grid;
        }

        private static void AddRow(Grid grid, int row, string label, UIElement control)
        {
            var lbl = new TextBlock
            {
                Text              = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 12, 8)
            };
            Grid.SetRow(lbl, row);
            Grid.SetColumn(lbl, 0);
            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);

            if (control is System.Windows.Controls.TextBox tb)
                tb.Margin = new Thickness(0, 0, 0, 8);

            grid.Children.Add(lbl);
            grid.Children.Add(control);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(_transferPortBox.Text,  out int tp)) _settings.TransferPort  = tp;
            if (int.TryParse(_discoveryPortBox.Text, out int dp)) _settings.DiscoveryPort = dp;
            _settings.ReceiveSavePath = _savePathBox.Text.Trim();
            _settings.AutoAccept      = _autoAcceptBox.IsChecked == true;
            App.SettingsSvc.Save(_settings);
            DialogResult = true;
        }
    }
}
