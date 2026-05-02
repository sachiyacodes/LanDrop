// App.xaml.cs
using System;
using System.Windows;
using LanDrop.Models;
using LanDrop.Networking;
using LanDrop.Services;
using LanDrop.ViewModels;
using Microsoft.Extensions.Logging;
using Serilog;

namespace LanDrop
{
    public partial class App : Application
    {
        public static AppSettings             Settings        { get; private set; } = new();
        public static SettingsService         SettingsSvc     { get; private set; } = null!;
        public static ILoggerFactory          LoggerFactory   { get; private set; } = null!;
        public static DeviceDiscovery         Discovery       { get; private set; } = null!;
        public static FileReceiver            Receiver        { get; private set; } = null!;
        public static MainViewModel           MainVM          { get; private set; } = null!;
        public static WiFiDirectService       WifiDirect      { get; private set; } = null!;
        public static WiFiDirectViewModel     WifiDirectVM    { get; private set; } = null!;
        public static TransferHistoryService  HistorySvc      { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Logging
            var logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LanDrop", "logs", "landrop_.log");
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();
            LoggerFactory = new LoggerFactory().AddSerilog(Log.Logger);

            // Settings
            SettingsSvc = new SettingsService(LoggerFactory.CreateLogger<SettingsService>());
            Settings    = SettingsSvc.Load();
            System.IO.Directory.CreateDirectory(Settings.ReceiveSavePath);

            // Theme
            ApplyTheme(Settings.DarkMode);

            // Services
            HistorySvc  = new TransferHistoryService();
            Discovery   = new DeviceDiscovery(Settings, LoggerFactory.CreateLogger<DeviceDiscovery>());
            Receiver    = new FileReceiver(Settings, LoggerFactory.CreateLogger<FileReceiver>());
            WifiDirect  = new WiFiDirectService(Settings, LoggerFactory.CreateLogger<WiFiDirectService>());
            WifiDirectVM = new WiFiDirectViewModel(WifiDirect, LoggerFactory.CreateLogger<WiFiDirectViewModel>());

            MainVM = new MainViewModel(Settings, Discovery, Receiver, LoggerFactory);

            Discovery.Start();
            Receiver.StartListening();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _ = WifiDirect.StopHotspotAsync();
            Discovery.Stop();
            Receiver.StopListening();
            SettingsSvc.Save(Settings);
            Log.CloseAndFlush();
            base.OnExit(e);
        }

        public static void ApplyTheme(bool dark)
        {
            Settings.DarkMode = dark;
            var dict = new ResourceDictionary
            {
                Source = new Uri(dark ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml",
                                 UriKind.Relative)
            };
            Current.Resources.MergedDictionaries.Clear();
            Current.Resources.MergedDictionaries.Add(dict);
        }
    }
}
