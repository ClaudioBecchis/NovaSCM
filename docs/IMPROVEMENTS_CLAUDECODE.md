# NovaSCM — Roadmap Miglioramenti v2.1.0
## Report per Claude Code

**Data:** 2026-03-10  
**Repository:** https://github.com/ClaudioBecchis/NovaSCM  
**Commit base:** `3b79018` (v2.0.0-alpha.1)  
**Stack:** Python/Flask + SQLite · C# .NET 8 (Agent) · Vanilla JS (Web UI)

---

## Contesto

NovaSCM è un sistema di deployment/configurazione PC basato su CR (Configuration Request), workflow a step, agente C# e server Flask. Le feature descritte di seguito sono migliorie prioritarie identificate dopo l'analisi Round 8. Sono ordinate per impatto/effort.

---

## FEATURE-1 — Notifiche deploy completato (webhook)

### Obiettivo
Inviare una notifica HTTP quando un `pc_workflow` cambia stato a `completed` o `error`.

### Implementazione lato server (`server/api.py`)

**1. Nuova impostazione in `_PXE_SETTINGS_DEFAULTS` (o impostazione generale):**
```python
# Aggiungere alle settings generali (non PXE)
GENERAL_SETTINGS_DEFAULTS = {
    "webhook_url":     "",   # URL HTTP POST da chiamare
    "webhook_enabled": "0",
}
```

**2. Helper `_fire_webhook(event, data)` da chiamare dopo ogni cambio stato:**
```python
def _fire_webhook(event: str, data: dict):
    """Chiama webhook configurato in modo fire-and-forget (thread separato)."""
    import threading, urllib.request
    with get_db_ctx() as conn:
        row = conn.execute("SELECT value FROM settings WHERE key='webhook_url'").fetchone()
        url = row["value"] if row else ""
        en  = conn.execute("SELECT value FROM settings WHERE key='webhook_enabled'").fetchone()
        enabled = (en["value"] if en else "0") == "1"
    if not url or not enabled:
        return
    payload = json.dumps({"event": event, "ts": datetime.datetime.utcnow().isoformat(), **data}).encode()
    def _send():
        try:
            req = urllib.request.Request(url, data=payload,
                                         headers={"Content-Type": "application/json"}, method="POST")
            urllib.request.urlopen(req, timeout=5)
        except Exception as exc:
            log.warning("Webhook failed: %s", exc)
    threading.Thread(target=_send, daemon=True).start()
```

**3. Chiamata in `report_wf_step` quando lo stato cambia a `completed` o `error`:**
```python
# Dopo conn.execute("UPDATE pc_workflows SET status='completed' ...")
_fire_webhook("workflow_completed", {
    "pc_name":  pc_name,
    "pw_id":    pw_id,
    "status":   "completed",
})
# Analogamente per "error" (step con su_errore=stop)
```

**Payload webhook (JSON):**
```json
{
  "event":   "workflow_completed",
  "ts":      "2026-03-10T14:32:00",
  "pc_name": "PC-SALA01",
  "pw_id":   42,
  "status":  "completed"
}
```

**Compatibilità target:** ntfy (`https://ntfy.sh/TOPIC`), Gotify, Telegram bot, webhook Discord/Slack, n8n.

### Impostazioni UI
Aggiungere nella sezione Impostazioni della web UI (`server/web/index.html`) due campi:
- `webhook_url` — input text, label "Webhook URL notifiche"
- `webhook_enabled` — toggle, label "Abilita notifiche"

---

## FEATURE-2 — Dashboard realtime (polling UI)

### Obiettivo
La web UI aggiorna automaticamente la lista dei deploy attivi senza ricaricare la pagina.

### Implementazione (`server/web/index.html`)

