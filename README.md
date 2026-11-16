# LanDrop 2.0.1 RC1 🚀

**Local file transfer for Windows, built with C# and WPF.**

LanDrop allows you to send anything — from tiny documents to massive 10+ GB archives — directly between Windows PCs over local WiFi, Ethernet, or Direct WiFi Hotspots. No accounts, no cloud, no internet required. Transfers go directly between peers on your local network.

---

## ✨ Current source features

* 🎨 **Complete 2026 UI/UX Redesign:** Clean Dark Slate (`#090D11`) and Warm Slate (`#F8FAFC`) design system with native frameless window controls and responsive layouts.
* ⚡ **Streaming Checksums:** A streaming 64-bit checksum detects accidental transfer corruption.
* 🗜️ **Real-Time LZ4 Block Compression:** High-speed on-the-fly compression for documents and logs with automatic bypass for pre-compressed media (`.mp4`, `.zip`, `.png`).
* 🧠 **Pooled Buffers:** `ArrayPool<byte>.Shared` recycles transfer buffers. Actual memory use depends on the application and input.
* 🗂️ **Files & Folders:** Reconstructs folder hierarchy and streams files without loading whole files into memory.
* ⏸️ **Pause, Resume & Cancel:** Pause either direction; cancellation closes the active receive session and cleans up its temporary file.
* 📶 **WiFi Direct Hotspot:** Integrated hotspot management with a random first-use password and optional password regeneration.

---

## 🚀 Quick Start

**Release status:** v2.0.1-rc.1 packages the receive-safety fixes on supported .NET 10 LTS. It remains a prerelease until transfer between two physical PCs and WiFi Direct hardware checks pass. The older v2.0.0 executable predates these fixes.

1. Download `LanDrop-2.0.1-rc.1-win-x64.zip` and its SHA-256 checksum from **[Releases](https://github.com/sachiyacodes/LanDrop/releases)**.
2. Verify the ZIP checksum, extract it, and run `LanDrop.exe` on both Windows PCs. The release is self-contained; no separate .NET installation is required. It is unsigned, so Windows may show an unknown-publisher warning. Framework-dependent source builds require the .NET 10 Desktop Runtime.
3. Ensure both devices are on the same Wi-Fi / LAN network (or use the built-in **WiFi Direct** hotspot tab).
4. Drag & drop files or folders $\rightarrow$ Click **Send**!

---

## 🔑 Core Features

* **Direct LAN Transfer** — High-throughput TCP socket streaming over Wi-Fi or Ethernet.
* **Zero Internet Required** — 100% offline; data never leaves your local network.
* **Auto Device Discovery** — Peer devices automatically detected via UDP broadcast beacons.
* **WiFi Direct (No Router)** — Start an ad-hoc local hotspot directly from the app.
* **Large File & Bulk Transfers** — Streams file contents in bounded chunks and reconstructs nested folders.
* **Transfer Verification** — A streaming checksum detects accidental corruption; it is not a cryptographic authenticity check.
* **Real-Time Speedometer** — Live MB/s speed, ETA, and transfer progress.
* **Transfer History** — Persistent session logs with timestamps, speeds, and direction badges.
* **Modern Themes** — Automatic system title bar synchronization for Dark and Light modes.

---

## Security and receive behavior

Use LanDrop only with trusted peers on a trusted LAN. File contents and the optional PIN travel in plaintext over TCP. The PIN does not provide encryption or verified peer identity. A local network observer can read traffic; do not use this version for secrets on shared or hostile networks.

The receiver allows one incoming connection at a time. Control payloads are limited to 64 KiB (PIN responses to 128 bytes), and raw and decompressed chunks to 4 MiB. Handshake reads and announced payload reads time out after 30 seconds. After acceptance, waiting for the next frame has no idle timeout, so a sender can pause and resume without disconnecting. Cancel can still close that session. Existing filenames are preserved: received duplicates use `name (1).ext`, `name (2).ext`, and so on. Cancel removes the current partial file; earlier completed files remain.

New settings use a cryptographically random hotspot password. Loading the old `11111111` default replaces and saves it; custom passwords remain unchanged. Share the displayed password only with intended peers.

## Performance evidence

Throughput and memory use depend on hardware, storage, network, file sizes, and compressibility. No reproducible hardware benchmark is committed, so this README makes no fixed speed, memory, or corruption-rate guarantee. The loopback regression checks below verify selected correctness and failure cases, not multi-gigabyte performance or hotspot hardware support.

---

## 🛠️ Build from Source

### Prerequisites
* Windows 10 / 11 (x64)
* [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Build Release Binary
```powershell
dotnet restore LanDrop\LanDrop.csproj
dotnet build LanDrop\LanDrop.csproj -c Release
```

### Publish a Self-Contained Executable
```powershell
dotnet publish LanDrop\LanDrop.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish\
```

---

## Regression checks

```powershell
dotnet run --project tests/LanDrop.Checks/LanDrop.Checks.csproj -c Release
```

The dependency-free console checks cover frame allocation limits, generated credentials, duplicate filenames, empty files, receive cancellation, decompression bounds, connection limits, idle pause/resume, and handshake/payload timeouts. The Windows Actions workflow builds the application and runs these checks. Two PCs, WiFi Direct, and GUI interactions still require manual hardware testing. The v2.0.1-rc.1 prerelease includes the current source fixes. See [release verification](docs/release-verification.md) for gates and rollback.

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
