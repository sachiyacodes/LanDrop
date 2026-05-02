# LanDrop

**Fast, private, zero-internet file transfers for Windows.**

LanDrop lets you send any file — from a single photo to a 10 GB video — directly between two Windows PCs over your local WiFi or Ethernet. No accounts, no cloud, no internet required. Everything stays on your network.

---

## Features

| Feature | Details |
|---|---|
| **LAN Transfer** | Direct TCP over WiFi or Ethernet — no router relay |
| **Auto Discovery** | Finds other LanDrop devices via UDP broadcast automatically |
| **WiFi Direct** | Creates a hotspot so two PCs can connect without any router |
| **Transfer History** | Full log of every sent and received file with speed and timestamp |
| **SHA-256 Verification** | Every file is checksummed — receiver confirms integrity before saving |
| **Pause / Resume / Cancel** | Full transfer control at any time |
| **Live Stats** | Real-time MB/s speed, ETA, and progress bar |
| **Toast Notifications** | Non-blocking slide-in notifications for all transfer events |
| **Custom Theme** | Deep dark teal palette + clean white light mode, seamless title bar |
| **Large File Support** | 4 MB chunk size + 4 MB socket buffers — tested to 10+ GB |
| **No Internet** | Works entirely offline — UDP and TCP over LAN only |
| **Single EXE** | Published as a self-contained single file — no installer, no dependencies |

---

## Color Palette

| Role | Hex | Usage |
|---|---|---|
| Background | `#040606` | App background (dark) |
| Surface | `#0a0f0f` | Cards, panels |
| Primary | `#50adaf` | Buttons, progress bar, accents |
| Secondary | `#206465` | Hover states, subtle backgrounds |
| Accent | `#22a8aa` | Active states, badges |
| Text | `#eceeee` | Primary text |

---

## Requirements

| Item | Requirement |
|---|---|
| OS | Windows 10 / 11 (x64) |
| .NET SDK | 8.0 (for building only) |
| Runtime | Self-contained — no runtime needed on target machines |
| Firewall ports | TCP 55001 (transfer) · UDP 55002 (discovery) |

---

## Quick Start

### 1 — Install .NET 8 SDK

Download from **https://dotnet.microsoft.com/download/dotnet/8.0**

Choose: **SDK 8.0 → Windows → x64 Installer**

Verify:
```cmd
dotnet --version
```

---

### 2 — Build

```cmd
cd C:\path\to\LanDrop
dotnet restore LanDrop\LanDrop.csproj
dotnet build LanDrop\LanDrop.csproj -c Release
```

---

### 3 — Publish as single EXE

```cmd
dotnet publish LanDrop\LanDrop.csproj ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -o publish\
```

Output: `publish\LanDrop.exe`

Copy this single file to any Windows 10/11 x64 machine and run — no installation needed.

---

### 4 — Open Firewall (run once as Administrator)

```cmd
netsh advfirewall firewall add rule name="LanDrop TCP" protocol=TCP dir=in localport=55001 action=allow
netsh advfirewall firewall add rule name="LanDrop UDP" protocol=UDP dir=in localport=55002 action=allow
```

---

## How to Use

### Sending Files

1. Open LanDrop on **both PCs**
2. On the **Send** tab, wait for the other device to appear under **Nearby Devices** (~3 seconds)
3. Click the device to select it — or type its IP in the **Manual IP** box
4. Drag and drop files/folders onto the drop zone — or click **+ Add Files** / **+ Add Folder**
5. Click **Send Files →**
6. The receiver gets a toast notification and the file saves automatically

---

### Receiving Files

1. Switch to the **Receive** tab
2. The green **Listening** badge confirms the app is ready
3. Change the save folder via the **Change** button if needed
4. When a sender initiates, a toast notification appears and the file is received automatically

---

### WiFi Direct — No Router Needed

Use this when the two PCs are on **different WiFi networks** or there is no router at all.

