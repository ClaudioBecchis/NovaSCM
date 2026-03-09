# CLAUDE.md — NovaSCM
> **Leggi questo file per intero prima di modificare qualsiasi file del progetto.**  
> Aggiornato al commit `5802c44` (v1.7.6) — 09/03/2026

---

## Cos'è questo progetto

**NovaSCM** è un sistema di provisioning automatico per PC Windows/Linux.  
Un server Flask (`server/api.py`) espone un'API REST. I PC client girano un agent
(Python `agent/novascm-agent.py` oppure .NET `NovaSCMAgent/`) che fa polling,
esegue workflow step-by-step e riporta lo stato. Una GUI WPF (.NET) permette agli
admin di creare Change Request, assegnare workflow e scaricare `autounattend.xml`
per il deploy PXE.

**Stack:** Flask + SQLite + gunicorn (server) · Python agent (Linux) · .NET Worker Service (Windows) · WPF (GUI admin) · Docker + docker-compose

**Rete homelab:** VLAN 20 `192.168.20.x` — il server gira su questa subnet (es. `192.168.20.110:9091`), i PC client su VLAN 10 la raggiungono via UCG-Fiber.

---

## Stato attuale

| | |
|---|---|
| Versione | **v1.7.6** — commit `5802c44` |
| Test suite | **78/78 ✅** |
| Deploy test | **35/35 ✅** (gunicorn live, vedi Sezione 5) |
| Bug aperti | **0** — tutti i Round 1–4 chiusi |
| Ultimo round | Round 4 — 5 fix applicati in v1.7.6 |

**Comando test — eseguilo sempre dopo ogni modifica:**
```bash
cd server
NOVASCM_DB=/tmp/t.db NOVASCM_API_KEY=test-key python -m pytest tests/ -v
```
Risultato atteso: `78 passed`. Se fallisce anche un solo test, non committare.

---

## File principali

| File | Ruolo |
|---|---|
| `server/api.py` | API Flask — 1043 righe, 38 endpoint REST |
| `server/tests/test_api.py` | Suite pytest 78 test |
| `server/Dockerfile` | Immagine Python 3.12-slim + gunicorn |
| `server/docker-compose.yml` | Deploy con healthcheck su `/health` |
| `server/requirements.txt` | flask, gunicorn, flask-limiter |
| `agent/novascm-agent.py` | Agent Python (Linux) |
| `agent/install-linux.sh` | Installer Linux con verifica SHA256 |
| `agent/install-windows.ps1` | Installer Windows con verifica SHA256 |
| `NovaSCMAgent/Worker.cs` | Agent .NET — loop polling + resume reboot |
| `NovaSCMAgent/ApiClient.cs` | HTTP client per-request thread-safe |
| `NovaSCMAgent/StepExecutor.cs` | Esecutore step (winget, apt, reg, reboot...) |
| `NovaSCMAgent/AgentConfig.cs` | Config + stato persistente (resume) |
| `NovaSCMApiService.cs` | Client HTTP WPF → API |
| `installer/NovaSCM.iss` | Inno Setup installer GUI |

---

## Regole — cosa NON toccare

Questi problemi sono stati già corretti. Non re-introdurli.

### Sicurezza
- `server/api.py` — `get_autounattend()` usa `xml.sax.saxutils.escape()` su tutti i valori nel XML (SEC-1, v1.7.3).
- `server/api.py` — installer generati dall'API scaricano in temp + verificano SHA256, no `Invoke-Expression` / `curl|bash` (SEC-2, v1.7.3).
- `server/api.py` — `NOVASCM_API_KEY` sempre da env var, mai hardcoded (v1.7.2).
- `agent/novascm-agent.py` — `_validate_api_url()` blocca SSRF su metadata service.

### Autenticazione
- `/health` è **senza** `@require_auth` — necessario per il Docker healthcheck (C-1, v1.7.5). Non rimetterci il decorator.
- Tutti gli altri endpoint hanno `@require_auth`.

