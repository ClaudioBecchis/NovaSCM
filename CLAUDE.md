# CLAUDE.md — NovaSCM
> **Leggi questo file per intero prima di modificare qualsiasi file del progetto.**  
> Aggiornato al commit `d175fc1` (v1.7.6) — 09/03/2026  
> Round 5 completato — 4 nuovi bug identificati, nessuno critico

---

## Stato attuale

| | |
|---|---|
| Versione | **v1.7.6** — commit `d175fc1` |
| Test suite | **78/78 ✅** |
| Deploy test Round 5 | **78/78 via pytest** ✅ |
| Bug aperti | **4** (0 critici, 2 medium, 2 info) |
| Ultimo round | Round 5 — analisi statica su v1.7.6 |

**Comando test — eseguilo sempre dopo ogni modifica:**
```bash
cd server
NOVASCM_DB=/tmp/t.db NOVASCM_API_KEY=test-key python -m pytest tests/ -v
```
Risultato atteso: `78 passed`.

---

## Bug aperti — Round 5

### M-1 · `agent/install-windows.ps1` + `agent/install-linux.sh` · MEDIUM
**api_key mai scritta nel config dell'agent dopo installazione**

Entrambi gli installer (statico PS1 e SH) creano `agent.json` con `api_url`, `pc_name`, `poll_sec` ma **non con `api_key`**. Anche gli installer dinamici generati dall'API (`/api/download/agent-install.ps1` e `.sh`) non passano la chiave. L'agent parte con `api_key = ""` → tutte le chiamate all'API falliscono con 401.

**Fix `agent/install-windows.ps1`** — aggiungi parametro `-ApiKey` e scrivilo nel config:

Cerca la firma attuale:
```powershell
param(
    [string]$ApiUrl  = "http://YOUR-SERVER-IP:9091",
    [string]$PcName  = $env:COMPUTERNAME,
    [int]   $PollSec = 60
)
```
Sostituisci con:
```powershell
param(
    [string]$ApiUrl  = "http://YOUR-SERVER-IP:9091",
    [string]$ApiKey  = "",
    [string]$PcName  = $env:COMPUTERNAME,
    [int]   $PollSec = 60
)
```

Poi trova il blocco `# ── 4. Crea config` e aggiungi `api_key`:
```powershell
@{
    api_url  = $ApiUrl
    api_key  = $ApiKey
    pc_name  = $PcName.ToUpper()
    poll_sec = [int]$PollSec
} | ConvertTo-Json | Set-Content -Path $ConfigFile -Encoding UTF8
```

**Fix `agent/install-linux.sh`** — aggiungi parametro `--api-key`:

Cerca nella sezione parsing argomenti il blocco `--api-url`:
```bash
--api-url)   API_URL="$2";  shift 2;;
```
Aggiungi subito dopo:
```bash
--api-key)   API_KEY="$2";  shift 2;;
```

Aggiungi la variabile default in cima (dopo `POLL_SEC=60`):
```bash
API_KEY=""
```

Poi trova il blocco `# ── 6. Crea config` e aggiungi `api_key`:
```bash
cat > "$CONFIG_DIR/agent.json" << EOF
{
  "api_url":  "$API_URL",
  "api_key":  "$API_KEY",
  "pc_name":  "$PC_NAME",
  "poll_sec": $POLL_SEC
}
EOF
```

**Fix `server/api.py`** — gli installer dinamici devono accettare e propagare `?api_key=`:

In `download_agent_installer_ps1()`:
```python
api_url = request.host_url.rstrip("/")
api_key = request.args.get("api_key", "")   # ← aggiungi questa riga
```
Nel PS1 generato, aggiungi `-ApiKey '{api_key}'` agli argomenti di `Start-Process`.

Stessa modifica in `download_agent_installer_sh()`.

---

### M-2 · `server/api.py` · MEDIUM
**`request.host_url` negli installer dinamici non validato — Host header injection**

