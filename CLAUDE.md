# NovaSCM — CLAUDE.md

## Cos'è NovaSCM

NovaSCM è un sistema open-source di fleet management e deploy automatico Windows — alternativa self-hosted a Microsoft SCCM/Intune. Gestisce PC aziendali: deploy zero-touch via PXE, installazione software, domain join, workflow di provisioning.

**Autore:** Claudio Becchis (sviluppatore unico)  
**Licenza:** MIT  
**Versione:** v2.2.1
**Test:** 142/142 ✅ (112 core + 30 PXE — tutti i bug v2.2.1 fixati)

---

## Architettura

```
┌─────────────────────┐     HTTP :9091      ┌──────────────────────────┐
│ Console WPF         │ ◄─────────────────► │ Server Flask + SQLite    │
│ (C#, Windows)       │                     │ CT 103 — 192.168.20.110  │
│ MainWindow.xaml.cs  │                     │ server/api.py (2585 LOC) │
└─────────────────────┘                     └──────────┬───────────────┘
                                                       │
                     ┌─────────────────────────────────┼───────────────────┐
                     │                                 │                   │
              ┌──────▼──────┐                   ┌──────▼──────┐    ┌──────▼──────┐
              │ UI Web      │                   │ Agent .NET  │    │ Agent Python│
              │ Alpine.js   │                   │ Windows     │    │ Linux       │
              │ index.html  │                   │ NovaSCMAgent│    │ novascm-    │
              └─────────────┘                   └─────────────┘    │ agent.py    │
                                                                   └─────────────┘
```

### Flusso PXE Deploy (zero-touch)

```
PC acceso → DHCP (UCG-Fiber, VLAN 10) option 66/67 → TFTP :69 (ipxe.efi)
  → iPXE GET /api/boot/{mac} → script iPXE
  → wimboot + BCD + boot.sdi + boot.wim via /api/pxe/file/*
  → autounattend.xml dinamico via /api/autounattend/{pc_name}
  → Windows Setup → FirstLogonCommands → NovaSCMAgent → workflow
```

---

## Mappa file

### Server (Python/Flask) — la parte principale

| File | Righe | Ruolo |
|---|---|---|
| `server/api.py` | 2585 | **Tutto il backend** — API REST, DB, PXE, autounattend, auth |
| `server/pxe_server.py` | 94 | TFTP server thread (serve ipxe.efi) |
| `server/web/index.html` | ~3000 | UI web completa (Alpine.js SPA) |
| `server/docker-compose.yml` | 39 | Deploy Docker — porte 9091 + 69/udp |
| `server/Dockerfile` | 24 | Immagine container |
| `server/requirements.txt` | 5 | flask, gunicorn, flask-limiter, tftpy |
| `server/tests/test_api.py` | ~1300 | Test suite (142 test — 112 core + 30 PXE) |
| `server/seed_demo.py` | — | Dati demo per sviluppo |

### Console WPF (C#) — client Windows

| File | Ruolo |
|---|---|
| `MainWindow.xaml` + `.cs` | Finestra principale (~189K XAML + 335K code-behind) |
| `Database.cs` | Accesso DB locale |
| `NovaSCMApiService.cs` | Client HTTP per API server |
| `OsdWindow.xaml` + `.cs` | Finestra OSD (deploy screen) |
| `CrDebugWindow.xaml` + `.cs` | Debug Change Request |
| `InventoryWindow.xaml` + `.cs` | Inventario dispositivi |
| `WfDialogs.cs` | Dialog workflow |

### Agenti

| File | Ruolo |
|---|---|
| `NovaSCMAgent/Worker.cs` | Loop principale agente Windows (.NET) |
| `NovaSCMAgent/StepExecutor.cs` | Esecutore step workflow |
| `NovaSCMAgent/ApiClient.cs` | Client API per agente |
| `agent/novascm-agent.py` | Agente Python (Linux) |
| `agent/install-windows.ps1` | Installer agente Windows |
| `agent/install-linux.sh` | Installer agente Linux |

### Deploy