**Aggiungere metodo `startPolling()` nell'app Vue/Alpine:**
```javascript
startPolling() {
    // Polling ogni 5s solo se ci sono workflow running
    this._pollTimer = setInterval(async () => {
        const active = this.pcWorkflows.filter(pw => pw.status === 'running');
        if (active.length === 0) return;
        const fresh = await this.api('/api/pc-workflows');
        if (fresh) {
            // Aggiorna solo i record modificati (evita re-render completo)
            fresh.forEach(pw => {
                const idx = this.pcWorkflows.findIndex(x => x.id === pw.id);
                if (idx >= 0 && JSON.stringify(this.pcWorkflows[idx]) !== JSON.stringify(pw)) {
                    this.pcWorkflows.splice(idx, 1, pw);
                }
            });
        }
    }, 5000);
},
stopPolling() {
    clearInterval(this._pollTimer);
},
```

**Avviare/fermare il polling al mount/unmount della vista "Deploy".**

**Indicatore visivo:** aggiungere un badge "● LIVE" nella toolbar quando ci sono workflow `running`.

### Endpoint già esistente
`GET /api/pc-workflows` restituisce già tutti i pc_workflow con stato — nessuna modifica lato server necessaria.

---

## FEATURE-3 — `su_errore=retry` con tentativi configurabili

### Obiettivo
Aggiungere la modalità `retry` agli step del workflow: se uno step fallisce, l'agente lo riprova N volte prima di fermarsi.

### Schema DB — nessuna modifica
Il campo `su_errore` in `workflow_steps` è già TEXT — i valori validi diventano: `stop`, `continue`, `retry`.

### Validazione server (`server/api.py`)
```python
# In add_step e update_step — aggiornare la validazione
SU_ERRORE_VALID = ("stop", "continue", "retry")

# In add_step:
if data.get("su_errore", "stop") not in SU_ERRORE_VALID:
    return jsonify({"error": f"su_errore non valido. Valori: {SU_ERRORE_VALID}"}), 400
```

**Nuovo campo opzionale `retry_max` in `parametri` dello step** (default: 3):
```json
{
  "tipo": "winget_install",
  "su_errore": "retry",
  "parametri": { "id": "Mozilla.Firefox", "retry_max": 3, "retry_delay_sec": 10 }
}
```

### Implementazione agente (`NovaSCMAgent/Worker.cs`)
```csharp
// In RunWorkflowAsync, sostituire il blocco esecuzione step:

int retryMax = 1;
int retryDelaySec = 10;
if (suErr == "retry") {
    var p = JsonNode.Parse(step["parametri"]?.GetValue<string>() ?? "{}")?.AsObject();
    retryMax = p?["retry_max"]?.GetValue<int>() ?? 3;
    retryDelaySec = p?["retry_delay_sec"]?.GetValue<int>() ?? 10;
}

StepResult result = default!;
for (int attempt = 1; attempt <= retryMax; attempt++) {
    result = await _exec.ExecuteAsync(step, ct);
    if (result.Ok == true || result.Ok == null) break;  // done o skipped
    if (suErr == "retry" && attempt < retryMax) {
        _log.LogWarning("  → Tentativo {A}/{M} fallito, retry tra {D}s", attempt, retryMax, retryDelaySec);
        await _api.ReportStepAsync(..., status: "running",
            output: $"Tentativo {attempt}/{retryMax} fallito. Retry...", ct, cfg.ApiKey);
        await Task.Delay(TimeSpan.FromSeconds(retryDelaySec), ct);
    }
}
```

---

## FEATURE-4 — Storico deploy (archivio workflow)

### Obiettivo
Mantenere la storia completa dei deploy per ogni PC invece di sovrascrivere.

### Schema DB — nuova colonna
```python
# In init_db → migrations:
("pc_workflows", "archived", "INTEGER DEFAULT 0"),
```

### Logica
- Un `pc_workflow` viene marcato `archived=1` quando viene completato da più di 24h (o manualmente).
- `GET /api/pc-workflows` di default restituisce solo `archived=0`.
- Nuovo endpoint `GET /api/pc-workflows/history?pc_name=PC-01` restituisce tutti i workflow inclusi gli archiviati.