1. On **PC A** — click the **⊕ WiFi Direct** tab
2. Set your network name and password (default password: `11111111`)
3. Click **Start Hotspot**
4. On **PC B** — open Windows WiFi settings → connect to the network shown in LanDrop
5. Open LanDrop on PC B — both devices discover each other automatically
6. Transfer files normally via the Send tab

> **Note:** WiFi Direct requires Administrator privileges.
> Right-click `LanDrop.exe` → **Run as Administrator** if the hotspot fails to start.

---

## Transfer Controls

| Button | Action |
|---|---|
| **Pause** | Freezes the transfer — connection stays open |
| **Resume** | Continues from exactly where it paused |
| **Cancel** | Stops immediately — partial files are deleted |
| **Dismiss** | Clears the completed / failed result banner |

---

## Settings

Saved automatically to `%AppData%\LanDrop\settings.json`:

```json
{
  "transferPort":     55001,
  "discoveryPort":    55002,
  "receiveSavePath":  "C:\\Users\\You\\Downloads\\LanDrop",
  "socketBufferSize": 4194304,
  "chunkSize":        4194304,
  "darkMode":         true,
  "autoAccept":       false,
  "wifiDirectSsid":   "LanDrop-Direct",
  "wifiDirectPass":   "11111111"
}
```

| Key | Default | Description |
|---|---|---|
| `transferPort` | 55001 | TCP port for file data |
| `discoveryPort` | 55002 | UDP port for device discovery |
| `receiveSavePath` | `Downloads\LanDrop` | Where received files are saved |
| `socketBufferSize` | 4194304 (4 MB) | TCP socket buffer size |
| `chunkSize` | 4194304 (4 MB) | File read/write chunk size |
| `darkMode` | true | Theme — saved automatically on toggle |
| `wifiDirectSsid` | LanDrop-Direct | WiFi Direct network name |
| `wifiDirectPass` | 11111111 | WiFi Direct password (min 8 characters) |

---

## Project Structure

```
LanDrop/
├── App.xaml / App.xaml.cs           Entry point, service wiring, theme switching
├── App.cs                           App version constant
├── GlobalUsings.cs                  Resolves WPF vs WinForms type ambiguities
│
├── Models/
│   ├── AppSettings.cs               User configuration model
│   ├── DeviceInfo.cs                Discovered peer descriptor
│   ├── TransferSession.cs           Transfer state and file entries
│   └── TransferHistoryEntry.cs      History log entry model
│
├── Networking/
│   ├── Protocol.cs                  Wire format, message types, frame helpers
│   ├── StreamExtensions.cs          Async framed message read/write
│   ├── DeviceDiscovery.cs           UDP broadcast peer discovery
│   ├── FileSender.cs                TCP client — connect, hash, stream chunks
│   ├── FileReceiver.cs              TCP server — accept, verify, save
│   └── NetworkHelper.cs             Local IP utilities
│
├── Services/
│   ├── FileHashService.cs           SHA-256 async computation
│   ├── FileCollectionService.cs     Expand paths to flat FileEntry list
│   ├── SettingsService.cs           JSON settings persistence
│   ├── TransferHistoryService.cs    History log read/write
│   └── WiFiDirectService.cs         Windows hosted network + Mobile Hotspot API
│
├── ViewModels/
│   ├── MainViewModel.cs             Root MVVM ViewModel, all commands + toasts
│   └── WiFiDirectViewModel.cs       Hotspot panel observable state
│
├── Views/
│   ├── MainWindow.xaml              Full WPF UI — Send, Receive, WiFi Direct, History
│   ├── MainWindow.xaml.cs           Drag & drop, window chrome, WiFi Direct handlers
│   ├── IncomingTransferDialog.cs    Accept/reject dialog
│   └── SettingsWindow.cs            Port and path settings dialog
│
├── Converters/
│   ├── Converters.cs                Bytes, speed, ETA, state, visibility converters
│   ├── InverseBoolConverter.cs      bool ↔ !bool two-way converter
│   └── ProgressWidthConverter.cs    Percent × container width → pixel fill
│
├── Helpers/
│   ├── FormatHelper.cs              Human-readable bytes, speed, and ETA strings
│   ├── SerilogExtensions.cs         ILoggerFactory.AddSerilog() bridge
│   └── WindowHelper.cs              DWM API — matches title bar to app background
│
└── Themes/
    ├── DarkTheme.xaml               Deep dark teal — #040606 base
    └── LightTheme.xaml              Clean white with teal accents
```

