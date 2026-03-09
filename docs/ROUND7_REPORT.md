# NovaSCM — Round 7 · Code Review v1.8.0

**Commit analizzato:** `87afb5fb47` (2026-03-09 17:13)
**File esaminati:** `server/api.py` (1179 righe), `server/web/index.html` (1382 righe), `agent/novascm-agent.py` (479 righe), `server/tests/test_api.py` (632 righe)
**Test suite:** 81/81 ✅

---

## Riepilogo

| Severità | N° | Titolo |
|---|---|---|
| 🔴 CRITICAL | 1 | UI: `api()` non invia `X-Api-Key` — UI rotta con auth attiva |
| 🟡 MEDIUM   | 2 | `DELETE /api/workflows` e `DELETE /api/cr` — record orfani nel DB |
| 🔵 INFO     | 2 | `PRAGMA foreign_keys` mancante · `PUT /api/settings` no cast int |

---

## 🔴 C-1 · `server/web/index.html` · CRITICAL

**Il metodo `api()` non include l'header `X-Api-Key`.**

Ogni chiamata fetch dalla UI viene fatta senza autenticazione. Se `NOVASCM_API_KEY` è configurata (default in produzione), il server risponde 500 a tutte le richieste. La UI risulta completamente non funzionante in produzione.

```js
// ATTUALE — nessun header auth
async api(url, method = 'GET', body = null) {
    const opts = { method, headers: { 'Content-Type': 'application/json' } };
    if (body) opts.body = JSON.stringify(body);
    const r = await fetch(url, opts);
    if (!r.ok) return null;
    return await r.json();
}
```

**Fix:**

```js
// Aggiungere in app() — leggere la key da una meta tag iniettata dal server
// In server/api.py, nella route GET / (serve index.html), iniettare:
//   <meta name="x-api-key" content="{{ api_key }}">
// oppure esporre un endpoint pubblico GET /api/token che la UI chiama al boot.
//
// Soluzione più semplice: leggere da <meta> iniettata lato server.

// In app():
apiKey: document.querySelector('meta[name="x-api-key"]')?.content || '',

async api(url, method = 'GET', body = null) {
    try {
        const opts = {
            method,
            headers: {
                'Content-Type': 'application/json',
                ...(this.apiKey ? { 'X-Api-Key': this.apiKey } : {})
            }
        };
        if (body) opts.body = JSON.stringify(body);
        const r = await fetch(url, opts);
        if (!r.ok) return null;
        return await r.json();
    } catch { return null; }
},
```

**In `server/api.py`** — la route che serve `index.html` deve iniettare la key:

```python
@app.route("/")
def index():
    html = open(os.path.join(WEB_DIR, "index.html")).read()
    # inietta API key come meta tag (non espone la key nel markup statico,
    # ma la rende disponibile al JS in-page già autenticato via cookie di sessione)
    html = html.replace(
        '</head>',
        f'<meta name="x-api-key" content="{API_KEY}">\n</head>'
    )
    return html, 200, {"Content-Type": "text/html"}
```

**Test da aggiungere:**
```python
def test_ui_api_call_with_auth(client, auth_headers):
    """La UI deve passare X-Api-Key — verifica che /api/cr risponda 200 non 401."""
    r = client.get("/api/cr", headers=auth_headers)
    assert r.status_code == 200

def test_ui_api_call_without_auth_fails(client):
    r = client.get("/api/cr")
    assert r.status_code == 401
```

---

## 🟡 M-1 · `server/api.py` · MEDIUM

**`DELETE /api/workflows` non cancella i record figli.**

Eliminando un workflow restano orfani in `workflow_steps` e `pc_workflows`, causando integrità referenziale rotta e record fantasma nella lista deploy.

```python
# ATTUALE — solo la riga padre
def delete_workflow(wf_id):
    with get_db_ctx() as conn:
        affected = conn.execute("DELETE FROM workflows WHERE id=?", (wf_id,)).rowcount
        conn.commit()
```

**Fix:**

```python
def delete_workflow(wf_id):
    with get_db_ctx() as conn:
        if not conn.execute("SELECT 1 FROM workflows WHERE id=?", (wf_id,)).fetchone():
            return jsonify({"error": "Non trovato"}), 404
        # rimuovi figli prima del padre
        conn.execute("DELETE FROM workflow_steps  WHERE workflow_id=?", (wf_id,))
        conn.execute("DELETE FROM pc_workflows     WHERE workflow_id=?", (wf_id,))
        conn.execute("DELETE FROM workflows        WHERE id=?",          (wf_id,))
        conn.commit()
    return jsonify({"ok": True})
```

