# CLAUDE.md — NovaSCM
> **Leggi questo file per intero prima di modificare qualsiasi file del progetto.**  
> Aggiornato al commit `f9d6150` (v1.7.8) — 09/03/2026  
> Round 6 completato — 3 bug identificati, nessuno critico

---

## Stato attuale

| | |
|---|---|
| Versione | **v1.7.8** — commit `f9d6150` |
| Test suite | **79/79 ✅** |
| Bug aperti | **3** (0 critici, 1 medium, 2 info) |
| Ultimo round | Round 6 — analisi statica su v1.7.8 |

**Comando test — eseguilo sempre dopo ogni modifica:**
```bash
cd server
NOVASCM_DB=/tmp/t.db NOVASCM_API_KEY=test-key python -m pytest tests/ -v
```
Risultato atteso: `79 passed`.

---

## Bug aperti — Round 6

### M-1 · `server/api.py` · MEDIUM
**`GET /api/cr` e `GET /api/cr/<id>` espongono `join_pass` e `admin_pass` in chiaro nel JSON**

`row_to_dict()` serializza tutte le colonne della tabella `cr` senza filtrare i campi sensibili. Chiunque abbia la `X-Api-Key` può fare `GET /api/cr` e ricevere tutte le password di dominio (`join_pass`) e dell'amministratore locale (`admin_pass`) in chiaro.

**Fix** — modifica `row_to_dict()` per escludere i campi sensibili dalla risposta:

Trova:
```python
def row_to_dict(row):
    d = dict(row)
    d["software"] = json.loads(d.get("software") or "[]")
    return d
```
Sostituisci con:
```python
_SENSITIVE = {"join_pass", "admin_pass"}

def row_to_dict(row, include_sensitive: bool = False):
    d = dict(row)
    d["software"] = json.loads(d.get("software") or "[]")
    if not include_sensitive:
        for k in _SENSITIVE:
            d.pop(k, None)
    return d
```
Poi nella sola funzione `get_autounattend()`, dove le password servono per generare il XML, usa `row_to_dict(row, include_sensitive=True)`.

Aggiungi un test in `test_api.py`:
```python
def test_get_cr_does_not_expose_passwords(self, client):
    cr_id = _create_cr(client).get_json()["id"]
    r = client.get(f"/api/cr/{cr_id}", headers=AUTH)
    d = r.get_json()
    assert "join_pass" not in d
    assert "admin_pass" not in d
```

---

### I-1 · `server/api.py` · INFO
**File `.api_key` creato senza permessi restrittivi (world-readable)**

Quando `NOVASCM_API_KEY` non è impostata, il server genera una chiave e la salva in `.api_key` con `open(..., "w")`. Il file eredita la umask del processo (tipicamente 022 → leggibile da tutti). Su bare metal o VM multi-utente chiunque sul sistema può leggere la chiave API.

**Fix** — dopo la scrittura del file, aggiungi `os.chmod`:

Trova (dentro il blocco `if not API_KEY:`):
```python
            with open(_key_file, "w") as _f:
                _f.write(API_KEY)
            log.warning("NOVASCM_API_KEY non impostata — generata e salvata in %s", _key_file)
```
Sostituisci con:
```python
            with open(_key_file, "w") as _f:
                _f.write(API_KEY)
            os.chmod(_key_file, 0o600)
            log.warning("NOVASCM_API_KEY non impostata — generata e salvata in %s", _key_file)
```

---

### I-2 · `agent/novascm-agent.py` · INFO
**`windows_update` su Windows con agent Python restituisce `False` (errore) invece di `None` (skipped)**

