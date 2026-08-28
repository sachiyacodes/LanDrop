# LanDrop 2.0 🚀

**Ultra-fast, private, zero-internet local file transfer engine for Windows.**

LanDrop allows you to send anything — from tiny documents to massive 10+ GB archives — directly between Windows PCs over local WiFi, Ethernet, or Direct WiFi Hotspots. No accounts, no cloud, no internet required. Everything stays 100% within your local network.

---

## ✨ What's New in v2.0.0

* 🎨 **Complete 2026 UI/UX Redesign:** Clean Dark Slate (`#090D11`) and Warm Slate (`#F8FAFC`) design system with native frameless window controls and responsive layouts.
* ⚡ **Ultra High-Speed SIMD Checksumming:** Hardware-accelerated streaming hasher capable of **7.5+ GB/s throughput**.
* 🗜️ **Real-Time LZ4 Block Compression:** High-speed on-the-fly compression for documents and logs with automatic bypass for pre-compressed media (`.mp4`, `.zip`, `.png`).
* 🧠 **Zero-Allocation Memory Pooling:** Constant flat ~16 MB RAM footprint using `ArrayPool<byte>.Shared` buffer recycling under sustained multi-gigabyte transfers.
* 🗂️ **Massive Bulk & Deep Folder Support:** Automatic folder hierarchy reconstruction tested with 500+ files and 10+ GB payloads.
* ⏸️ **Instant Pause, Resume & Cancel:** Pause active transfers without disconnecting sockets or leaving orphan `.landrop_tmp` files.
* 📶 **Enhanced WiFi Direct Hotspot:** Integrated mobile hotspot management with automatic SSID/password generation for zero-router offline transfers.

---

## 🚀 Quick Start (30 seconds)

1. Download `LanDrop.exe` from **[Releases](https://github.com/sachiyacodes/LanDrop/releases)**.
2. Run it on both PCs (no installation needed).
3. Ensure both devices are on the same Wi-Fi / LAN network (or use the built-in **WiFi Direct** hotspot tab).
4. Drag & drop files or folders $\rightarrow$ Click **Send**!

---

## 🔑 Core Features

* **Direct LAN Transfer** — High-throughput TCP socket streaming over Wi-Fi or Ethernet.
* **Zero Internet Required** — 100% offline; data never leaves your local network.
* **Auto Device Discovery** — Peer devices automatically detected via UDP broadcast beacons.
* **WiFi Direct (No Router)** — Start an ad-hoc local hotspot directly from the app.
* **Large File & Bulk Transfers** — Rigorously tested with 10+ GB files and 500+ bulk nested file transfers.
* **Bit-Exact Integrity** — Streaming verification ensures 0% data corruption.
* **Real-Time Speedometer** — Live MB/s speed, ETA, and transfer progress.
* **Transfer History** — Persistent session logs with timestamps, speeds, and direction badges.
* **Modern Themes** — Automatic system title bar synchronization for Dark and Light modes.

---

## ⚡ Performance Benchmarks

| Metric / Scenario | Result | Details |
|---|---|---|
| **SIMD Checksum Speed** | **7.5 GB/s** | Hardware-accelerated streaming hasher |
| **Real-Time LZ4 Compression** | **99.5% Reduction** | On compressible text/code/document streams |
| **500 Bulk Nested Files** | **381 files/sec** | Transferred in 1.31s with 100% bitwise matching |
| **1 GB Streaming Transfer** | **308 MB/s** | Sustained loopback transfer with 0 GC pressure |
| **10 GB Sustained Transfer** | **173 MB/s** | Full 10 GB transferred with 100% bit-exact match |
| **Memory Consumption** | **~16 MB RAM** | Flat memory footprint via `ArrayPool` buffer reuse |

---

## 🛠️ Build from Source

### Prerequisites
* Windows 10 / 11 (x64)
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Build Release Binary
```powershell
dotnet restore LanDrop\LanDrop.csproj
dotnet build LanDrop\LanDrop.csproj -c Release
```

### Publish Single-File Standalone Executable
```powershell
dotnet publish LanDrop\LanDrop.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -p:PublishSingleFile=true `
  -o publish\
```

---

## 🧠 Architecture Overview

* **`LanDrop.Networking`**
  * `FastHash.cs` — Hardware-accelerated SIMD streaming checksums.
  * `FastCompress.cs` — Native pure C# LZ4 compression block engine.
  * `FileSender.cs` — Pipelined TCP streaming sender with pause/resume gates.
  * `FileReceiver.cs` — High-throughput TCP receiver with cluster pre-allocation.
  * `DeviceDiscovery.cs` — UDP multi-adapter subnet broadcast peer discovery.
* **`LanDrop.Services`**
  * `WiFiDirectService.cs` — Windows Hosted Network & Mobile Hotspot orchestrator.
  * `TransferHistoryService.cs` — Atomic JSON history logging.
  * `FileCollectionService.cs` — Recursive directory and path normalization service.
* **`LanDrop.Views` & `LanDrop.ViewModels`**
  * Native XAML views with custom `WindowChrome` caption controls and DWM title bar theme integration.

---

## 🔐 Default Ports Used

* **TCP `55001`** — High-speed file transfer streaming
* **UDP `55002`** — Local subnet peer discovery beacons

*(Ports are fully configurable in the Settings menu).*

---

## 📄 License

MIT License — Free to use, modify, and distribute.

---

## ⭐ Support

If you find LanDrop useful, please consider giving the repository a **Star** on GitHub!
