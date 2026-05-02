# LanDrop

**Fast, private, zero-internet file transfers for Windows.**

LanDrop lets you send anything — from a single photo to multi-gigabyte files — directly between two Windows PCs over local WiFi or Ethernet.
No accounts. No cloud. No internet. Everything stays on your network.

---

## 🚀 Quick Start (30 seconds)

1. Download `LanDrop.exe` from **Releases**
2. Run it on both PCs
3. Make sure both devices are on the same network
4. Drag & drop files → done

---

## ✨ Why LanDrop?

Most file sharing tools rely on:

* Internet connectivity
* Cloud uploads
* User accounts

LanDrop removes all of that.

* ⚡ Direct LAN transfer → faster speeds
* 🔒 Fully local → no data leaves your network
* 🧩 Simple → no setup, no installation

---

## 📸 Demo

<img width="1224" height="851" alt="image" src="https://github.com/user-attachments/assets/8aa6d50b-d7a7-4d7c-ac74-22d2d90b654a" />
<img width="1223" height="841" alt="image" src="https://github.com/user-attachments/assets/59b73f31-4688-4643-bbf3-6d38f59ac0f1" />
<img width="1220" height="840" alt="image" src="https://github.com/user-attachments/assets/413b6b64-d34c-441d-904b-0f3309ad994b" />
<img width="1217" height="850" alt="image" src="https://github.com/user-attachments/assets/e999a3ec-a911-465d-b2d4-96193a0a495a" />




---

## 🔑 Features

* **LAN Transfer** — Direct TCP over WiFi or Ethernet
* **Auto Discovery** — Devices found automatically via UDP broadcast
* **WiFi Direct** — Connect two PCs without a router
* **Large File Support** — Tested with 10+ GB transfers
* **Pause / Resume / Cancel** — Full control during transfers
* **SHA-256 Verification** — Ensures file integrity
* **Live Stats** — Real-time speed, ETA, and progress
* **Transfer History** — Logs all transfers with timestamps
* **Toast Notifications** — Non-intrusive alerts
* **Custom Theme** — Dark & light modes
* **Single EXE** — No installation required

---

## ⚙️ Requirements

* Windows 10 / 11 (x64)
* No runtime required (self-contained build)

For development only:

* .NET 8 SDK

---

## 📦 Download

👉 Get the latest version from **Releases**


---

## 🛠️ Build from Source

```bash
dotnet restore LanDrop\LanDrop.csproj
dotnet build LanDrop\LanDrop.csproj -c Release
```

### Publish as Single EXE

```bash
dotnet publish LanDrop\LanDrop.csproj ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -o publish\
```

---

## 🔥 How It Works (Simple)

* Devices discover each other using **UDP broadcast**
* File transfers happen over **TCP connections**
* Files are split into chunks and streamed efficiently
* Each file is verified using **SHA-256 checksums**

---

## 📡 How to Use

### Sending Files

1. Open LanDrop on both PCs
2. Go to **Send tab**
3. Select a device (or enter IP manually)
4. Drag & drop files
5. Click **Send**

### Receiving Files

* Open **Receive tab**
* Files are accepted automatically (or configurable)
* Save location can be changed anytime

---

## 📶 WiFi Direct (No Router)

Use when no shared network is available:

1. On PC A → Start hotspot from **WiFi Direct tab**
2. On PC B → Connect to that network
3. Open LanDrop → devices auto-detect
4. Transfer normally

> Requires Administrator privileges

---

## ⚡ Performance

| Network          | Speed         |
| ---------------- | ------------- |
| Gigabit Ethernet | 80 – 110 MB/s |
| 5 GHz WiFi       | 30 – 80 MB/s  |
| WiFi Direct      | 20 – 50 MB/s  |
| 2.4 GHz WiFi     | 5 – 20 MB/s   |

---

## 🧾 Logs

Stored at:

```
%AppData%\LanDrop\logs\
```

Useful for debugging errors and tracking transfers.

---

## 🧠 Project Architecture

* **Networking** — TCP file transfer + UDP discovery
* **Services** — Settings, hashing, history, WiFi Direct
* **MVVM** — Clean separation using ViewModels
* **WPF UI** — Modern interface with theme support

---

## 🧪 Troubleshooting

| Problem            | Solution                             |
| ------------------ | ------------------------------------ |
| Device not found   | Ensure same network or use manual IP |
| Connection refused | Open firewall ports                  |
| Hotspot fails      | Run as Administrator                 |
| Transfer fails     | Retry (possible network issue)       |
| App crash          | Check logs folder                    |

---

## 🔐 Ports Used

* TCP: `55001` (file transfer)
* UDP: `55002` (device discovery)

---

## 📄 License

MIT License — free to use, modify, and distribute.

---

## 🏗️ Built With

* C#
* WPF
* .NET 8
* CommunityToolkit.Mvvm
* Serilog

---

## ⭐ Support

If you like this project, consider giving it a star on GitHub!
