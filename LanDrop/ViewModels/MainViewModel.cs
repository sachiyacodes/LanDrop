// ViewModels/MainViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LanDrop.Models;
using LanDrop.Networking;
using LanDrop.Services;
using Microsoft.Extensions.Logging;

namespace LanDrop.ViewModels
{
    // ── Toast notification model ──────────────────────────────────────────
    public partial class ToastViewModel : ObservableObject
    {
        public string  Title      { get; init; } = string.Empty;
        public string  Message    { get; init; } = string.Empty;
        public string  Icon       { get; init; } = "✓";
        public string  Type       { get; init; } = "success"; // success | error | info | warn
        [ObservableProperty] private double _opacity = 1.0;
    }

    public partial class MainViewModel : ObservableObject
    {
        private readonly AppSettings     _settings;
        private readonly DeviceDiscovery _discovery;
        private readonly FileReceiver    _receiver;
        private readonly ILoggerFactory  _loggerFactory;
        private readonly ILogger         _logger;

        private FileSender?              _sender;
        private CancellationTokenSource? _transferCts;

        // ── Observable properties ─────────────────────────────────────────
        [ObservableProperty] private bool   _isDarkMode;
        [ObservableProperty] private string _localDeviceName = Environment.MachineName;
        [ObservableProperty] private string _localIpAddress  = GetLocalIp();

        // Tab: 0=Send 1=Receive 2=WiFiDirect 3=History
        [ObservableProperty] private int _activeTab = 0;

        public bool IsReceiveMode    => ActiveTab == 1;
        public bool IsWifiDirectMode => ActiveTab == 2;
        public bool IsHistoryMode    => ActiveTab == 3;
        public bool IsSendMode       => ActiveTab == 0;

        partial void OnActiveTabChanged(int value)
        {
            OnPropertyChanged(nameof(IsReceiveMode));
            OnPropertyChanged(nameof(IsWifiDirectMode));
            OnPropertyChanged(nameof(IsHistoryMode));
            OnPropertyChanged(nameof(IsSendMode));
        }

        public ObservableCollection<DeviceInfo>           DiscoveredDevices { get; } = new();
        public ObservableCollection<FileEntry>            QueuedFiles       { get; } = new();
        public ObservableCollection<TransferHistoryEntry> History           { get; } = new();
        public ObservableCollection<ToastViewModel>       Toasts            { get; } = new();

        [ObservableProperty] private DeviceInfo? _selectedDevice;
        [ObservableProperty] private string       _manualIpInput   = string.Empty;
        [ObservableProperty] private TransferState _transferState  = TransferState.Idle;
        [ObservableProperty] private double        _progressPercent = 0;
        [ObservableProperty] private long          _totalBytes      = 0;
        [ObservableProperty] private long          _transferredBytes = 0;
        [ObservableProperty] private double        _speedBytesPerSec = 0;
        [ObservableProperty] private TimeSpan      _eta             = TimeSpan.Zero;
        [ObservableProperty] private string        _currentFileName = string.Empty;
        [ObservableProperty] private string        _statusMessage   = "Ready";
        [ObservableProperty] private bool          _isHashing       = false;
        [ObservableProperty] private string        _hashingStatus   = string.Empty;

        // Transfer start time for history
        private DateTime _transferStartTime;
        private string   _transferPeerName = string.Empty;

        public MainViewModel(AppSettings s, DeviceDiscovery d, FileReceiver r, ILoggerFactory lf)
        {
            _settings      = s;
            _discovery     = d;
            _receiver      = r;
            _loggerFactory = lf;
            _logger        = lf.CreateLogger<MainViewModel>();
            _isDarkMode    = s.DarkMode;

            // Load history
            foreach (var h in App.HistorySvc.Load())
                History.Add(h);

            _discovery.DeviceDiscovered += dev =>
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (!DiscoveredDevices.Any(x => x.Address.Equals(dev.Address)))
                        DiscoveredDevices.Add(dev);
                });

