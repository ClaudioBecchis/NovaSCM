# CLAUDE.md — NovaSCM Project Context

> Questo file è letto automaticamente da Claude Code all'inizio di ogni sessione.
> Aggiornato a: **v1.7.6** (commit `5802c44`)

---

## Progetto: NovaSCM

Applicazione WPF (.NET 9, C#) + server Flask (Python) + agent Python/C# per la gestione di reti e fleet PC.
Repository: https://github.com/ClaudioBecchis/NovaSCM

### Struttura principale

| Percorso | Descrizione |
|---|---|
| `*.cs` / `*.xaml` | GUI WPF — namespace `PolarisManager` |
| `NovaSCMAgent/` | .NET Worker Service (agent Windows) |
| `server/api.py` | Flask server (Python 3, SQLite, WAL mode) |
| `server/tests/` | pytest — 78/78 test passing |
| `agent/novascm-agent.py` | Agent Python (Linux/Mac) |
| `agent/install-windows.ps1` | Installer Windows con SHA256 verify |
| `agent/install-linux.sh` | Installer Linux con SHA256 verify |
| `installer/NovaSCM.iss` | Script Inno Setup |
| `wiki/` | Documentazione Markdown |

---

## Versione corrente: v1.7.6

**Commit base:** `5802c44`
**Data:** 2026-03-09
**Stato:** Stabile — nessun bug aperto

### Changelog v1.7.6
- Agent: verifica `pw_id` prima di usare `resume_step` (evita resume su workflow sbagliato)
- StepExecutor: `@catArgs` passato correttamente a `Get-WindowsUpdate` e `Install-WindowsUpdate`
- Installer PS1/SH: aggiunta verifica SHA256 dopo download agent
- Test: `test_health_does_not_expose_db_path` (invece di `test_health_includes_db_field`)

---

## Regole di sviluppo

### Git workflow
```
# SOLO codice sorgente — nessun binario, nessuna release ZIP
git add <file specifici>
git commit -m "vX.Y.Z — descrizione fix"
git push origin main
```

### Versioning — dove aggiornare
1. `App.xaml.cs` — `AppVersion` (riga ~21)
2. `installer/NovaSCM.iss` — `#define AppVersion`
3. `CHANGELOG.md` — aggiungere riga in cima alla tabella
4. `wiki/Home.md` — aggiornare tabella changelog rapido

### Build & Test
```bash
# Test server (78/78)
cd server && python -m pytest tests/ -v

# Build WPF
dotnet build

# Publish self-contained
dotnet publish -c Release -r win-x64 --self-contained true -o publish_v2
```

---

## Architettura server (api.py)

- **Framework**: Flask + SQLite (WAL mode, `PRAGMA journal_mode=WAL`)
- **Auth**: header `X-Api-Key` su tutti gli endpoint tranne `/health`
- **Health check**: `GET /health` — risponde `{"status":"ok","db":true}` senza auth
- **SHA256**: calcolato dinamicamente dall'API, non file statico

### Endpoint principali
| Metodo | Path | Descrizione |
|---|---|---|
| GET | `/health` | Health check (no auth) |
| GET/POST | `/api/pcs` | Lista/registra PC |
| GET/PUT | `/api/pcs/<name>` | Dettaglio/aggiorna PC |
| GET | `/api/workflows` | Lista workflow |
| POST | `/api/workflows/<id>/assign` | Assegna workflow a PC |
| POST | `/api/agent/checkin` | Check-in agent (heartbeat + step result) |
| GET | `/api/download/agent` | Scarica agent Python |
| GET | `/api/download/agent.sha256` | SHA256 agent |

---

## NovaSCMAgent (.NET Worker)

- `Worker.cs`: loop polling, verifica `pw_id` prima di resume
- `ApiClient.cs`: `BuildRequest()` per header per-request (thread-safe)
- `StepExecutor.cs`: esegue step workflow (run_script, install_software, ecc.)
- `AgentVer`: letta da `Assembly.GetEntryAssembly().GetName().Version`

---

## Security fixes applicati

| ID | Descrizione |
|---|---|
| SEC-01 | Command injection agent `run_cmd()` — `shell=False` hardcodato |
| SEC-02 | Argument injection UI — `ProcessStartInfo.ArgumentList` |
| SEC-03 | SCP/SSH injection in `NovaSCMApiService` |
| SEC-05 | `shell=False` hardcodato agent Python |
| SEC-06 | Path traversal in `file_copy` (StepExecutor + agent) |
| C-7 | SHA256 verifica agente negli installer (v1.7.6) |

---

## Infrastruttura di test

- **Proxmox VM 120** (`win-client`): test deploy zero-touch Windows 11
  - Boot: OVMF UEFI, `ide2` = Windows ISO, `ide3` = autounattend ISO, `sata0` = disco
  - Hookscript: `local:snippets/vm120-autoboot.sh`
- **CT 103** (192.168.20.110:9091): NovaSCM server via gunicorn + systemd
  - API key in `/opt/novascm/.env`
  - Log: `journalctl -u novascm -f`
