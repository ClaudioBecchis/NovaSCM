# NovaSCM — Nova Software Configuration Manager

> Open source fleet & network manager per infrastrutture IT Windows e Linux.

[![Release](https://img.shields.io/github/v/release/ClaudioBecchis/NovaSCM?label=versione)](https://github.com/ClaudioBecchis/NovaSCM/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-purple)](https://dotnet.microsoft.com/download/dotnet/9.0)

![NovaSCM Banner](https://novascm.polariscore.it/assets/banner.png)

---

## Cos'è NovaSCM

NovaSCM è un'alternativa open source a Microsoft SCCM per la gestione di reti e flotte di PC. Combina in un'unica interfaccia WPF:

- **Scansione rete** multi-VLAN con rilevamento vendor OUI
- **Deploy Windows zero-touch** via PXE (autounattend.xml + postinstall.ps1)
- **Workflow e automazione** — sequenze di step eseguite dall'agent sui PC gestiti
- **Change Request** — tracciamento completo del ciclo di vita provisioning PC
- **Certificati WiFi EAP-TLS** — autenticazione enterprise senza password
- **Dashboard** — metriche in tempo reale (PC online, workflow attivi, CR aperte)
- **Gestione Proxmox** — VM e container direttamente dall'interfaccia
- **Console SCCM** — visualizzatore read-only per ambienti SCCM esistenti

---

## Download

**[↓ Scarica NovaSCM.exe](https://github.com/ClaudioBecchis/NovaSCM/releases/latest)**

Requisiti: Windows 10/11 (64-bit) · [.NET 9.0 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)

---

## Architettura

```
NovaSCM/
├── MainWindow.xaml/.cs        — UI principale WPF (MVVM)
├── ViewModels/                — ViewModel per ogni tab (9 ViewModel)
│   ├── MainViewModel.cs
│   ├── NetworkViewModel.cs
│   ├── SettingsViewModel.cs
│   ├── WorkflowViewModel.cs
│   ├── ChangeRequestViewModel.cs
│   ├── DeployViewModel.cs
│   ├── DashboardViewModel.cs
│   ├── ProxmoxViewModel.cs
│   └── ...
├── Services/
│   ├── ConfigService.cs       — Config JSON + DPAPI encrypt/decrypt
│   └── NetworkToolsService.cs — Ping, WoL, ARP, Port scan, Traceroute
├── Commands/
│   └── RelayCommand.cs        — ICommand, AsyncRelayCommand
├── NovaSCMAgent/              — Agent .NET 9 Worker Service
├── agent/                     — Agent Python (legacy/Linux)
└── server/
    └── api.py                 — API Flask + SQLite (~2700 righe)
```

---

## Deploy PXE — Flow completo

```
PC boot → DHCP Option 66/67 → TFTP NovaSCM
    ↓
iPXE → GET /api/boot/{mac} → crea host + CR automaticamente
    ↓
WinPE → autounattend.xml dinamico → Windows Setup
    ↓
postinstall.ps1 → enrollment token → installa NovaSCMAgent
    ↓
Agent poll ogni 30s → esegue workflow → riavvio → riprende da dove era rimasto
```

---

## Workflow e automazione

I workflow sono sequenze di step configurabili via GUI, assegnabili a singoli PC o gruppi. L'agent esegue ogni step e riporta il risultato al server.

**Tipi di step supportati:**

| Tipo | Descrizione |
|------|-------------|
| `powershell` | Esegue script PowerShell |
| `cmd` / `shell` | Esegue comando shell |
| `winget_install` | Installa pacchetto via winget |
| `reg_set` | Imposta chiave di registro |
| `file_copy` | Copia file |
| `reboot` | Riavvia il PC (con resume automatico post-reboot) |
| `wait` | Attende N secondi |
| `systemd_service` | Gestisce servizi Linux |

**Condizioni:** ogni step può essere condizionato a `windows`, `linux` o `hostname=NOME-PC`.

**Reboot resume:** l'agent salva lo stato in modo atomico prima del riavvio e riprende dal passo successivo automaticamente.

---

## Server API

Il server è un'API Flask + SQLite che gira come servizio **systemd** su LXC Proxmox (porta 9091).

```bash
# Installazione
pip install flask gunicorn
cp api.py /opt/novascm/
systemctl enable --now novascm
```

**Endpoint principali:**

| Metodo | Endpoint | Descrizione |
|--------|----------|-------------|
| GET | `/health` | Healthcheck (no auth) |
| GET/POST | `/api/cr` | Lista / crea Change Request |
| PUT | `/api/cr/<id>/status` | Cambia stato CR |
| GET | `/api/boot/<mac>` | Boot PXE (iPXE) |
| GET | `/api/cr/<id>/autounattend.xml` | XML per WinPE |
| POST | `/api/enrollment-token` | Token monouso per agent |
| GET | `/api/workflows` | Lista workflow |
| GET | `/api/version` | Versione (auto-update) |

**Autenticazione:** header `X-Api-Key` oppure query param `?key=`. Confronto timing-safe con `hmac.compare_digest`. Token enrollment monouso con scadenza automatica.

---

## NovaSCM Agent

Servizio Windows (.NET 9 Worker) che gira in background sui PC gestiti.

```bash
# Installazione da PowerShell admin
iwr http://<SERVER>:9091/api/download/agent-install.ps1 | iex
```

**Sicurezza:**
- Comandi eseguiti con `ArgumentList` — no shell injection
- API key passata via file temporaneo con ACL restrittiva (non env var)
- Retry 3× con backoff esponenziale su errori di rete
- State file scritto in modo atomico (temp + rename) — crash-safe

---

## Sicurezza

- DPAPI per credenziali in config (fallback plain:base64 se DPAPI non disponibile)
- Security headers: `X-Content-Type-Options`, `X-Frame-Options`, `Cache-Control`
- SSRF protection su webhook e validazione URL agent
- Enrollment token UUID → `secrets.token_hex(32)`
- SQLite WAL mode — no lock su letture concorrenti

---

## Build

```bash
# Debug
dotnet build PolarisManager.csproj -c Debug

# Release (richiede .NET 9 installato sul target)
dotnet publish PolarisManager.csproj -c Release -r win-x64 --self-contained false -o publish/
```

---

## Sito web

[novascm.polariscore.it](https://novascm.polariscore.it)

---

## Licenza

MIT — © 2026 [Claudio Becchis](https://github.com/ClaudioBecchis) · [PolarisCore.it](https://polariscore.it)
