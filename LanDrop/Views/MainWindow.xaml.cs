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
        private MainViewModel VM => (MainViewModel)DataContext;

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
            else if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                try
                {
                    if (WindowState != WindowState.Maximized)
                    {
                        DragMove();
                    }
                }
                catch (InvalidOperationException)
                {
                    // Ignore if called while in an invalid window state
                }
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
            if (paths?.Length > 0)
            {
                VM.HandleDrop(paths);
                VM.ActiveTab = 0; // switch to send tab
            }
        }
    }
}