### Endpoint nuovo (`server/api.py`)
```python
@app.route("/api/pc-workflows/history", methods=["GET"])
@require_auth
def pc_workflow_history():
    pc_name = request.args.get("pc_name", "").upper().strip()
    if not pc_name:
        return jsonify({"error": "Parametro pc_name obbligatorio"}), 400
    with get_db_ctx() as conn:
        rows = conn.execute("""
            SELECT pw.*, w.nome as workflow_nome
            FROM pc_workflows pw
            JOIN workflows w ON w.id = pw.workflow_id
            WHERE pw.pc_name = ?
            ORDER BY pw.id DESC
        """, (pc_name,)).fetchall()
    return jsonify([dict(r) for r in rows])
```

### UI
Nella scheda dettaglio PC, aggiungere tab "Storico Deploy" che chiama `/api/pc-workflows/history?pc_name=...`.

---

## FEATURE-5 — Rate limiting su `/api/boot/<mac>`

### Obiettivo
Evitare che un MAC in loop di boot inondì `pxe_boot_log` con INSERT infiniti.

### Implementazione (`server/api.py`)
`flask-limiter` è già importato e configurato. Aggiungere il decorator:

```python
@app.route("/api/boot/<mac>", methods=["GET"])
@limiter.limit("10 per minute")   # ← aggiungere questa riga
def pxe_boot_script(mac: str):
```

Per evitare di bloccare MAC legittimi in caso di burst (es. PXE retry), usare un limite più permissivo:
```python
@limiter.limit("30 per minute; 200 per hour")
```

**Nota:** `limiter` con `storage_uri="memory://"` (default) non persiste tra restart. Per produzione usare Redis: `NOVASCM_RATE_LIMIT_STORAGE=redis://localhost:6379`.

---

## FEATURE-6 — Timeout workflow globale

### Obiettivo
Marcare come `error` i workflow rimasti in stato `running` per troppo tempo (agente morto/bloccato).

### Schema DB — nuova colonna in `workflows`
```python
("workflows", "timeout_min", "INTEGER DEFAULT 120"),
```

### Job di cleanup (`server/api.py`)
```python
def _cleanup_stale_workflows():
    """Chiamare periodicamente (es. ogni 5 minuti) o all'avvio del server."""
    now = datetime.datetime.utcnow()
    with get_db_ctx() as conn:
        stale = conn.execute("""
            SELECT pw.id, pw.pc_name, pw.started_at, w.timeout_min
            FROM pc_workflows pw
            JOIN workflows w ON w.id = pw.workflow_id
            WHERE pw.status = 'running'
              AND pw.started_at IS NOT NULL
        """).fetchall()
        for row in stale:
            started = datetime.datetime.fromisoformat(row["started_at"])
            timeout = row["timeout_min"] or 120
            if (now - started).total_seconds() > timeout * 60:
                log.warning("Workflow timeout: pw_id=%s pc=%s", row["id"], row["pc_name"])
                conn.execute(
                    "UPDATE pc_workflows SET status='error', completed_at=? WHERE id=?",
                    (now.isoformat(), row["id"])
                )
        conn.commit()
```

**Integrazione:** chiamare `_cleanup_stale_workflows()` in un thread di background all'avvio:
```python
import threading
def _start_background_jobs():
    def _loop():
        while True:
            try: _cleanup_stale_workflows()
            except Exception as e: log.warning("Cleanup error: %s", e)
            time.sleep(300)  # ogni 5 minuti
    threading.Thread(target=_loop, daemon=True).start()

# Dopo init_db():
_start_background_jobs()
```

---

## FEATURE-7 — Import/Export workflow JSON

### Obiettivo
Esportare un workflow completo (metadati + step) come JSON portabile, e reimportarlo su un'altra istanza NovaSCM.