            _discovery.DeviceLost += dev =>
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var ex = DiscoveredDevices.FirstOrDefault(x => x.Address.Equals(dev.Address));
                    if (ex is not null) DiscoveredDevices.Remove(ex);
                });

            _receiver.IncomingTransfer  += OnIncomingTransfer;
            _receiver.Progress          += OnReceiverProgress;
            _receiver.TransferCompleted += OnTransferCompleted;
            _receiver.TransferError     += msg =>
                Application.Current.Dispatcher.Invoke(() =>
                {
                    TransferState = TransferState.Failed;
                    StatusMessage = $"Transfer error: {msg}";
                    ShowToast("Transfer Failed", msg, "✕", "error");
                });

            StatusMessage = $"Listening on port {_settings.TransferPort}";
        }

        // ── Commands ──────────────────────────────────────────────────────

        [RelayCommand] private void ToggleTheme()  { IsDarkMode = !IsDarkMode; App.ApplyTheme(IsDarkMode); }
        [RelayCommand] private void SwitchToSend() => ActiveTab = 0;
        [RelayCommand] private void SwitchToReceive() => ActiveTab = 1;
        [RelayCommand] private void ClearHistory()
        {
            History.Clear();
            App.HistorySvc.Save(new List<TransferHistoryEntry>());
        }

        [RelayCommand]
        private void AddFiles()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true };
            if (dlg.ShowDialog() == true) AddPaths(dlg.FileNames);
        }

        [RelayCommand]
        private void AddFolder()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                AddPaths(new[] { dlg.SelectedPath });
        }

        [RelayCommand] private void RemoveFile(FileEntry e) => QueuedFiles.Remove(e);
        [RelayCommand] private void ClearFiles()           => QueuedFiles.Clear();

        [RelayCommand]
        private async Task SendFilesAsync()
        {
            if (QueuedFiles.Count == 0)
            {
                ShowToast("No Files", "Add files or folders to send first", "!", "warn");
                return;
            }

            string targetIp = SelectedDevice?.Address.ToString()
                           ?? (string.IsNullOrWhiteSpace(ManualIpInput) ? null : ManualIpInput.Trim())
                           ?? string.Empty;

            if (string.IsNullOrEmpty(targetIp))
            {
                ShowToast("No Target", "Select a device or enter an IP address", "!", "warn");
                return;
            }

            var files = QueuedFiles.ToList();

            // Hash phase
            IsHashing    = true;
            TransferState = TransferState.Connecting;
            StatusMessage  = "Computing checksums…";

            try
            {
                var svc = new FileHashService(_loggerFactory.CreateLogger<FileHashService>());
                await svc.ComputeHashesAsync(files,
                    (done, total) => HashingStatus = $"Hashing {done} of {total}…");
            }
            catch (Exception ex)
            {
                IsHashing = false; TransferState = TransferState.Failed;
                ShowToast("Hash Error", ex.Message, "✕", "error");
                return;
            }
            IsHashing = false;

            // Transfer
            _transferCts     = new CancellationTokenSource();
            _sender          = new FileSender(_settings, _loggerFactory.CreateLogger<FileSender>());
            _transferStartTime = DateTime.Now;
            _transferPeerName  = SelectedDevice?.DeviceName ?? targetIp;

            TransferState     = TransferState.Transferring;
            TotalBytes        = files.Sum(f => f.SizeBytes);
            TransferredBytes  = 0;
            ProgressPercent   = 0;
            StatusMessage     = $"Connecting to {targetIp}…";

            try
            {
                await _sender.SendAsync(targetIp, _settings.TransferPort, files, null,
                    p => Application.Current.Dispatcher.Invoke(() =>
                    {
                        TransferredBytes  = p.TransferredBytes;
                        TotalBytes        = p.TotalBytes;
                        ProgressPercent   = TotalBytes > 0
                            ? Math.Min(100, TransferredBytes / (double)TotalBytes * 100) : 0;
                        SpeedBytesPerSec  = p.SpeedBytesPerSecond;
                        Eta               = SpeedBytesPerSec > 0
                            ? TimeSpan.FromSeconds((TotalBytes - TransferredBytes) / SpeedBytesPerSec)
                            : TimeSpan.Zero;
                        CurrentFileName   = Path.GetFileName(p.CurrentFileName);
                        StatusMessage     = $"Sending {CurrentFileName}…";
                    }),
                    _transferCts.Token);

                TransferState   = TransferState.Completed;
                ProgressPercent = 100;
                StatusMessage   = $"Sent {files.Count} file(s) successfully";

                // Add to history
                double elapsed = (DateTime.Now - _transferStartTime).TotalSeconds;
                double speed   = elapsed > 0 ? TotalBytes / elapsed / 1024.0 / 1024.0 : 0;
                foreach (var f in files)
                    AddHistory(new TransferHistoryEntry
                    {
                        FileName  = f.RelativePath,
                        SizeBytes = f.SizeBytes,
                        IsSent    = true,
                        Success   = true,
                        PeerName  = _transferPeerName,
                        SpeedMbps = speed
                    });

                ShowToast("Transfer Complete",
                    $"Sent {files.Count} file(s) to {_transferPeerName}",
                    "✓", "success");
            }
            catch (OperationCanceledException)
            {
                TransferState = TransferState.Cancelled;
                StatusMessage  = "Transfer cancelled";
                ShowToast("Cancelled", "Transfer was cancelled", "✕", "info");
            }
            catch (Exception ex)
            {
                TransferState = TransferState.Failed;
                StatusMessage  = $"Failed: {ex.Message}";
                ShowToast("Transfer Failed", ex.Message, "✕", "error");
                _logger.LogError(ex, "Send error");
            }
            finally { _transferCts?.Dispose(); _transferCts = null; }
        }

        [RelayCommand]
        private void PauseTransfer()
        {
            if (TransferState != TransferState.Transferring) return;
            _sender?.Pause(); _receiver.Pause();
            TransferState = TransferState.Paused;
            StatusMessage  = "Paused";
        }

        [RelayCommand]
        private void ResumeTransfer()
        {
            if (TransferState != TransferState.Paused) return;
            _sender?.Resume(); _receiver.Resume();
            TransferState = TransferState.Transferring;
            StatusMessage  = "Resumed";
        }

        [RelayCommand]
        private void CancelTransfer()
        {
            _transferCts?.Cancel();
            _sender?.Resume();
            _receiver.Resume();
            TransferState = TransferState.Cancelled;
            StatusMessage  = "Cancelled";
        }

        [RelayCommand]
        private void ResetTransfer()
        {
            TransferState    = TransferState.Idle;
            ProgressPercent  = 0;
            TransferredBytes = 0;
            TotalBytes       = 0;
            SpeedBytesPerSec = 0;
            Eta              = TimeSpan.Zero;
            CurrentFileName  = string.Empty;
            StatusMessage    = "Ready";
        }

        [RelayCommand]
        private void ChangeSaveFolder()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                SelectedPath = _settings.ReceiveSavePath
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            _settings.ReceiveSavePath = dlg.SelectedPath;
            App.SettingsSvc.Save(_settings);
            ShowToast("Save Folder Updated", dlg.SelectedPath, "✓", "success");
        }

        // ── Drag & drop ───────────────────────────────────────────────────
        public void HandleDrop(string[] paths) => AddPaths(paths);

        // ── Toast system ──────────────────────────────────────────────────
        public async void ShowToast(string title, string message, string icon, string type)
        {
            var toast = new ToastViewModel
            {
                Title   = title,
                Message = message,
                Icon    = icon,
                Type    = type
            };
            Application.Current.Dispatcher.Invoke(() => Toasts.Add(toast));
            await Task.Delay(4000);
            Application.Current.Dispatcher.Invoke(() => Toasts.Remove(toast));
        }

        // ── Receiver callbacks ────────────────────────────────────────────
        private void OnIncomingTransfer(object? s, IncomingTransferEventArgs args)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                args.Accepted      = true;
                args.SaveDirectory = _settings.ReceiveSavePath;

                TransferState    = TransferState.Transferring;
                TotalBytes       = args.Hello.TotalBytes;
                TransferredBytes = 0;
                ProgressPercent  = 0;
                _transferPeerName  = args.Hello.SenderName;
                _transferStartTime = DateTime.Now;
                ActiveTab        = 1;

                ShowToast("Incoming Transfer",
                    $"{args.Hello.SenderName} is sending {args.Hello.FileCount} file(s) · " +
                    Helpers.FormatHelper.FormatBytes(args.Hello.TotalBytes),
                    "↓", "info");

                StatusMessage = $"Receiving from {args.Hello.SenderName}…";
            });
        }

        private void OnReceiverProgress(TransferProgress p)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                TransferredBytes  = p.TransferredBytes;
                TotalBytes        = p.TotalBytes;
                ProgressPercent   = TotalBytes > 0
                    ? Math.Min(100, TransferredBytes / (double)TotalBytes * 100) : 0;
                SpeedBytesPerSec  = p.SpeedBytesPerSecond;
                Eta               = SpeedBytesPerSec > 0
                    ? TimeSpan.FromSeconds((TotalBytes - TransferredBytes) / SpeedBytesPerSec)
                    : TimeSpan.Zero;
                CurrentFileName   = Path.GetFileName(p.CurrentFileName);
            });
        }

        private void OnTransferCompleted(string path, bool ok)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (ok)
                {
                    StatusMessage = $"Received: {Path.GetFileName(path)}";
                    double elapsed = (DateTime.Now - _transferStartTime).TotalSeconds;
                    double speed   = elapsed > 0 ? TotalBytes / elapsed / 1024.0 / 1024.0 : 0;

                    AddHistory(new TransferHistoryEntry
                    {
                        FileName  = Path.GetFileName(path),
                        SizeBytes = TotalBytes,
                        IsSent    = false,
                        Success   = true,
                        PeerName  = _transferPeerName,
                        SpeedMbps = speed
                    });

                    ShowToast("File Received",
                        $"{Path.GetFileName(path)} from {_transferPeerName}",
                        "↓", "success");

                    TransferState = TransferState.Completed;
                }
                else
                {
                    TransferState = TransferState.Failed;
                    ShowToast("Transfer Failed", "Checksum mismatch — file may be corrupted", "✕", "error");
                }
            });
        }

        // ── Helpers ───────────────────────────────────────────────────────
        private void AddHistory(TransferHistoryEntry e)
        {
            History.Insert(0, e);
            if (History.Count > 200) History.RemoveAt(History.Count - 1);
            App.HistorySvc.Append(e);
        }

        private void AddPaths(IEnumerable<string> paths)
        {
            var entries = FileCollectionService.Collect(paths);
            foreach (var e in entries)
                if (!QueuedFiles.Any(x => x.FullPath == e.FullPath))
                    QueuedFiles.Add(e);
        }

        private static string GetLocalIp() =>
            Networking.NetworkHelper.GetPreferredLocalIP();
    }
}
