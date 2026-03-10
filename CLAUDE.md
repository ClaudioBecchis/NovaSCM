# NovaSCM — CLAUDE.md

## Versione corrente
**v2.1.0** (target) — base: commit `3b79018` (v2.0.0-alpha.1)
Test suite: 112/112 ✅ · Round 8 + v2.1.0 features + test v2.1.0 completati

### Fix aggiuntivo v2.1.0
- `PUT /api/settings` ora rifiuta (400) chiavi non in `SETTINGS_SCHEMA` (whitelist)

---

## Bug aperti — Round 8 (v2.0.0-alpha.1)

### 🔴 C-1 · `server/api.py` riga 598 — CRITICAL ✅ FIXATO
`@require_auth` aggiunto a `report_step`

### 🔴 C-2 · `server/api.py` riga 618 — CRITICAL ✅ FIXATO
`@require_auth` aggiunto a `get_steps_by_name`

### 🔴 C-3 · `server/api.py` riga 1621 — CRITICAL ✅ FIXATO
`@require_auth` aggiunto a `download_deploy_screen`

### 🟡 M-1 · `server/api.py` riga 371 — MEDIUM ✅ FIXATO
`delete_cr` ora elimina anche `pc_workflows` per `pc_name`

### 🟡 M-2 · `server/version.json` — MEDIUM ✅ FIXATO
IP privato rimosso, `url: ""`

### 🔵 I-1 · `ApiClient.cs` + `api.py` — INFO ✅ FIXATO
`elapsed_sec` misurato in Worker.cs, inviato da ApiClient, salvato in api.py

### 🔵 I-2 · `Worker.cs` riga 74 — INFO ✅ FIXATO
API key passata via `EnvironmentVariables["NOVASCM_API_KEY"]` invece di arg CLI

### 🔵 I-3 · `test_api.py` riga 661 — INFO ✅ FIXATO
Test usa endpoint corretto `/api/cr/by-name/STEP-PC/step`

**NOTA C-1/C-2:** endpoint `report_step` e `get_steps_by_name` ora richiedono auth.
Il `postinstall.ps1` su CT110 (`/var/www/html/postinstall.ps1`) deve passare X-Api-Key.
Aggiornare il file con l'API key del server NovaSCM.

---

## Bug chiusi — Round 7

### 🔴 C-1 · `server/web/index.html` + `server/api.py` — CRITICAL
**UI rotta in produzione: `api()` non invia `X-Api-Key`.**

- `index.html` metodo `api()`: aggiungere `'X-Api-Key': this.apiKey` agli header fetch
- `index.html` in `app()`: leggere `apiKey` da `<meta name="x-api-key">`
- `server/api.py` route `/`: iniettare `<meta name="x-api-key" content="{API_KEY}">` nell'HTML
- Test: `test_ui_api_call_with_auth`, `test_ui_api_call_without_auth_fails`

### 🟡 M-1 · `server/api.py` — MEDIUM
**`DELETE /api/workflows` non cancella `workflow_steps` e `pc_workflows` figli.**

```python
# Aggiungere in delete_workflow():
conn.execute("DELETE FROM workflow_steps  WHERE workflow_id=?", (wf_id,))
conn.execute("DELETE FROM pc_workflows     WHERE workflow_id=?", (wf_id,))
```
- Test: `test_delete_workflow_cascade`

### 🟡 M-2 · `server/api.py` — MEDIUM
**`DELETE /api/cr` non cancella `pc_workflows` con `cr_id`.**

```python
# Aggiungere in delete_cr() prima della DELETE su cr:
conn.execute("DELETE FROM pc_workflows WHERE cr_id=?", (cr_id,))
```
- Test: `test_delete_cr_cascade_pc_workflows`

### 🔵 I-1 · `server/api.py` — INFO
**`PRAGMA foreign_keys = ON` mancante in `get_db_ctx()`.**

```python
conn.execute("PRAGMA foreign_keys = ON")
```

### 🔵 I-2 · `server/api.py` — INFO
**`PUT /api/settings` non valida tipo `default_workflow_id`.**
Aggiungere `SETTINGS_SCHEMA` con cast e whitelist chiavi. Vedere ROUND7_REPORT.md.

---

## Ordine esecuzione per v1.8.1

1. I-1: `get_db_ctx()` PRAGMA foreign_keys = ON
2. M-2: `delete_cr()` cascade pc_workflows
3. M-1: `delete_workflow()` cascade + test
4. C-1: route `/` inject meta + `api()` X-Api-Key + test
5. `cd server && NOVASCM_DB=/tmp/t.db NOVASCM_API_KEY=test pytest tests/ -v` → 84 passed
6. `git commit -m "fix: X-Api-Key in UI, cascade delete workflow/cr, pragma foreign_keys"`

---

<details>
<summary>Round 6 — v1.7.9 (COMPLETATO ✅)</summary>

- M-1: `row_to_dict` filtra `join_pass`/`admin_pass` ✅
- I-1: `os.chmod(0o600)` su `.api_key` ✅
- I-2: `windows_update` → None skipped ✅
- 81/81 test ✅

</details>

<details>
<summary>Round 5 — v1.7.8 (COMPLETATO ✅)</summary>

- M-1: `api_key` rimossa da installer PS1+SH ✅
- M-2: ProxyFix + NOVASCM_PUBLIC_URL ✅
- I-1: `set -euo pipefail` in install-linux.sh ✅
- I-2: `windows_update` skip su Linux ✅

</details>

---

## Architettura

- **Server:** Flask + SQLite · `server/api.py`
- **UI:** Alpine.js SPA · `server/web/index.html`
- **Agent Python:** `agent/novascm-agent.py`
- **Agent .NET:** `NovaSCMAgent/` (Windows)
- **Installer:** `agent/install-windows.ps1`, `agent/install-linux.sh`
- **Network:** 192.168.20.110:9091 · VLAN20 Servers · Pi-hole DNS 192.168.20.253

## Consigli futuri
- Paginazione `GET /api/cr` (crescita tabella prevedibile)
- Alert UI quando agent offline da >5 min