---

## Wire Protocol

Every TCP message uses a fixed 5-byte frame header:

```
[1 byte: message type] [4 bytes: payload length, big-endian] [N bytes: payload]
```

### Transfer sequence

```
Sender                              Receiver
  │── Hello (JSON) ────────────────▶│
  │◀── HelloAck (JSON) ─────────────│
  │                                 │
  │── FileHeader (JSON) ────────────▶│   ← per file
  │── DataChunk × N (raw bytes) ───▶│
  │── FileDone (JSON) ──────────────▶│
  │◀── ChecksumAck (JSON) ──────────│
  │                                 │
  │── SessionDone (JSON) ───────────▶│
```

### Message type reference

| Hex | Name | Direction | Payload |
|---|---|---|---|
| `0x01` | Hello | Sender → Receiver | JSON |
| `0x02` | HelloAck | Receiver → Sender | JSON |
| `0x10` | FileHeader | Sender → Receiver | JSON |
| `0x20` | DataChunk | Sender → Receiver | Raw bytes |
| `0x30` | Pause | Either | Empty |
| `0x31` | Resume | Either | Empty |
| `0x32` | Cancel | Either | Empty |
| `0x33` | FileDone | Sender → Receiver | JSON |
| `0x34` | SessionDone | Sender → Receiver | JSON |
| `0x41` | ChecksumAck | Receiver → Sender | JSON |
| `0xFF` | Error | Either | JSON |

---

## Performance

| Network | Expected Speed |
|---|---|
| Gigabit Ethernet | 80 – 110 MB/s |
| 5 GHz WiFi (802.11ac) | 30 – 80 MB/s |
| WiFi Direct (hotspot) | 20 – 50 MB/s |
| 2.4 GHz WiFi | 5 – 20 MB/s |

At 50 MB/s a **1.7 GB video** transfers in about **34 seconds**.

Both chunk size and socket buffer default to **4 MB**, optimized for LAN throughput.
You can increase both in `settings.json` for even faster Gigabit Ethernet transfers.

---

## Logs

```
%AppData%\LanDrop\logs\landrop_YYYYMMDD.log
```

Example:
```
10:32:01 [INF] Discovery started on UDP port 55002
10:32:04 [INF] Device discovered: KALINDU-TUF @ 192.168.16.242
10:32:10 [INF] Incoming connection from 192.168.16.242
10:32:10 [INF] Receiving file [0] movie.mkv (1825361920 bytes)
10:32:44 [INF] File saved: C:\Users\...\movie.mkv (hash OK)
```

---

## Troubleshooting

| Problem | Solution |
|---|---|
| Device not discovered | Confirm both PCs are on the same subnet. Use Manual IP as fallback. |
| "Connection refused" error | Run the `netsh` firewall commands above as Administrator. |
| Hotspot won't start | Right-click `LanDrop.exe` → **Run as Administrator**. |
| Hotspot not visible on other PC | Go to **Windows Settings → Network → Mobile Hotspot** and verify it turns on manually. Then try again in LanDrop. |
| Transfer fails with hash mismatch | Retry the transfer — usually a transient network glitch. |
| Title bar not matching app color | The DWM title bar color API requires Windows 11 22H1 or later. On Windows 10 the bar may remain the default system color. |
| App crashes on start | Open `%AppData%\LanDrop\logs\` and check the latest log file for the full error. |

---

## License

MIT — free to use, modify, and distribute.

---

*Built with C# · WPF · .NET 8 · CommunityToolkit.Mvvm · Serilog*