Le funzioni `download_agent_installer_ps1()` e `download_agent_installer_sh()` usano `request.host_url` per costruire `$ApiUrl` / `API_URL` nell'installer generato. Se Flask gira dietro un reverse proxy senza `ProxyFix`, un attaccante che controlla l'header `Host:` può far sì che l'installer generato punti a un server malevolo.

**Fix** — aggiungi `ProxyFix` a `server/api.py` (dopo le import, prima di `app = Flask(__name__)`):
```python
from werkzeug.middleware.proxy_fix import ProxyFix
```
Dopo la creazione dell'app:
```python
app = Flask(__name__)
app.wsgi_app = ProxyFix(app.wsgi_app, x_for=1, x_proto=1, x_host=1, x_prefix=1)
```
Aggiungi `werkzeug` a `requirements.txt` se non già presente (fa parte di Flask, ma esplicitarlo è buona pratica).

---

### I-1 · `agent/install-linux.sh` (statico) · INFO
**`set -e` invece di `set -euo pipefail`**

Lo script statico `agent/install-linux.sh` usa `set -e`. Lo script dinamico generato dall'API usa correttamente `set -euo pipefail`. I due sono disallineati.

**Fix** — in `agent/install-linux.sh`, riga 5:
```bash
set -e
```
Sostituisci con:
```bash
set -euo pipefail
```

---

### I-2 · `agent/novascm-agent.py` · INFO
**Tipo step `windows_update` non implementato nell'agent Python**

`NovaSCMAgent/StepExecutor.cs` implementa 11 tipi di step incluso `windows_update`. L'agent Python `novascm-agent.py` implementa solo 10 tipi — manca `windows_update`. Se un workflow con step `tipo=windows_update` viene assegnato a un PC Linux, l'agent risponde "tipo step sconosciuto" e il passo risulta in errore invece di essere gestito.

**Fix** — aggiungi in `novascm-agent.py` nella funzione `esegui_step()`, nel blocco elif dei tipi, dopo l'ultimo elif:
```python
elif tipo == "windows_update":
    log.info("Step windows_update: ignorato su Linux (solo Windows)")
    return {"stato": "skipped", "output": "windows_update non supportato su Linux — skipped"}
```

---

## Ordine di esecuzione consigliato (v1.7.7)

1. **I-1** — `agent/install-linux.sh`: `set -euo pipefail` (1 min)
2. **I-2** — `agent/novascm-agent.py`: aggiunta caso `windows_update` → skipped (3 min)
3. **M-1** — `agent/install-windows.ps1` + `install-linux.sh` + `server/api.py`: parametro `api_key` (10 min)
4. **M-2** — `server/api.py`: aggiunta `ProxyFix` + `requirements.txt` (5 min)
5. `cd server && NOVASCM_DB=/tmp/t.db NOVASCM_API_KEY=test pytest tests/ -v` → verifica 78/78
6. `git commit -m "fix: api_key installer, ProxyFix, set -euo pipefail, windows_update skip Linux"`

---

## Regole — cosa NON toccare

### Autenticazione
- `/health` è **senza** `@require_auth` — necessario per Docker healthcheck (C-1, v1.7.5). Non aggiungere `@require_auth`.
- Tutti gli altri endpoint hanno `@require_auth`.

### Sicurezza già fixata
- `get_autounattend()` usa `xml.sax.saxutils.escape()` su tutti i valori XML (SEC-1, v1.7.3).
- Installer generati: no `Invoke-Expression`, no `curl|bash`, download in temp + SHA256 (SEC-2, v1.7.3).
- `NOVASCM_API_KEY` sempre da env var (v1.7.2).
- `_validate_api_url()` blocca SSRF.

### .NET Agent
- `ApiClient.cs` — `X-Api-Key` per-request in `BuildRequest()`, non in `DefaultRequestHeaders` (race condition, BUG-6).
- `Worker.cs` — resume verifica `PwId` salvato vs workflow corrente (BUG-8).
- `NovaSCMApiService.cs` — `ApiBase` via `Uri.Authority` (BUG-9).
- `SendAsync` / `DownloadExeAsync` usano `using var resp` (M-3, v1.7.5).