### .NET Agent
- `ApiClient.cs` — `X-Api-Key` aggiunto per-request in `BuildRequest()`, non in `DefaultRequestHeaders` (race condition, BUG-6, v1.7.3).
- `Worker.cs` — resume dopo reboot verifica `PwId` salvato vs workflow corrente (BUG-8, v1.7.3).
- `NovaSCMApiService.cs` — `ApiBase` calcolato con `Uri.Authority` (BUG-9, v1.7.3).
- `NovaSCMApiService.cs` — `SendAsync` e `DownloadExeAsync` usano `using var resp` (M-3, v1.7.5).

### Python Agent
- `agent/novascm-agent.py` — `run_workflow()` verifica `pw_id` salvato vs workflow corrente (M-1, v1.7.6).
- Tutti i comandi usano `shell=False` con lista argomenti — non usare stringhe interpolate con `shell=True`.

### Server
- `report_wf_step()` conta `done + skipped + error` per completare il workflow (BUG-3, v1.7.3).
- `update_step()` valida `tipo` con whitelist (BUG-1, v1.7.3).
- `init_db()` crea 4 indici (v1.7.3–v1.7.4). Non rimuoverli.

---

## Storico fix (Round 1–4)

<details>
<summary>Round 1 — v1.7.2 (13 fix)</summary>

- FIX-1: `$NssmExe` usato prima di essere dichiarato in `install-windows.ps1`
- FIX-2: IP homelab placeholder in commenti
- FIX-3: `$PollSec` tipizzato come `[int]`
- FIX-4: Dockerfile — `VOLUME` dopo `RUN chown`, non prima
- FIX-5: `NOVASCM_API_KEY` da env var
- FIX-6: systemd hardening agent Linux
- FIX-7: Inno Setup path relativi
- FIX-8: `requirements.txt` aggiunto
- FIX-9: Docker resource limits
- FIX-10: NSSM version + `RMDir /r` NSIS
- FIX-11: `Worker.cs` versione da `Assembly`
- FIX-12: `Worker.cs` null check su `workflow_nome`
- FIX-13: `pubxml` conflitto R2R/compression rimosso
- Rimossi ~259MB binari da Git

</details>

<details>
<summary>Round 2 — v1.7.3 (3 SEC + 10 BUG + 2 INFO)</summary>

- SEC-1: XML injection → `xml.sax.saxutils.escape()`
- SEC-2: pipe-to-shell RCE negli installer
- SEC-3: `/health` con `@require_auth` (poi rimosso in v1.7.5)
- BUG-1: `update_step()` mancava whitelist `tipo`
- BUG-2: `update_workflow()` accettava nome vuoto
- BUG-3: `report_wf_step()` non completava su step `error`
- BUG-4: `init_db()` warning per errori non-duplicate
- BUG-5: indici SQLite aggiunti
- BUG-6: `SetApiKey()` race condition su `DefaultRequestHeaders`
- BUG-7: `AddSingleton<AgentConfig>()` rimosso
- BUG-8: `Worker.cs` resume verifica `PwId`
- BUG-9: `ApiBase` via `Uri.Authority`
- BUG-10: `EnsureSuccessStatusCode` → controllo manuale
- INFO-1: rate limiter via env `NOVASCM_RATE_LIMIT_STORAGE`
- INFO-2: cache config con mtime in Python agent

</details>

<details>
<summary>Round 3 — v1.7.4 + v1.7.5 (1 CRIT + 3 MED)</summary>

- C-1 (CRITICO): `/health` con `@require_auth` → Docker healthcheck loop
- M-1: `TestHealth` aggiornati, 78/78 ripristinato
- M-2: `DownloadExeAsync` usava ancora `EnsureSuccessStatusCode()`
- M-3: `HttpResponseMessage` resource leak in `SendAsync`
- C-7: SHA256 agent + endpoint `/api/download/agent.sha256`

</details>

<details>
<summary>Round 4 — v1.7.6 (2 MED + 2 INFO + 1 COS)</summary>