| File | Ruolo |
|---|---|
| `deploy/autounattend.xml` | Template statico (credenziali → `CHANGE_ME`, usare `/api/autounattend/<pc>`) |
| `deploy/postinstall.ps1` | Script post-installazione Windows |
| `deploy/pxe.conf` | Config proxyDHCP backup per dnsmasq (NovaSCM iPXE @ 192.168.20.110) |

### Cartelle runtime (non in git)

| Percorso | Contenuto |
|---|---|
| `server/dist/ipxe.efi` | Bootloader iPXE (~400KB) |
| `server/dist/winpe/wimboot` | Bootloader wimboot (~50KB) |
| `server/dist/winpe/BCD` | Boot Configuration Data |
| `server/dist/winpe/boot.sdi` | System Deployment Image (~3MB) |
| `server/dist/winpe/boot.wim` | Windows PE image (~300-500MB) |

---

## Database (SQLite)

Tabelle principali in `init_db()` (api.py riga 236):

| Tabella | Scopo |
|---|---|
| `cr` | Change Request — un PC da deployare (nome, dominio, OU, password, software) |
| `workflows` | Template workflow (sequenza di step) |
| `workflow_steps` | Step di un workflow (tipo, parametri, ordine) |
| `pc_workflows` | Assegnazione workflow → PC (stato: pending/running/done/error) |
| `pc_workflow_steps` | Stato esecuzione di ogni step per PC |
| `pxe_hosts` | Mappa MAC → PC per PXE boot |
| `pxe_boot_log` | Log di tutti i boot PXE |
| `settings` | Configurazione chiave-valore (dominio, PXE settings, webhook) |
| `deploy_tokens` | Token monouso per enrollment agente |
| `enrollment_tokens` | Token enrollment (legacy) |

---

## API endpoints principali

### Autenticati (richiedono header `X-Api-Key`)
- `GET/POST /api/cr` — CRUD Change Request
- `GET/POST/PUT/DELETE /api/workflows` — CRUD workflow
- `GET/POST /api/pc-workflows` — assegnazioni workflow
- `GET/PUT /api/settings` — configurazione server
- `GET/PUT/DELETE /api/pxe/hosts` — gestione host PXE
- `GET/PUT /api/pxe/settings` — configurazione PXE
- `GET /api/pxe/status` — health check PXE + stato file WinPE

### Senza auth (protetti da subnet allow-list)
- `GET /api/boot/<mac>` — script iPXE dinamico per boot PXE
- `GET /api/pxe/file/<name>` — serve file WinPE (wimboot, BCD, boot.sdi, boot.wim, install.wim)
- `GET /api/autounattend/<pc_name>` — XML autounattend dinamico per PXE
- `POST /api/deploy/enroll` — enrollment agente con token monouso

### Senza auth (pubblici)
- `GET /api/version` — versione server
- `GET /health` — health check

---

## Rete PolarisCore (homelab)

| Risorsa | IP | VLAN |
|---|---|---|
| NovaSCM server (CT 103) | 192.168.20.110 | 20 (Servers) |
| PC client | 192.168.10.x | 10 (Trusted) |
| UCG-Fiber (gateway/DHCP) | 192.168.10.1 / .20.1 | tutte |
| Pi-hole DNS | 192.168.20.253 | 20 |
| Windows Server AD | 192.168.20.12 | 20 |
| Dominio | polariscore.it | — |

DHCP option 66/67 vanno sulla **VLAN 10** (dove sono i client), puntano a 192.168.20.110.

---

## Bug v2.2.1 — tutti fixati ✅

I bug critici e medi del report v2.2.1 sono stati risolti nel commit `591a816`.