### Python Agent
- `run_workflow()` verifica `pw_id` salvato vs workflow corrente (M-1, v1.7.6). Non rimuovere.
- Tutti i comandi usano `shell=False` con lista argomenti.

### Server
- `report_wf_step()` conta `done + skipped + error` per completare il workflow (BUG-3).
- `update_step()` valida `tipo` con whitelist (BUG-1).
- `init_db()` crea 4 indici. Non rimuoverli.

---

## Storico fix (Round 1–4, tutti completati)

<details>
<summary>Round 1 — v1.7.2 (13 fix)</summary>

FIX-1..13: path NSSM, placeholder IP, tipo PollSec, VOLUME Dockerfile, API key env var, systemd hardening Linux, Inno Setup path, requirements.txt, Docker resource limits, NSSM version + RMDir NSIS, versione da Assembly, null check workflow_nome, pubxml R2R conflict. Rimossi ~259MB binari da Git.
</details>

<details>
<summary>Round 2 — v1.7.3 (3 SEC + 10 BUG + 2 INFO)</summary>

SEC-1: XML injection → escape. SEC-2: pipe-to-shell RCE → temp+SHA256. SEC-3: /health @require_auth (poi rimosso). BUG-1..10: whitelist tipo, nome workflow vuoto, completamento workflow con su_errore=continua, init_db exception, indici SQLite, SetApiKey per-request, DI inutilizzato, resume PwId check, ApiBase Uri.Authority, EnsureSuccessStatusCode. INFO-1: rate limiter env. INFO-2: cache config mtime.
</details>

<details>
<summary>Round 3 — v1.7.4 (C-7 SHA256 installer)</summary>

C-7: SHA256 verification per agent installer + endpoint GET /api/download/agent.sha256.
</details>

<details>
<summary>Round 4 — v1.7.5 + v1.7.6</summary>

v1.7.5 — C-1 (CRITICO): rimosso @require_auth da /health (Docker healthcheck loop). M-1: test 78/78. M-2: DownloadExeAsync EnsureSuccessStatusCode. M-3: HttpResponseMessage using.
v1.7.6 — M-1: novascm-agent.py pw_id check. M-2: StepExecutor @catArgs. I-1: SHA256 install-windows.ps1. I-2: SHA256 install-linux.sh.
</details>

---

## Deploy test Round 5 (78/78 via pytest ✅)

Test eseguiti tramite suite pytest su Flask test client. Tutti i 78 test passano su v1.7.6. Il server è stato verificato funzionante (gunicorn, port 19094, SQLite WAL) prima dell'esecuzione.

Scenari coperti dalla suite: health/auth (9), CR CRUD (8), XML + SEC-1 (4), workflow CRUD (8), agent cycle (5), pc-workflow (5), CR steps (3), checkin (2), version/download (3), security edge (4), DB integrità.

---

## Consigli per sviluppi futuri

### Priorità alta
- **CI/CD**: `.github/workflows/test.yml` con `pytest` su ogni push a `main`.
- **Secret bootstrap**: se `NOVASCM_API_KEY` è assente il server risponde 500. Generare chiave automatica al primo avvio e salvarla in `/data/.api_key`.
- **Rate limiter multi-worker**: `NOVASCM_RATE_LIMIT_STORAGE=redis://redis:6379/0`. Con `memory://` e 2 worker gunicorn il contatore non è condiviso.

### Priorità media
- **Test .NET**: xUnit per `StepExecutor.cs` e `Worker.cs`.
- **Logging strutturato**: `python-json-logger` per Grafana/Loki (homelab VLAN 20).
- **Paginazione `GET /api/cr`**: nessun `LIMIT` — su deployment grandi può restituire migliaia di righe.

### Priorità bassa
- `AGENT_VER` in `novascm-agent.py` è hardcoded. Considerare `agent/version.txt` letto a runtime.
- `delete_cr` elimina prima padre poi figli. Ordine corretto: figli prima del padre.
- `update_status` non controlla `rowcount` dopo UPDATE — no-op silenzioso su id inesistente.