Riga 324: se il Python agent gira su Windows e riceve uno step `windows_update`, il risultato è `False` (step fallito) invece di `None` (skipped). Il workflow si blocca con errore invece di procedere al passo successivo. Il caso è raro (su Windows si usa l'agent .NET), ma è inconsistente con il comportamento Linux.

**Fix** — in `agent/novascm-agent.py`, funzione `esegui_step()`:

Trova:
```python
    elif tipo == "windows_update":
        # I-2: windows_update è implementato solo nell'agent .NET (Windows)
        # Su Linux restituisce skipped (None) per coerenza con StepExecutor.cs
        if not IS_WINDOWS:
            return None, "Skipped: windows_update è supportato solo su Windows (agent .NET)"
        return False, "windows_update non supportato dall'agent Python su Windows — usa l'agent .NET"
```
Sostituisci con:
```python
    elif tipo == "windows_update":
        # windows_update è implementato solo nell'agent .NET
        # Entrambe le piattaforme restituiscono skipped (None)
        return None, "Skipped: windows_update richiede l'agent .NET (NovaSCMAgent.exe)"
```

---

## Ordine di esecuzione consigliato (v1.7.9)

1. **I-2** — `agent/novascm-agent.py`: `windows_update` → `None` su Windows (2 min)
2. **I-1** — `server/api.py`: `os.chmod(_key_file, 0o600)` (2 min)
3. **M-1** — `server/api.py`: `row_to_dict` filtra `join_pass`/`admin_pass` + 1 test (15 min)
4. `cd server && NOVASCM_DB=/tmp/t.db NOVASCM_API_KEY=test pytest tests/ -v` → verifica `80 passed`
5. `git commit -m "fix: row_to_dict nasconde password, api_key chmod 600, windows_update skipped"`

---

## Regole — cosa NON toccare

### Autenticazione
- `/health` è **senza** `@require_auth` — necessario per Docker healthcheck (v1.7.5). Non aggiungere `@require_auth`.
- Tutti gli altri endpoint hanno `@require_auth`.

### Sicurezza già fixata
- `get_autounattend()` usa `xml.sax.saxutils.escape()` su tutti i valori XML (SEC-1, v1.7.3).
- Installer generati dall'API: no `Invoke-Expression`, no `curl|bash`, download in temp + SHA256 (SEC-2).
- `NOVASCM_API_KEY` con secret bootstrap automatico da `/data/.api_key` (v1.7.8).
- `ProxyFix` + `NOVASCM_PUBLIC_URL` per URL pubblico corretto dietro reverse proxy (v1.7.8).
- `_validate_api_url()` nell'agent Python blocca SSRF.

### .NET Agent
- `ApiClient.cs` — `X-Api-Key` per-request in `BuildRequest()`, non in `DefaultRequestHeaders`.
- `Worker.cs` — resume verifica `PwId` vs workflow corrente (v1.7.3).
- `NovaSCMApiService.cs` — `ApiBase` via `Uri.Authority`.
- `SendAsync` / `DownloadExeAsync` usano `using var resp`.

### Python Agent
- `run_workflow()` verifica `pw_id` salvato vs workflow corrente (v1.7.6). Non rimuovere.
- Tutti i comandi usano `shell=False` con lista argomenti.

### Installer
- `install-windows.ps1`: accetta `-ApiKey` e lo scrive in `agent.json` (v1.7.8).
- `install-linux.sh`: accetta `--api-key`, usa `set -euo pipefail`, SHA256 check (v1.7.8).
- Installer dinamici (`/api/download/agent-install.ps1` e `.sh`): includono `api_key` (v1.7.8).

### Server
- `report_wf_step()` conta `done + skipped + error` per completare il workflow (v1.7.3).
- `update_step()` valida `tipo` con whitelist (v1.7.3).
- `init_db()` crea 4 indici. Non rimuoverli.
- `delete_cr()` elimina prima `cr_steps` poi `cr` (ordine corretto, v1.7.8).

---

## Storico fix (Round 1–5, tutti completati)

<details>
<summary>Round 1 — v1.7.2 (13 fix)</summary>
path NSSM, placeholder IP, tipo PollSec, VOLUME Dockerfile, API key env var, systemd hardening, Inno Setup path, requirements.txt, Docker resource limits, NSSM 2.24 + RMDir NSIS, versione da Assembly, null check workflow_nome, pubxml R2R. Rimossi ~259MB binari.
</details>

<details>
<summary>Round 2 — v1.7.3 (3 SEC + 10 BUG + 2 INFO)</summary>
SEC-1: XML escape. SEC-2: pipe-to-shell → temp+SHA256. BUG-1..10: whitelist tipo, nome vuoto, completamento workflow, init_db exception, indici SQLite, SetApiKey race condition, DI inutilizzato, resume PwId, ApiBase, EnsureSuccessStatusCode. INFO-1: rate limiter env. INFO-2: cache config mtime.
</details>

<details>
<summary>Round 3 — v1.7.4 (C-7 SHA256 installer)</summary>
SHA256 per agent installer + endpoint GET /api/download/agent.sha256.
</details>

<details>
<summary>Round 4 — v1.7.5 + v1.7.6</summary>
v1.7.5: rimosso @require_auth da /health (Docker loop), test 78/78, DownloadExeAsync, HttpResponseMessage using.
v1.7.6: novascm-agent.py pw_id check, StepExecutor @catArgs, SHA256 installers statici.
</details>

<details>
<summary>Round 5 — v1.7.7 + v1.7.8</summary>
v1.7.7: CI/CD GitHub Actions (pytest + xUnit), secret bootstrap, Redis rate limiter, paginazione cr_steps, xUnit .NET, agent/version.txt.
v1.7.8: api_key in installers (M-1), NOVASCM_PUBLIC_URL + ProxyFix (M-2), set -euo pipefail linux installer (I-1), windows_update skipped Linux (I-2).
</details>

---

## Deploy test Round 6 (79/79 via pytest ✅)

Suite pytest eseguita su v1.7.8 (Flask test client). Tutti i 79 test passano.

Nuovi test in v1.7.7/v1.7.8: `test_get_steps_pagination` (paginazione cr_steps con `?page=&per_page=`).

---

## Consigli per sviluppi futuri

### Priorità alta
- **Alert agent offline**: `last_seen` viene aggiornato ad ogni checkin ma non c'è endpoint né logica che segnali un agent silente da più di N minuti. Utile per monitoraggio homelab.
- **GET /api/cr senza LIMIT**: la query `SELECT * FROM cr ORDER BY id DESC` non ha paginazione. Su deployment con molti PC può diventare lenta. Aggiungere `?page=&per_page=` come già fatto per `cr_steps`.

### Priorità media
- **autounattend.xml**: le credenziali di dominio (`join_pass`) sono in chiaro nel XML — è by design (formato Windows) ma il file dovrebbe essere scaricabile solo da chi ha la `X-Api-Key`. Attualmente è già protetto da `@require_auth`. Documentare esplicitamente che il XML non va esposto pubblicamente.
- **Logging strutturato**: `python-json-logger` per Grafana/Loki (homelab VLAN 20).

### Priorità bassa
- `update_status` ora controlla `rowcount` (fixato in v1.7.8). ✅
- `delete_cr` ora elimina figli prima del padre. ✅
