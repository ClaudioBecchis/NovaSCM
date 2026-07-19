# NovaSCM — Nova Software Configuration Manager

> Open source fleet & network manager per infrastrutture IT Windows e Linux.

[![Release](https://img.shields.io/github/v/release/ClaudioBecchis/NovaSCM?label=versione)](https://github.com/ClaudioBecchis/NovaSCM/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-purple)](https://dotnet.microsoft.com/download/dotnet/9.0)

![NovaSCM Banner](https://novascm.polariscore.it/assets/banner.png)

---

## Cos'è NovaSCM

NovaSCM è un'alternativa open source a Microsoft SCCM per la gestione di reti e flotte di PC. È composto da due parti: un **client Windows** (console WPF) e un **server API** (Python, multipiattaforma) — vedi [Download](#download) e [Server API](#server-api). Il client offre:

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

> ⚠️ **Questo è solo il client** (la console di gestione, solo Windows). Scansione rete, ping e Wake-on-LAN funzionano subito da soli. Per il deploy PXE, i workflow e la gestione flotta completa serve anche il **server API** — un componente separato, multipiattaforma (Docker/Linux/Windows/macOS), vedi la sezione [Server API](#server-api) più sotto.

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

Il server è un'API Flask + SQLite (porta 9091 di default). È un componente **separato** dal client: il client (NovaSCM.exe) gira solo su Windows, il server invece gira ovunque ci sia Python o Docker — Linux, Windows, macOS.

**Requisiti:**
- Docker (qualsiasi OS) — **opzione consigliata**, oppure
- Python 3.12 nativo

### Opzione 1 — Docker (consigliato, qualsiasi sistema operativo)

```bash
git clone https://github.com/ClaudioBecchis/NovaSCM.git
cd NovaSCM/server
docker compose up -d
```

L'API sarà disponibile su `http://localhost:9091`. Vedi `server/README.md` per le variabili d'ambiente (API key, abilitazione PXE, ecc.).

### Opzione 2 — Python nativo (qualsiasi Linux/Windows/macOS con Python 3.12)

```bash
cd NovaSCM/server
pip install -r requirements.txt
python api.py
```

Per farlo girare come servizio persistente in background serve un supervisore di processo (systemd su Linux, NSSM/Task Scheduler su Windows, ecc.) configurato manualmente — non incluso in questo comando.

### Opzione 3 — Proxmox LXC automatico

Se il tuo ambiente è un container LXC Proxmox (Debian), lo script `server/deploy/install-flask-ct.sh` automatizza tutto (dipendenze, Samba, unit systemd incluso):

```bash
./server/deploy/install-flask-ct.sh <CTID> <API_KEY> <SERVER_IP>
```

### Collegare il client al server

Una volta che il server è avviato (con una qualsiasi delle 3 opzioni sopra):

1. Recupera l'API key: con Docker/Python nativo viene generata al primo avvio e salvata in `data/.api_key` (o stampata nei log all'avvio); con lo script Proxmox l'hai passata tu come argomento.
2. Apri NovaSCM.exe → **Impostazioni** → **URL API NovaSCM**.
3. Inserisci l'indirizzo del server, es. `http://localhost:9091` (stessa macchina) o `http://192.168.x.x:9091` (server su un'altra macchina/rete).
4. Inserisci l'API key nel campo corrispondente.

Da qui il client comunica col server per tutte le funzioni che lo richiedono (deploy PXE, workflow, gestione flotta).

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

**Come dialoga con il server:** l'agent fa polling HTTP verso il server ogni 30 secondi (nessuna porta in ingresso richiesta sul PC gestito). Ad ogni poll: controlla se ci sono workflow assegnati, esegue lo step successivo, riporta l'esito al server (`/api/cr/by-name/<pc>/step`). Se il PC viene riavviato durante un workflow (es. dopo un `winget_install` che lo richiede), l'agent riprende automaticamente dal passo successivo al riavvio.

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