- M-1: `novascm-agent.py` — `run_workflow()` verifica `pw_id` (allineato a `Worker.cs`)
- M-2: `StepExecutor.cs` — `WindowsUpdate()` usa `$catArgs` invece di `$criteria` dead code
- I-1: `install-windows.ps1` — step 3 verifica SHA256
- I-2: `install-linux.sh` — step 5 verifica SHA256 + rinumerazione
- C-1: numerazione commenti (risolto da I-1)

</details>

---

## Deploy test — Round 4 (35/35 ✅)

Eseguiti su gunicorn 2 worker, porta 19092, SQLite WAL.

| Fase | Scenario | Esito |
|---|---|---|
| Health | GET /health senza auth → 200 | ✅ |
| Health | GET /health con auth → 200 | ✅ |
| Health | Body: nessun db path esposto | ✅ |
| Auth | No key → 401 | ✅ |
| Auth | Wrong key → 401 | ✅ |
| Auth | Correct key → 200 | ✅ |
| CR | POST → id=1 status=open | ✅ |
| CR | Duplicato → 409 | ✅ |
| CR | GET by-id → 200 | ✅ |
| CR | GET by-name → POLARIS-PC01 | ✅ |
| CR | PUT status cr inesistente → 404 | ✅ |
| XML | autounattend.xml generato | ✅ |
| XML | &amp; / &lt; / &gt; escaped (SEC-1) | ✅ |
| XML | Package injection sanitizzato | ✅ |
| Workflow | Creato → id=1 | ✅ |
| Workflow | 4 step aggiunti (201 ciascuno) | ✅ |
| Workflow | Tipo invalido → 400 | ✅ |
| Workflow | Ordine duplicato → 409 | ✅ |
| Agent | Assegnato → pending | ✅ |
| Agent | GET workflow → running | ✅ |
| Agent | 4 step ricevuti | ✅ |
| Agent | done / skipped / done / done | ✅ |
| Agent | Workflow → completed | ✅ |
| Settings | 3 chiavi salvate | ✅ |
| Settings | Auto-assign default workflow | ✅ |
| Lifecycle | CR → in_progress → completed | ✅ |
| Lifecycle | Stato invalido → 400 | ✅ |
| Lifecycle | completed_at impostato | ✅ |
| Checkin | CR heartbeat → ok=True | ✅ |
| Steps | 5 step CR classici tracciati | ✅ |
| DB | 84KB su disco | ✅ |
| DB | 4 indici creati | ✅ |
| Cleanup | DELETE CR → 200 | ✅ |
| Cleanup | DELETE CR inesistente → 404 | ✅ |
| Cleanup | Nessun cr_steps orfano | ✅ |

---

## Consigli per sviluppi futuri

### Priorità alta

- **CI/CD**: aggiungere `.github/workflows/test.yml` che esegue `pytest` su ogni push a `main`.
- **Secret bootstrap**: se `NOVASCM_API_KEY` è assente il server risponde 500 a tutte le richieste. Generare automaticamente una chiave sicura al primo avvio e scriverla in `/data/.api_key`.
- **Rate limiter multi-worker**: `NOVASCM_RATE_LIMIT_STORAGE=redis://redis:6379/0` nel `docker-compose.yml`. Con `memory://` e 2 worker gunicorn il contatore non è condiviso.

### Priorità media

- **Test .NET**: aggiungere unit test con xUnit per `StepExecutor.cs` e `Worker.cs`. Attualmente solo il server Python ha copertura.
- **Logging strutturato**: passare a `python-json-logger` sul server per facilitare parsing con Grafana/Loki (homelab VLAN 20).
- **Paginazione `/api/cr/<id>/steps`**: la query non ha `LIMIT`. Su deployment grandi può restituire migliaia di righe.

### Priorità bassa

- `AGENT_VER` in `agent/novascm-agent.py` è hardcoded `"1.0.0"`. Considerare un file `agent/version.txt` letto a runtime.
- `delete_cr` elimina prima `cr` poi `cr_steps`. L'ordine corretto sarebbe inverso (figli prima del padre), anche se SQLite lo rende atomico.
- `update_status` esegue `UPDATE` + `COMMIT` anche se `cr_id` non esiste. Considerare `rowcount` check prima del commit.