### Endpoint export (`server/api.py`)
```python
@app.route("/api/workflows/<int:wf_id>/export", methods=["GET"])
@require_auth
def export_workflow(wf_id):
    with get_db_ctx() as conn:
        wf = conn.execute("SELECT * FROM workflows WHERE id=?", (wf_id,)).fetchone()
        if not wf: return jsonify({"error": "Non trovato"}), 404
        steps = conn.execute(
            "SELECT ordine, nome, tipo, parametri, condizione, su_errore, platform "
            "FROM workflow_steps WHERE workflow_id=? ORDER BY ordine ASC", (wf_id,)
        ).fetchall()
    export = {
        "novascm_export": "1.0",
        "exported_at": datetime.datetime.utcnow().isoformat(),
        "workflow": {
            "nome":        dict(wf)["nome"],
            "descrizione": dict(wf)["descrizione"],
            "steps":       [dict(s) for s in steps]
        }
    }
    return Response(
        json.dumps(export, indent=2, ensure_ascii=False),
        mimetype="application/json",
        headers={"Content-Disposition": f"attachment; filename=workflow-{wf_id}.json"}
    )
```

### Endpoint import (`server/api.py`)
```python
@app.route("/api/workflows/import", methods=["POST"])
@require_auth
def import_workflow():
    data = request.get_json(force=True)
    if data.get("novascm_export") != "1.0":
        return jsonify({"error": "Formato non valido"}), 400
    wf_data = data.get("workflow", {})
    if not wf_data.get("nome"):
        return jsonify({"error": "Campo obbligatorio: nome"}), 400
    now = datetime.datetime.utcnow().isoformat()
    with get_db_ctx() as conn:
        try:
            conn.execute(
                "INSERT INTO workflows (nome, descrizione, versione, created_at, updated_at) VALUES (?,?,1,?,?)",
                (wf_data["nome"].strip(), wf_data.get("descrizione", ""), now, now)
            )
            conn.commit()
            wf_id = conn.execute("SELECT id FROM workflows WHERE nome=?",
                                  (wf_data["nome"].strip(),)).fetchone()["id"]
            for step in wf_data.get("steps", []):
                parametri = step.get("parametri", "{}")
                if isinstance(parametri, dict): parametri = json.dumps(parametri)
                conn.execute("""
                    INSERT INTO workflow_steps
                        (workflow_id, ordine, nome, tipo, parametri, condizione, su_errore, platform)
                    VALUES (?,?,?,?,?,?,?,?)
                """, (wf_id, step["ordine"], step["nome"], step["tipo"],
                      parametri, step.get("condizione",""), step.get("su_errore","stop"),
                      step.get("platform","all")))
            conn.commit()
            return jsonify({"ok": True, "workflow_id": wf_id}), 201
        except sqlite3.IntegrityError:
            return jsonify({"error": f"Workflow '{wf_data['nome']}' esiste già"}), 409
```

### UI
- Pulsante "⬇ Esporta" in ogni riga della lista workflow → scarica JSON
- Pulsante "⬆ Importa" nella toolbar → file picker JSON → POST a `/api/workflows/import`

---

## Bug da fixare in parallelo (dal Round 8)

Prima di sviluppare le feature, applicare i fix critici:

| ID | File | Fix |
|---|---|---|
| C-1 | `server/api.py` riga 598 | `@require_auth` su `report_step` |
| C-2 | `server/api.py` riga 618 | `@require_auth` su `get_steps_by_name` |
| C-3 | `server/api.py` riga 1621 | `@require_auth` su `download_deploy_screen` |
| M-1 | `server/api.py` riga 371 | `delete_cr` elimina anche `pc_workflows` |
| M-2 | `server/version.json` | Rimuovere IP privato `192.168.20.110` |

---

## Ordine di implementazione consigliato

```
1. Fix bug R8 (C-1, C-2, C-3, M-1, M-2)        ← prerequisito
2. FEATURE-5 — Rate limiting PXE                  ← 5 minuti, una riga
3. FEATURE-1 — Webhook notifiche                  ← alto impatto, basso effort
4. FEATURE-2 — Dashboard realtime (polling)       ← solo frontend
5. FEATURE-6 — Timeout workflow                   ← previene stati bloccati
6. FEATURE-3 — Retry step                         ← migliora robustezza deploy
7. FEATURE-4 — Storico deploy                     ← nice-to-have
8. FEATURE-7 — Import/export workflow             ← utility, implementare per ultimo
```