| ID | Severità | Fix applicato |
|----|----------|---------------|
| C-1 | 🔴 | `DIST_DIR` + `_WINPE_DIR` definiti come globali |
| C-3 | 🔴 | `<ImageInstall><InstallTo>` aggiunto in `_build_autounattend_xml_pxe` |
| C-4 | 🔴 | `pxe_server.py` aggiunto al Dockerfile |
| C-5 | 🔴 | API key sostituita con enrollment token monouso nell'autounattend PXE |
| H-1 | 🟠 | 15 timestamp uniformati a UTC con `now(datetime.timezone.utc)` |
| H-2 | 🟠 | Validazione `boot_action` in create/update host PXE |
| H-3 | 🟠 | 30 test PXE aggiunti — totale ora 142/142 ✅ |
| M-1 | 🟡 | `_get_pxe_settings()` usa `_PXE_SETTINGS_DEFAULTS` come base |
| M-2 | 🟡 | `send_file` spostato in import top-level |
| M-3 | 🟡 | `editPxeHost()` e `openPxeHostModal()` implementati |
| M-4 | 🟡 | `deploy/pxe.conf` aggiornato per NovaSCM iPXE |
| I-1 | 🔵 | Cleanup boot-log reso probabilistico (1%) |
| I-2 | 🔵 | `import ipaddress` spostato in cima al file |

**Problemi aperti (infrastruttura, non codice):** vedi `docs/PXE_INTEGRATION_CLAUDECODE.md` sezione "PROBLEMI NOTI E PUNTI DA COMPLETARE" (P-1..P-12).
I principali: copiare `ipxe.efi`, i file WinPE e `install.wim` in `server/dist/`, configurare DHCP option 66/67 sul gateway UCG Fiber.

---

## Convenzioni codice

- **Python:** Flask, SQLite via `get_db_ctx()` context manager, `require_auth` decorator
- **Auth:** header `X-Api-Key` con HMAC compare, o session token da `/api/ui-token`
- **Date:** ISO 8601 UTC senza timezone (`datetime.datetime.now(datetime.timezone.utc).replace(tzinfo=None)`)
- **PC names:** uppercase, validati con `_PC_NAME_RE = r'^[A-Z0-9][A-Z0-9\-]{0,14}$'`
- **MAC format:** `AA:BB:CC:DD:EE:FF` uppercase (normalizzato da `_normalize_mac()`)
- **Settings:** tabella `settings` chiave-valore, PXE settings con prefisso `pxe_`
- **XML escape:** `_xe()` = `xml.sax.saxutils.escape`, `_xe_ps()` aggiunge escape apici PowerShell
- **Test:** `pytest`, fixture `client` e `api_headers`, DB in `/tmp/test.db`

---

## Come eseguire

### Server locale (sviluppo)
```bash
cd server
NOVASCM_DB=/tmp/novascm.db NOVASCM_API_KEY=test python api.py
# Server su http://localhost:9091
```

### Docker (produzione)
```bash
cd server
docker-compose up -d
# Configurare NOVASCM_API_KEY in .env o docker-compose.yml
```

### Test
```bash
cd server
NOVASCM_DB=/tmp/test.db NOVASCM_API_KEY=test pytest tests/ -v
```

---

## Prossimi step (infrastruttura)

Tutti i bug di codice sono risolti. Rimane da completare l'infrastruttura sul server CT 103:

```bash
# 1. Scarica ipxe.efi
wget https://boot.ipxe.org/ipxe.efi -O /opt/novascm/dist/ipxe.efi

# 2. Copia file WinPE dall'ISO estratta su CT 110
scp root@192.168.10.122:/DATA/win11-extracted/boot/bcd      /opt/novascm/dist/winpe/BCD
scp root@192.168.10.122:/DATA/win11-extracted/boot/boot.sdi /opt/novascm/dist/winpe/boot.sdi
scp root@192.168.10.122:/DATA/win11-extracted/sources/boot.wim /opt/novascm/dist/winpe/boot.wim

# 3. Scarica wimboot
wget https://github.com/ipxe/wimboot/releases/latest/download/wimboot \
     -O /opt/novascm/dist/winpe/wimboot

# 4. Copia install.wim (~4GB)
scp root@192.168.10.122:/DATA/win11-extracted/sources/install.wim \
    /opt/novascm/dist/winpe/install.wim

# 5. Configura DHCP option 66/67 su UCG Fiber (https://192.168.10.1)
#    VLAN 10: next-server = 192.168.20.110, filename = ipxe.efi
```

---

*Ultimo aggiornamento: 2026-03-12 — v2.2.1*