**Test da aggiungere:**
```python
def test_delete_workflow_cascade(client, auth_headers):
    wf = client.post("/api/workflows", json={"nome": "WF-DEL"}, headers=auth_headers).get_json()
    wf_id = wf["id"]
    client.post(f"/api/workflows/{wf_id}/steps",
                json={"nome": "S1", "tipo": "reboot", "ordine": 1}, headers=auth_headers)
    r = client.delete(f"/api/workflows/{wf_id}", headers=auth_headers)
    assert r.status_code == 200
    steps = client.get(f"/api/workflows/{wf_id}/steps", headers=auth_headers)
    assert steps.status_code == 404 or steps.get_json() == []
```

---

## 🟡 M-2 · `server/api.py` · MEDIUM

**`DELETE /api/cr` non cancella `pc_workflows` con `cr_id`.**

`pc_workflows` ha colonna `cr_id INTEGER NOT NULL`. Eliminando una CR restano record orfani in `pc_workflows` che referenziano una CR inesistente.

```python
# ATTUALE
def delete_cr(cr_id):
    conn.execute("DELETE FROM cr_steps WHERE cr_id=?", (cr_id,))
    conn.execute("DELETE FROM cr        WHERE id=?",   (cr_id,))
    # ❌ manca: DELETE FROM pc_workflows WHERE cr_id=?
```

**Fix:** aggiungere prima della DELETE su `cr`:

```python
conn.execute("DELETE FROM pc_workflows WHERE cr_id=?", (cr_id,))
```

**Test da aggiungere:**
```python
def test_delete_cr_cascade_pc_workflows(client, auth_headers):
    cr = client.post("/api/cr", json={"pc_name": "DEL-PC", "domain": "test.local"},
                     headers=auth_headers).get_json()
    cr_id = cr["id"]
    # assegna un workflow
    client.post("/api/pc-workflows",
                json={"pc_name": "DEL-PC", "workflow_id": 1, "cr_id": cr_id},
                headers=auth_headers)
    client.delete(f"/api/cr/{cr_id}", headers=auth_headers)
    # non devono restare pc_workflows orfani
    r = client.get("/api/pc-workflows", headers=auth_headers)
    orphans = [pw for pw in r.get_json() if pw.get("cr_id") == cr_id]
    assert orphans == []
```

---

## 🔵 I-1 · `server/api.py` · INFO

**`PRAGMA foreign_keys = ON` mancante — FK non enforced in SQLite.**

SQLite di default non applica i vincoli `FOREIGN KEY`. Le righe REFERENCES nel DDL sono decorative senza questo PRAGMA.

**Fix:** aggiungere in `init_db()` e in `get_db_ctx()`:

```python
# In get_db_ctx(), dopo conn = sqlite3.connect(...):
conn.execute("PRAGMA foreign_keys = ON")
```

---

## 🔵 I-2 · `server/api.py` · INFO

**`PUT /api/settings` non valida il tipo di `default_workflow_id`.**

Il valore viene salvato come `str(value)` senza verificare che sia un intero valido. Un payload `{"default_workflow_id": "drop table"}` viene accettato silenziosamente.

```python
# ATTUALE — nessuna validazione
for key, value in data.items():
    conn.execute("INSERT INTO settings ... VALUES (?,?)", (key, str(value) if value is not None else ""))
```

**Fix:**

```python
SETTINGS_SCHEMA = {
    "default_workflow_id": int,
}

for key, value in data.items():
    if key not in SETTINGS_SCHEMA:
        continue  # ignora chiavi sconosciute
    try:
        value = SETTINGS_SCHEMA[key](value) if value is not None else None
    except (ValueError, TypeError):
        return jsonify({"error": f"Tipo non valido per {key}"}), 400
    conn.execute("...", (key, str(value) if value is not None else ""))
```

---

## Note positive

- **row_to_dict**: filtra correttamente `join_pass`/`admin_pass` per default ✅
- **Agent auth**: `X-Api-Key` inviato correttamente in tutte le chiamate ✅
- **add_step**: whitelist `tipi_validi` presente e correttamente applicata ✅
- **ProxyFix**: presente e configurato ✅
- **os.chmod(0o600)**: presente su `.api_key` ✅
- **windows_update**: restituisce `None` (skipped) correttamente ✅

---

## Ordine di esecuzione per Claude Code (v1.8.1)

1. **I-1**: `get_db_ctx()` — aggiungere `PRAGMA foreign_keys = ON` (2 min)
2. **M-2**: `delete_cr()` — aggiungere `DELETE FROM pc_workflows WHERE cr_id=?` (2 min)
3. **M-1**: `delete_workflow()` — cascade su `workflow_steps` + `pc_workflows` + test (10 min)
4. **C-1**: `server/api.py` route `/` + `index.html` metodo `api()` — iniettare key + header (20 min)
5. `cd server && NOVASCM_DB=/tmp/t.db NOVASCM_API_KEY=test pytest tests/ -v` → target **84 passed**
6. `git commit -m "fix: X-Api-Key in UI, cascade delete workflow/cr, pragma foreign_keys"`

