# NovaSCM

> **Open source fleet & deployment manager for Windows and Linux — SCCM-inspired, self-hosted.**

NovaSCM is a lightweight alternative to Microsoft SCCM/Intune for small and medium IT environments.
It combines a **WPF desktop console** (Windows), a **REST API server** (Python/Flask) and a **cross-platform agent** (Linux & Windows) to manage PC deployments, software installation, network scanning and WiFi certificate enrollment — all from a single interface.

---

## What it does

### Fleet Management
- **Network scanner** — discovers devices by IP/MAC, vendor detection, open port scan
- **Device inventory** — hostname, OS, hardware details, last seen
- **RDP & SSH one-click** — connect to any device directly from the console
- **Change Request system** — track deployment jobs per machine with status and notes

### Software Deployment (like SCCM Task Sequences)
- **Visual Workflow editor** — create multi-step deployment sequences (install, script, reboot, message...)
- **Step types**: `winget_install`, `apt_install`, `shell_script`, `windows_update`, `reboot`, `message`, `systemd_service`, `registry`, `file_copy`, `powershell`
- **Per-platform steps** — tag steps as `windows`, `linux` or `all`
- **Real-time progress** — live status per device, step-by-step log, progress bar
- **Auto-assign** — assign a default workflow; agent picks it up on next check-in

### OS Deployment (Zero-touch)
- **Generates `autounattend.xml`** — Windows 11 unattended install file, ready for USB or PXE
- **Generates `postinstall.ps1`** — runs on first boot: installs software via winget, enrolls the device
- **USB & PXE support** — copy files to a USB stick or push to a PXE server via SCP
- **PC naming template** — `PC-{MAC6}` renames the machine automatically from its MAC address

### WiFi 802.1X EAP-TLS
- **Certificate portal** — issues client certificates signed by an internal CA
- **Auto-enrollment agent** — installed on Windows, runs at startup, installs cert silently, adds WiFi profile
- **iOS mobileconfig** — one-tap install of CA + cert + WiFi profile from Safari
- **Android support** — QR code enrollment flow

### Network & Security
- **OPSI integration** — manage OPSI software packages and deployments
- **Port scanner** — per-device open port report
- **App catalog** — install/uninstall applications remotely

---

## Architecture

```
┌──────────────────────┐      HTTP/REST      ┌──────────────────────────┐
│  NovaSCM Console     │ ◄─────────────────► │  NovaSCM Server          │
│  (WPF, Windows)      │                     │  (Flask + SQLite)        │
└──────────────────────┘                     │  port 9091               │
                                             └──────────┬───────────────┘
                                                        │ polling
                                             ┌──────────▼───────────────┐
                                             │  NovaSCM Agent           │
                                             │  (Python — Win & Linux)  │
                                             │  executes workflow steps  │
                                             └──────────────────────────┘
```

| Component | Technology | Platform |
|-----------|-----------|----------|
| Console (GUI) | C# / WPF / .NET 8 | Windows |
| Server (API) | Python 3 / Flask | Linux / Docker |
| Agent | Python 3 | Windows & Linux |
| Database | SQLite | — |
| Web UI | Alpine.js | Browser |

---

## Quick Start

### 1. Start the server

```bash
cd server
docker compose up -d
```

Server runs at `http://localhost:9091`.
Web UI available at `http://localhost:9091` (open in browser).

Or without Docker:
```bash
pip install flask gunicorn
python api.py
```

### 2. Seed demo data

```bash
python seed_demo.py --db /data/novascm.db
```

Creates 3 workflows, 6 demo machines and 4 assignments in different states (completed / running / pending).

### 3. Run the console

Download `NovaSCM.exe` from [Releases](../../releases) and run it.
Go to **Settings → NovaSCM API URL** and enter `http://<server-ip>:9091`.

### 4. (Optional) Deploy the agent

On a Windows target machine (admin PowerShell):
```powershell
iwr http://<server-ip>:9091/agent/install.ps1 | iex
```

On Linux:
```bash
curl -s http://<server-ip>:9091/agent/install-linux.sh | bash
```

---

## Console Tabs

| Tab | Description |
|-----|-------------|
| **Network** | Scan subnets, view devices, RDP/SSH |
| **Certs** | Issue WiFi EAP-TLS certificates, manage CA |
| **Apps** | Application catalog, remote install |
| **PC** | Device list, Change Requests, enrollment |
| **OPSI** | OPSI package management |
| **Deploy** | Generate autounattend.xml + postinstall.ps1 for USB/PXE |
| **Workflow** | Create and manage deployment workflows, assign to PCs |
| **Requests** | Change Request tracker with status and log |
| **Settings** | API URL, subnets, credentials |

---

## Requirements

### Console
- Windows 10/11 x64
- .NET 8 Runtime (or use the self-contained single-file exe from Releases)

### Server
- Python 3.10+ or Docker
- 512 MB RAM, 1 GB disk

### Agent
- Python 3.8+ (Windows or Linux)
- Administrator / root privileges

---

## Demo Styles

NovaSCM includes 3 GUI style demos (About tab → Demo Stili GUI) inspired by:
- **SCCM Console** — ribbon toolbar, tree navigation, results + details pane
- **Advanced Installer** — dark blue header, sidebar, stat cards
- **MSIX Packaging Tool** — step-by-step wizard layout

These are previews to choose the final UI design direction.

---

## License

MIT License — free to use, modify and distribute.
© 2026 Claudio Becchis — [PolarisCore.it](https://polariscore.it)
