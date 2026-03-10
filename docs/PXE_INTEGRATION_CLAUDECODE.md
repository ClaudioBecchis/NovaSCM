# NovaSCM — Integrazione PXE Server
## Report per Claude Code — v2.0.0

**Data:** 2026-03-10  
**Repository:** https://github.com/ClaudioBecchis/NovaSCM  
**Scope:** Server iPXE/TFTP integrato in NovaSCM — boot intelligente per MAC, auto-creazione CR, sezione PXE nella UI

---

## ARCHITETTURA

```
PC acceso
    │
    ▼
DHCP (UCG-Fiber)
    next-server = 192.168.20.110   ← NovaSCM
    filename    = "ipxe.efi"
    │
    ▼
TFTP → NovaSCM:69/udp
    serve ipxe.efi (binario statico ~400KB)
    │
    ▼
iPXE chainload HTTP
    GET http://192.168.20.110:9091/api/boot/{mac}
    (senza auth — token one-time nel URL)
    │
    ├─ MAC conosciuto + workflow assegnato → script iPXE → chainload iVentoy
    ├─ MAC sconosciuto → auto-crea CR → script iPXE → chainload iVentoy
    └─ Nessun workflow → script iPXE → boot da disco locale
    │
    ▼
iVentoy serve ISO Windows
    │
    ▼
autounattend.xml (generato da NovaSCM per quel PC)
    │
    ▼
NovaSCMAgent avviato → DeployScreen → workflow
```

### Componenti da aggiungere

| Componente | Dove | Descrizione |
|---|---|---|
| TFTP server | `server/pxe_server.py` | Thread separato, porta 69/udp, serve solo `ipxe.efi` |
| Endpoint `/api/boot/<mac>` | `server/api.py` | Script iPXE dinamico per MAC |
| Endpoint `/api/pxe/hosts` | `server/api.py` | CRUD host PXE + log boot |
| Endpoint `/api/pxe/settings` | `server/api.py` | Config TFTP (iVentoy IP, workflow default, dominio) |
| Tabella `pxe_hosts` | SQLite | Mappa MAC → CR + storico boot |
| Tabella `pxe_settings` | SQLite | Config PXE (già esiste tabella `settings`) |
| Sezione UI PXE | `server/web/index.html` | Tab "PXE / Boot" nella UI principale |
| Docker | `server/docker-compose.yml` | Esponi porta 69/udp |
| File `ipxe.efi` | `server/dist/ipxe.efi` | Binario iPXE da scaricare (vedi sezione setup) |

---

## PARTE 1 — DATABASE: nuova tabella `pxe_hosts`

### 1.1 — Aggiungere tabella in `init_db()` (`server/api.py`)

Aggiungere dopo la creazione di `pc_workflow_steps` (riga ~214), prima del `conn.commit()`:

```python
conn.execute("""
    CREATE TABLE IF NOT EXISTS pxe_hosts (
        id             INTEGER PRIMARY KEY AUTOINCREMENT,
        mac            TEXT    NOT NULL UNIQUE,   -- formato: AA:BB:CC:DD:EE:FF (uppercase)
        pc_name        TEXT    DEFAULT '',        -- nome PC associato (da CR se esiste)
        cr_id          INTEGER REFERENCES cr(id) ON DELETE SET NULL,
        workflow_id    INTEGER REFERENCES workflows(id) ON DELETE SET NULL,
        boot_action    TEXT    DEFAULT 'auto',    -- 'auto'|'deploy'|'local'|'block'
        last_boot_at   TEXT,
        boot_count     INTEGER DEFAULT 0,
        last_ip        TEXT    DEFAULT '',
        notes          TEXT    DEFAULT '',
        created_at     TEXT
    )
""")
conn.execute("""
    CREATE TABLE IF NOT EXISTS pxe_boot_log (
        id          INTEGER PRIMARY KEY AUTOINCREMENT,
        mac         TEXT NOT NULL,
        pc_name     TEXT DEFAULT '',
        ip          TEXT DEFAULT '',
        action      TEXT DEFAULT '',        -- 'deploy'|'local'|'block'|'unknown'
        pw_id       INTEGER,               -- pc_workflow_id se deploy
        ts          TEXT
    )
""")
```

Aggiungere anche agli indici:
```python
("idx_pxe_mac", "CREATE INDEX IF NOT EXISTS idx_pxe_mac ON pxe_hosts(mac)"),
("idx_pxe_cr",  "CREATE INDEX IF NOT EXISTS idx_pxe_cr  ON pxe_hosts(cr_id)"),
```

E nelle migrazioni (per DB esistenti):
```python
# Nessuna migrazione colonne necessaria — tabelle nuove create con CREATE IF NOT EXISTS
```

---

## PARTE 2 — TFTP SERVER (`server/pxe_server.py`)

Creare il file `server/pxe_server.py`. Il server TFTP gira in un thread daemon separato e serve **solo** il file `ipxe.efi` dalla cartella `server/dist/`.

```python
"""
NovaSCM — TFTP Server minimale
Serve ipxe.efi via TFTP (porta 69/udp).
Avviato come thread daemon da api.py all'avvio del server Flask.

Dipendenza: pip install tftpy
"""
import logging
import os
import threading

log = logging.getLogger("novascm-tftp")

_tftp_thread: threading.Thread | None = None


def start_tftp_server(dist_dir: str, host: str = "0.0.0.0", port: int = 69) -> None:
    """
    Avvia il server TFTP in background.
    dist_dir: cartella contenente ipxe.efi (e altri file da servire via TFTP).
    """
    global _tftp_thread

    ipxe_path = os.path.join(dist_dir, "ipxe.efi")
    if not os.path.isfile(ipxe_path):
        log.warning(
            "ipxe.efi non trovato in %s — server TFTP non avviato. "
            "Scaricare da https://boot.ipxe.org/ipxe.efi e copiare in server/dist/",
            dist_dir,
        )
        return

    try:
        import tftpy  # type: ignore
    except ImportError:
        log.error(
            "tftpy non installato — server TFTP non avviato. "
            "Eseguire: pip install tftpy"
        )
        return

    def _run() -> None:
        server = tftpy.TftpServer(dist_dir)
        log.info("TFTP server avviato su %s:%d — dist_dir=%s", host, port, dist_dir)
        try:
            server.listen(host, port)
        except PermissionError:
            log.error(
                "Permesso negato per porta %d. "
                "Su Linux la porta 69 richiede root o CAP_NET_BIND_SERVICE. "
                "Nel container Docker assicurarsi che la porta sia esposta come 69:69/udp.",
                port,
            )
        except OSError as exc:
            log.error("TFTP server errore: %s", exc)

    _tftp_thread = threading.Thread(target=_run, name="tftp-server", daemon=True)
    _tftp_thread.start()
    log.info("Thread TFTP avviato (daemon)")
```

### 2.1 — Avviare TFTP da `api.py`

In `api.py`, **dopo** la chiamata `init_db()` (che avviene nella sezione di startup), aggiungere:

```python
# Import in cima al file, con gli altri import
from pxe_server import start_tftp_server

# Dopo init_db(), aggiungere:
# ── TFTP Server PXE (opzionale — richiede tftpy e porta 69) ──────────────────
_PXE_ENABLED = os.environ.get("NOVASCM_PXE_ENABLED", "1").lower() not in ("0", "false", "no")
_DIST_DIR    = os.path.join(os.path.dirname(__file__), "dist")

if _PXE_ENABLED:
    start_tftp_server(
        dist_dir = _DIST_DIR,
        host     = os.environ.get("NOVASCM_TFTP_HOST", "0.0.0.0"),
        port     = int(os.environ.get("NOVASCM_TFTP_PORT", "69")),
    )
```

---

## PARTE 3 — ENDPOINT `/api/boot/<mac>` (nessuna auth)

Questo è l'endpoint più importante. iPXE lo chiama senza header di autenticazione, quindi usiamo un **token monouso opzionale** nell'URL per sicurezza base. L'endpoint risponde con uno script iPXE.

Aggiungere in `api.py`:

```python
# ── PXE BOOT SCRIPT ───────────────────────────────────────────────────────────

def _normalize_mac(mac: str) -> str:
    """Normalizza MAC in formato AA:BB:CC:DD:EE:FF uppercase."""
    clean = re.sub(r"[^0-9a-fA-F]", "", mac)
    if len(clean) != 12:
        return ""
    return ":".join(clean[i:i+2].upper() for i in range(0, 12, 2))


@app.route("/api/boot/<mac>", methods=["GET"])
def pxe_boot_script(mac: str):
    """
    Endpoint iPXE — NESSUNA autenticazione (iPXE non può inviare header).
    Risponde con script iPXE testuale.
    Accessibile solo dalla rete interna (configurare firewall/UCG-Fiber).

    Logica:
      1. Normalizza MAC
      2. Cerca host in pxe_hosts
      3. Se non esiste → auto-crea CR + pxe_host con nome generato da MAC
      4. Controlla boot_action e workflow assegnato
      5. Risponde con script iPXE appropriato
    """
    norm_mac = _normalize_mac(mac)
    if not norm_mac:
        log.warning("Boot PXE: MAC non valido: %s", mac)
        return _ipxe_local("MAC non valido"), 200

    client_ip = request.headers.get("X-Forwarded-For", request.remote_addr).split(",")[0].strip()
    now = datetime.datetime.utcnow().isoformat()

    # Leggi config PXE globale
    pxe_cfg = _get_pxe_settings()

    with get_db_ctx() as conn:
        host = conn.execute(
            "SELECT * FROM pxe_hosts WHERE mac=?", (norm_mac,)
        ).fetchone()

        if not host:
            # MAC sconosciuto — auto-crea CR e pxe_host
            pc_name = _generate_pc_name(norm_mac, pxe_cfg)
            log.info("PXE: MAC sconosciuto %s — auto-creo CR '%s'", norm_mac, pc_name)

            # Crea CR (solo se auto_provision abilitato nelle settings)
            cr_id = None
            if pxe_cfg.get("auto_provision", "1") == "1":
                try:
                    conn.execute("""
                        INSERT OR IGNORE INTO cr
                            (pc_name, domain, ou, dc_ip, join_user, join_pass,
                             admin_pass, software, status, created_at, workflow_id)
                        VALUES (?,?,?,?,?,?,?,?,?,?,?)
                    """, (
                        pc_name,
                        pxe_cfg.get("default_domain", ""),
                        pxe_cfg.get("default_ou", ""),
                        pxe_cfg.get("default_dc_ip", ""),
                        pxe_cfg.get("default_join_user", ""),
                        pxe_cfg.get("default_join_pass", ""),
                        pxe_cfg.get("default_admin_pass", ""),
                        "[]",
                        "open",
                        now,
                        int(pxe_cfg["default_workflow_id"])
                        if pxe_cfg.get("default_workflow_id") else None,
                    ))
                    cr_row = conn.execute(
                        "SELECT id FROM cr WHERE pc_name=?", (pc_name,)
                    ).fetchone()
                    cr_id = cr_row["id"] if cr_row else None
                except Exception as exc:
                    log.warning("PXE auto-provision CR fallito: %s", exc)

            # Crea pxe_host
            wf_id = int(pxe_cfg["default_workflow_id"]) if pxe_cfg.get("default_workflow_id") else None
            conn.execute("""
                INSERT OR IGNORE INTO pxe_hosts
                    (mac, pc_name, cr_id, workflow_id, boot_action,
                     last_boot_at, boot_count, last_ip, created_at)
                VALUES (?,?,?,?,'auto',?,1,?,?)
            """, (norm_mac, pc_name, cr_id, wf_id, now, client_ip, now))
            conn.commit()

            host = conn.execute(
                "SELECT * FROM pxe_hosts WHERE mac=?", (norm_mac,)
            ).fetchone()

        # Aggiorna last_boot e boot_count
        conn.execute("""
            UPDATE pxe_hosts
            SET last_boot_at=?, boot_count=boot_count+1, last_ip=?
            WHERE mac=?
        """, (now, client_ip, norm_mac))

        host_dict = dict(host)
        action     = host_dict.get("boot_action", "auto")
        wf_id      = host_dict.get("workflow_id")
        pc_name    = host_dict.get("pc_name") or norm_mac
        cr_id      = host_dict.get("cr_id")

        # Se auto, controlla se c'è un workflow assegnato
        if action == "auto":
            action = "deploy" if wf_id else "local"

        # Crea/aggiorna pc_workflow se deploy
        pw_id = None
        if action == "deploy" and wf_id:
            try:
                conn.execute("""
                    INSERT OR IGNORE INTO pc_workflows
                        (pc_name, workflow_id, status, assigned_at)
                    VALUES (?,?,'pending',?)
                """, (pc_name, wf_id, now))
                pw_row = conn.execute("""
                    SELECT id FROM pc_workflows
                    WHERE pc_name=? AND status IN ('pending','running')
                    ORDER BY id DESC LIMIT 1
                """, (pc_name,)).fetchone()
                pw_id = pw_row["id"] if pw_row else None
            except Exception as exc:
                log.warning("PXE: creazione pc_workflow fallita: %s", exc)

        # Log boot
        conn.execute("""
            INSERT INTO pxe_boot_log (mac, pc_name, ip, action, pw_id, ts)
            VALUES (?,?,?,?,?,?)
        """, (norm_mac, pc_name, client_ip, action, pw_id, now))
        conn.commit()

    log.info("PXE boot: MAC=%s PC=%s IP=%s action=%s pw_id=%s",
             norm_mac, pc_name, client_ip, action, pw_id)

    iventoy_ip = pxe_cfg.get("iventoy_ip", "192.168.20.110")
    iventoy_port = pxe_cfg.get("iventoy_port", "10809")

    if action == "block":
        return _ipxe_block(pc_name), 200
    elif action == "deploy":
        return _ipxe_deploy(pc_name, iventoy_ip, iventoy_port), 200
    else:
        return _ipxe_local(pc_name), 200


def _get_pxe_settings() -> dict:
    """Legge tutte le settings con prefisso 'pxe_' dalla tabella settings."""
    with get_db_ctx() as conn:
        rows = conn.execute(
            "SELECT key, value FROM settings WHERE key LIKE 'pxe_%'"
        ).fetchall()
    return {r["key"][4:]: r["value"] for r in rows}  # rimuove prefisso 'pxe_'


def _generate_pc_name(mac: str, cfg: dict) -> str:
    """Genera nome PC da MAC: prefisso configurabile + ultimi 6 char MAC senza ':'."""
    prefix = cfg.get("pc_prefix", "PC")
    suffix = mac.replace(":", "")[-6:].upper()
    return f"{prefix}-{suffix}"


def _ipxe_deploy(pc_name: str, iventoy_ip: str, iventoy_port: str) -> str:
    """Script iPXE: avvia deploy via iVentoy."""
    server_url = _get_public_url()
    return f"""#!ipxe
echo NovaSCM Deploy — {pc_name}
echo Connessione a iVentoy {iventoy_ip}:{iventoy_port}...
set iventoy http://{iventoy_ip}:{iventoy_port}
chain ${{iventoy}}/boot.ipxe || goto local_boot

:local_boot
echo iVentoy non raggiungibile — boot da disco locale
sanboot --no-describe --drive 0x80
"""


def _ipxe_local(pc_name: str) -> str:
    """Script iPXE: boot da disco locale."""
    return f"""#!ipxe
echo NovaSCM — {pc_name}
echo Nessun deploy assegnato — boot da disco locale
sanboot --no-describe --drive 0x80
"""


def _ipxe_block(pc_name: str) -> str:
    """Script iPXE: blocca il boot (PC in manutenzione)."""
    return f"""#!ipxe
echo NovaSCM — {pc_name}
echo ATTENZIONE: questo PC e' bloccato — contattare l'amministratore
prompt Premi Invio per spegnere...
poweroff
"""
```

---

## PARTE 4 — ENDPOINT CRUD PXE HOSTS (`server/api.py`)

```python
# ── PXE HOSTS CRUD ────────────────────────────────────────────────────────────

@app.route("/api/pxe/hosts", methods=["GET"])
@require_auth
def list_pxe_hosts():
    """Lista tutti gli host PXE con info CR e workflow."""
    with get_db_ctx() as conn:
        rows = conn.execute("""
            SELECT h.*,
                   c.pc_name  AS cr_pc_name,
                   c.domain   AS cr_domain,
                   c.status   AS cr_status,
                   w.nome     AS workflow_nome
            FROM pxe_hosts h
            LEFT JOIN cr        c ON h.cr_id       = c.id
            LEFT JOIN workflows w ON h.workflow_id = w.id
            ORDER BY h.last_boot_at DESC NULLS LAST
        """).fetchall()
    return jsonify([dict(r) for r in rows])


@app.route("/api/pxe/hosts/<mac>", methods=["GET"])
@require_auth
def get_pxe_host(mac: str):
    norm = _normalize_mac(mac)
    if not norm:
        return jsonify({"error": "MAC non valido"}), 400
    with get_db_ctx() as conn:
        row = conn.execute(
            "SELECT * FROM pxe_hosts WHERE mac=?", (norm,)
        ).fetchone()
    if not row:
        return jsonify({"error": "Host non trovato"}), 404
    return jsonify(dict(row))


@app.route("/api/pxe/hosts", methods=["POST"])
@require_auth
def create_pxe_host():
    """Registra manualmente un host PXE."""
    data = request.get_json(silent=True) or {}
    mac = _normalize_mac(data.get("mac", ""))
    if not mac:
        return jsonify({"error": "MAC non valido o mancante"}), 400
    now = datetime.datetime.utcnow().isoformat()
    with get_db_ctx() as conn:
        try:
            conn.execute("""
                INSERT INTO pxe_hosts
                    (mac, pc_name, cr_id, workflow_id, boot_action, notes, created_at)
                VALUES (?,?,?,?,?,?,?)
            """, (
                mac,
                data.get("pc_name", "").upper().strip(),
                data.get("cr_id") or None,
                data.get("workflow_id") or None,
                data.get("boot_action", "auto"),
                data.get("notes", ""),
                now,
            ))
            conn.commit()
            row = conn.execute(
                "SELECT * FROM pxe_hosts WHERE mac=?", (mac,)
            ).fetchone()
            return jsonify(dict(row)), 201
        except Exception as exc:
            if "UNIQUE" in str(exc):
                return jsonify({"error": f"MAC {mac} già registrato"}), 409
            raise


@app.route("/api/pxe/hosts/<mac>", methods=["PUT"])
@require_auth
def update_pxe_host(mac: str):
    """Aggiorna un host PXE (boot_action, workflow_id, notes, pc_name)."""
    norm = _normalize_mac(mac)
    if not norm:
        return jsonify({"error": "MAC non valido"}), 400
    data = request.get_json(silent=True) or {}
    allowed = ("pc_name", "cr_id", "workflow_id", "boot_action", "notes")
    updates = {k: v for k, v in data.items() if k in allowed}
    if not updates:
        return jsonify({"error": "Nessun campo aggiornabile fornito"}), 400
    set_clause = ", ".join(f"{k}=?" for k in updates)
    values = list(updates.values()) + [norm]
    with get_db_ctx() as conn:
        rowcount = conn.execute(
            f"UPDATE pxe_hosts SET {set_clause} WHERE mac=?", values
        ).rowcount
        if rowcount == 0:
            return jsonify({"error": "Host non trovato"}), 404
        conn.commit()
        row = conn.execute(
            "SELECT * FROM pxe_hosts WHERE mac=?", (norm,)
        ).fetchone()
    return jsonify(dict(row))


@app.route("/api/pxe/hosts/<mac>", methods=["DELETE"])
@require_auth
def delete_pxe_host(mac: str):
    norm = _normalize_mac(mac)
    if not norm:
        return jsonify({"error": "MAC non valido"}), 400
    with get_db_ctx() as conn:
        rowcount = conn.execute(
            "DELETE FROM pxe_hosts WHERE mac=?", (norm,)
        ).rowcount
        if rowcount == 0:
            return jsonify({"error": "Host non trovato"}), 404
        conn.commit()
    return jsonify({"ok": True})


@app.route("/api/pxe/boot-log", methods=["GET"])
@require_auth
def get_pxe_boot_log():
    """Ultimi 200 boot PXE."""
    with get_db_ctx() as conn:
        rows = conn.execute(
            "SELECT * FROM pxe_boot_log ORDER BY id DESC LIMIT 200"
        ).fetchall()
    return jsonify([dict(r) for r in rows])
```

---

## PARTE 5 — ENDPOINT SETTINGS PXE (`server/api.py`)

```python
# ── PXE SETTINGS ──────────────────────────────────────────────────────────────

# Valori default per settings PXE
_PXE_SETTINGS_DEFAULTS = {
    "pxe_enabled":            "1",
    "pxe_iventoy_ip":         "192.168.20.110",
    "pxe_iventoy_port":       "10809",
    "pxe_auto_provision":     "1",       # crea CR automaticamente per MAC sconosciuto
    "pxe_pc_prefix":          "PC",      # prefisso nome PC auto-generato
    "pxe_default_domain":     "polariscore.local",
    "pxe_default_ou":         "OU=Workstations,OU=PolarisCore,DC=polariscore,DC=local",
    "pxe_default_dc_ip":      "192.168.20.12",
    "pxe_default_join_user":  "",
    "pxe_default_join_pass":  "",
    "pxe_default_admin_pass": "",
    "pxe_default_workflow_id": "",       # workflow assegnato a nuovi CR auto-creati
}


@app.route("/api/pxe/settings", methods=["GET"])
@require_auth
def get_pxe_settings_api():
    """Legge le impostazioni PXE."""
    with get_db_ctx() as conn:
        rows = conn.execute(
            "SELECT key, value FROM settings WHERE key LIKE 'pxe_%'"
        ).fetchall()
    result = dict(_PXE_SETTINGS_DEFAULTS)
    result.update({r["key"]: r["value"] for r in rows})
    # Non restituire password in chiaro
    for k in ("pxe_default_join_pass", "pxe_default_admin_pass"):
        if result.get(k):
            result[k] = "••••••••"
    return jsonify(result)


@app.route("/api/pxe/settings", methods=["PUT"])
@require_auth
def update_pxe_settings_api():
    """Aggiorna le impostazioni PXE."""
    data = request.get_json(silent=True) or {}
    allowed_keys = set(_PXE_SETTINGS_DEFAULTS.keys())
    now = datetime.datetime.utcnow().isoformat()
    with get_db_ctx() as conn:
        for key, value in data.items():
            if key not in allowed_keys:
                continue
            # Non sovrascrivere password con placeholder
            if value == "••••••••":
                continue
            conn.execute("""
                INSERT INTO settings (key, value) VALUES (?,?)
                ON CONFLICT(key) DO UPDATE SET value=excluded.value
            """, (key, str(value)))
        conn.commit()
    return jsonify({"ok": True})
```

---

## PARTE 6 — DOCKER (`server/docker-compose.yml`)

Aggiungere la porta TFTP e la variabile d'ambiente nel servizio `novascm`:

```yaml
services:
  novascm:
    # ... configurazione esistente ...
    ports:
      - "9091:9091"      # HTTP API (già presente)
      - "69:69/udp"      # TFTP per PXE  ← AGGIUNGERE
    environment:
      # ... variabili esistenti ...
      NOVASCM_PXE_ENABLED: "1"       # ← AGGIUNGERE
      NOVASCM_TFTP_HOST:   "0.0.0.0" # ← AGGIUNGERE
      NOVASCM_TFTP_PORT:   "69"      # ← AGGIUNGERE
```

### Aggiungere `tftpy` a `requirements.txt` (o `Dockerfile`)

Se esiste `server/requirements.txt`:
```
tftpy==0.8.2
```

Se l'install è nel `Dockerfile`:
```dockerfile
RUN pip install tftpy==0.8.2
```

---

## PARTE 7 — SEZIONE UI PXE (`server/web/index.html`)

Aggiungere un tab "PXE / Boot" nella navigazione principale della UI. La sezione mostra:

- **Tabella host PXE** con MAC, nome PC, azione, ultimo boot, workflow assegnato
- **Boot log** ultimi 50 boot con MAC, IP, azione, timestamp
- **Settings PXE** form per iVentoy IP, auto-provision, prefisso PC, dominio default
- **Badge stato** TFTP server (verde/rosso)

### 7.1 — Aggiungere tab nella navbar

Trovare la sezione `<nav>` o la lista dei tab nella UI e aggiungere:

```html
<!-- Aggiungere accanto agli altri tab -->
<button class="tab-btn" data-tab="pxe" onclick="showTab('pxe')">
  <span class="tab-icon">🖧</span>
  <span class="tab-label">PXE / Boot</span>
</button>
```

### 7.2 — Aggiungere sezione PXE

```html
<!-- Sezione PXE — aggiungere dopo le altre sezioni -->
<section id="tab-pxe" class="tab-section" style="display:none">

  <!-- Header -->
  <div class="section-header">
    <div>
      <h2>PXE / Boot Manager</h2>
      <p class="section-desc">Gestione boot di rete — iPXE chainload → iVentoy</p>
    </div>
    <div class="header-actions">
      <span id="tftp-status" class="badge badge-dim">TFTP ...</span>
      <button class="btn btn-primary" onclick="openPxeHostModal()">+ Aggiungi Host</button>
      <button class="btn btn-ghost" onclick="loadPxeData()">↻ Aggiorna</button>
    </div>
  </div>

  <!-- Settings rapide -->
  <div class="card card-compact" style="margin-bottom:16px">
    <div class="card-grid-4">
      <div class="field-group">
        <label>iVentoy IP</label>
        <input type="text" id="pxe-iventoy-ip" placeholder="192.168.20.110">
      </div>
      <div class="field-group">
        <label>iVentoy Porta</label>
        <input type="text" id="pxe-iventoy-port" placeholder="10809">
      </div>
      <div class="field-group">
        <label>Prefisso PC auto</label>
        <input type="text" id="pxe-pc-prefix" placeholder="PC">
      </div>
      <div class="field-group">
        <label>Auto-provision MAC sconosciuti</label>
        <select id="pxe-auto-provision">
          <option value="1">Abilitato</option>
          <option value="0">Disabilitato</option>
        </select>
      </div>
    </div>
    <div style="margin-top:10px; text-align:right">
      <button class="btn btn-sm btn-primary" onclick="savePxeSettings()">Salva impostazioni</button>
    </div>
  </div>

  <!-- Tabs interni: Host / Boot Log -->
  <div class="inner-tabs">
    <button class="inner-tab active" onclick="showPxeTab('hosts')">Host registrati</button>
    <button class="inner-tab"        onclick="showPxeTab('log')">Boot log</button>
  </div>

  <!-- Tabella host -->
  <div id="pxe-hosts-panel">
    <table class="data-table" id="pxe-hosts-table">
      <thead>
        <tr>
          <th>MAC</th>
          <th>Nome PC</th>
          <th>Azione</th>
          <th>Workflow</th>
          <th>Ultimo boot</th>
          <th>Boot #</th>
          <th>IP</th>
          <th>Azioni</th>
        </tr>
      </thead>
      <tbody id="pxe-hosts-tbody">
        <tr><td colspan="8" class="loading-cell">Caricamento...</td></tr>
      </tbody>
    </table>
  </div>

  <!-- Boot log -->
  <div id="pxe-log-panel" style="display:none">
    <table class="data-table" id="pxe-log-table">
      <thead>
        <tr>
          <th>Timestamp</th>
          <th>MAC</th>
          <th>Nome PC</th>
          <th>IP</th>
          <th>Azione</th>
          <th>pw_id</th>
        </tr>
      </thead>
      <tbody id="pxe-log-tbody">
        <tr><td colspan="6" class="loading-cell">Caricamento...</td></tr>
      </tbody>
    </table>
  </div>

</section>
```

### 7.3 — JavaScript per la sezione PXE

Aggiungere nello script JS esistente (o in un file separato):

```javascript
// ── PXE Manager ───────────────────────────────────────────────
async function loadPxeData() {
  await loadPxeHosts();
  await loadPxeSettings();
  await loadPxeLog();
  checkTftpStatus();
}

async function loadPxeHosts() {
  const rows = await apiGet('/api/pxe/hosts');
  const tbody = document.getElementById('pxe-hosts-tbody');
  if (!rows || rows.length === 0) {
    tbody.innerHTML = '<tr><td colspan="8" class="empty-cell">Nessun host registrato</td></tr>';
    return;
  }
  tbody.innerHTML = rows.map(h => `
    <tr>
      <td><code>${h.mac}</code></td>
      <td>${h.pc_name || '<span class="muted">—</span>'}</td>
      <td><span class="badge badge-${pxeActionColor(h.boot_action)}">${h.boot_action}</span></td>
      <td>${h.workflow_nome || '<span class="muted">—</span>'}</td>
      <td class="muted">${h.last_boot_at ? formatDate(h.last_boot_at) : '—'}</td>
      <td>${h.boot_count || 0}</td>
      <td class="muted">${h.last_ip || '—'}</td>
      <td>
        <button class="btn btn-xs" onclick="editPxeHost('${h.mac}')">✎</button>
        <button class="btn btn-xs btn-danger" onclick="deletePxeHost('${h.mac}')">✕</button>
      </td>
    </tr>
  `).join('');
}

async function loadPxeLog() {
  const rows = await apiGet('/api/pxe/boot-log');
  const tbody = document.getElementById('pxe-log-tbody');
  if (!rows || rows.length === 0) {
    tbody.innerHTML = '<tr><td colspan="6" class="empty-cell">Nessun boot registrato</td></tr>';
    return;
  }
  tbody.innerHTML = rows.map(r => `
    <tr>
      <td class="muted">${formatDate(r.ts)}</td>
      <td><code>${r.mac}</code></td>
      <td>${r.pc_name || '—'}</td>
      <td class="muted">${r.ip || '—'}</td>
      <td><span class="badge badge-${pxeActionColor(r.action)}">${r.action}</span></td>
      <td>${r.pw_id || '—'}</td>
    </tr>
  `).join('');
}

async function loadPxeSettings() {
  const cfg = await apiGet('/api/pxe/settings');
  if (!cfg) return;
  document.getElementById('pxe-iventoy-ip').value   = cfg.iventoy_ip   || '';
  document.getElementById('pxe-iventoy-port').value = cfg.iventoy_port || '10809';
  document.getElementById('pxe-pc-prefix').value    = cfg.pc_prefix    || 'PC';
  document.getElementById('pxe-auto-provision').value = cfg.auto_provision || '1';
}

async function savePxeSettings() {
  const data = {
    pxe_iventoy_ip:     document.getElementById('pxe-iventoy-ip').value,
    pxe_iventoy_port:   document.getElementById('pxe-iventoy-port').value,
    pxe_pc_prefix:      document.getElementById('pxe-pc-prefix').value,
    pxe_auto_provision: document.getElementById('pxe-auto-provision').value,
  };
  await apiPut('/api/pxe/settings', data);
  showToast('Impostazioni PXE salvate');
}

async function deletePxeHost(mac) {
  if (!confirm(`Eliminare host ${mac}?`)) return;
  await apiDelete(`/api/pxe/hosts/${mac}`);
  await loadPxeHosts();
}

async function checkTftpStatus() {
  // Ping endpoint version — se risponde il server è up, TFTP probabilmente anche
  const badge = document.getElementById('tftp-status');
  try {
    await apiGet('/api/version');
    badge.textContent = 'TFTP ON';
    badge.className = 'badge badge-ok';
  } catch {
    badge.textContent = 'SERVER OFF';
    badge.className = 'badge badge-err';
  }
}

function pxeActionColor(action) {
  return { deploy: 'run', local: 'dim', block: 'err', auto: 'warn' }[action] || 'dim';
}

function showPxeTab(name) {
  document.getElementById('pxe-hosts-panel').style.display = name === 'hosts' ? '' : 'none';
  document.getElementById('pxe-log-panel').style.display   = name === 'log'   ? '' : 'none';
  document.querySelectorAll('.inner-tab').forEach((b, i) => {
    b.classList.toggle('active', (i === 0 && name === 'hosts') || (i === 1 && name === 'log'));
  });
}

// Carica dati PXE quando si apre il tab
document.addEventListener('DOMContentLoaded', () => {
  const pxeBtn = document.querySelector('[data-tab="pxe"]');
  if (pxeBtn) pxeBtn.addEventListener('click', loadPxeData);
});
```

---

## PARTE 8 — SETUP DHCP SU UCG-FIBER

Questa configurazione va fatta **una volta sola** sull'UCG-Fiber. Non è codice da scrivere, ma va documentata nel README.

### 8.1 — Configurazione DHCP Option 66/67

Nell'interfaccia UniFi dell'UCG-Fiber:

**Percorso:** Settings → Networks → VLAN 20 (Servers) → DHCP → Advanced

```
DHCP Option 66 (TFTP Server):  192.168.20.110
DHCP Option 67 (Boot File):    ipxe.efi
```

**Oppure via CLI UniFi:**
```
set service dhcp-server shared-network-name VLAN20 subnet 192.168.20.0/24 \
    bootfile-server 192.168.20.110
set service dhcp-server shared-network-name VLAN20 subnet 192.168.20.0/24 \
    bootfile-name ipxe.efi
```

### 8.2 — Configurazione per VLAN 10 (Trusted) — opzionale

Se vuoi fare PXE anche dai PC della VLAN 10 (Trusted), aggiungere le stesse option 66/67 anche per quella subnet, o usare un DHCP relay.

---

## PARTE 9 — FILE `ipxe.efi` (setup iniziale)

Il file `ipxe.efi` è il bootloader iPXE pre-compilato. Va scaricato una volta e copiato in `server/dist/`.

```bash
# Sul server Linux, scaricare iPXE ufficiale
mkdir -p /path/to/NovaSCM/server/dist

# Download diretto (build ufficiale iPXE)
wget https://boot.ipxe.org/ipxe.efi \
     -O /path/to/NovaSCM/server/dist/ipxe.efi

# Verifica
ls -lh /path/to/NovaSCM/server/dist/ipxe.efi
# Atteso: ~400-500KB
```

**Oppure compilare iPXE custom** con il NovaSCM server già embedded (avanzato):

```bash
# Clona iPXE
git clone https://github.com/ipxe/ipxe.git
cd ipxe/src

# Crea script embedded
cat > novascm.ipxe << 'EOF'
#!ipxe
dhcp
chain http://192.168.20.110:9091/api/boot/${net0/mac} || sanboot --no-describe --drive 0x80
EOF

# Compila con script embedded
make bin-x86_64-efi/ipxe.efi EMBED=novascm.ipxe
cp bin-x86_64-efi/ipxe.efi /path/to/NovaSCM/server/dist/
```

> **Consigliato:** usa la build custom — così iPXE chiama direttamente NovaSCM senza bisogno di scrivere il server URL ogni volta.

---

## PARTE 10 — AGGIORNARE `autounattend.xml`

Il template `autounattend.xml` in `api.py` deve scaricare e avviare l'agente via HTTP dal server NovaSCM (già parzialmente presente). Verificare che il blocco `FirstLogonCommands` includa il download dell'agente.

**Nel metodo `get_autounattend`, sostituire il blocco `FirstLogonCommands` con:**

```python
server_url = _get_public_url()
first_logon = f"""
      <FirstLogonCommands>
        <SynchronousCommand wcm:action="add">
          <Order>1</Order>
          <CommandLine>cmd /c mkdir C:\\ProgramData\\NovaSCM\\logs</CommandLine>
          <Description>NovaSCM: crea cartella</Description>
        </SynchronousCommand>
        <SynchronousCommand wcm:action="add">
          <Order>2</Order>
          <CommandLine>powershell.exe -NonInteractive -ExecutionPolicy Bypass -Command "Invoke-WebRequest -Uri '{server_url}/api/download/agent' -Headers @{{'X-Api-Key'='{_xe(d.get('admin_pass','')[:8])}...'}} -OutFile 'C:\\ProgramData\\NovaSCM\\NovaSCMAgent.exe' -UseBasicParsing"</CommandLine>
          <Description>NovaSCM: scarica agente</Description>
        </SynchronousCommand>
        <SynchronousCommand wcm:action="add">
          <Order>3</Order>
          <CommandLine>powershell.exe -NonInteractive -ExecutionPolicy Bypass -Command "Invoke-WebRequest -Uri '{server_url}/api/download/deploy-screen' -Headers @{{'X-Api-Key'='{_xe(d.get('admin_pass','')[:8])}...'}} -OutFile 'C:\\ProgramData\\NovaSCM\\NovaSCMDeployScreen.exe' -UseBasicParsing"</CommandLine>
          <Description>NovaSCM: scarica DeployScreen</Description>
        </SynchronousCommand>
        <SynchronousCommand wcm:action="add">
          <Order>4</Order>
          <CommandLine>powershell.exe -NonInteractive -ExecutionPolicy Bypass -Command "@{{api_url='{server_url}';api_key='{_xe(d.get('cr_api_key',''))}';pc_name='{xpc_name}';poll_sec=30;domain='{xdomain}'}}|ConvertTo-Json|Set-Content 'C:\\ProgramData\\NovaSCM\\agent.json' -Encoding UTF8"</CommandLine>
          <Description>NovaSCM: crea config agente</Description>
        </SynchronousCommand>
        <SynchronousCommand wcm:action="add">
          <Order>5</Order>
          <CommandLine>cmd /c start "" /b "C:\\ProgramData\\NovaSCM\\NovaSCMAgent.exe"</CommandLine>
          <Description>NovaSCM: avvia agente (lancia DeployScreen automaticamente)</Description>
        </SynchronousCommand>
      </FirstLogonCommands>"""
```

> **Nota:** `cr_api_key` è la API key del server da includere nel CR. Aggiungere questo campo alla tabella `cr` e al form di creazione CR nella UI, oppure usare la API key globale del server.

---

## RIEPILOGO FILE DA MODIFICARE/CREARE

| File | Tipo |
|---|---|
| `server/pxe_server.py` | **NUOVO** — TFTP server thread |
| `server/api.py` | Modifica — tabelle DB, endpoint `/api/boot`, `/api/pxe/*` |
| `server/docker-compose.yml` | Modifica — porta 69/udp + env vars |
| `server/requirements.txt` | Modifica — aggiungere `tftpy==0.8.2` |
| `server/web/index.html` | Modifica — tab PXE + HTML + JS |
| `server/dist/ipxe.efi` | **NUOVO** — da scaricare (non committare, aggiungere a .gitignore) |

---

## ORDINE DI IMPLEMENTAZIONE CONSIGLIATO

1. DB — nuove tabelle `pxe_hosts` e `pxe_boot_log` in `init_db()`
2. `pxe_server.py` + avvio da `api.py` + `docker-compose.yml`
3. Scaricare `ipxe.efi` in `server/dist/`
4. Endpoint `/api/boot/<mac>` — il più critico
5. Endpoint CRUD `/api/pxe/hosts` e `/api/pxe/settings`
6. UI — tab PXE
7. Configurare DHCP su UCG-Fiber (opzione 66/67)
8. Test: spegnere e riaccendere un PC sulla VLAN 20, verificare nel boot log

---

## TEST END-TO-END

```bash
# 1. Verifica TFTP risponde
# Da un PC sulla stessa rete:
tftp 192.168.20.110 -c get ipxe.efi /tmp/test.efi && ls -lh /tmp/test.efi

# 2. Simula boot iPXE (curl)
curl http://192.168.20.110:9091/api/boot/AA:BB:CC:DD:EE:FF
# Atteso: script iPXE testuale

# 3. Verifica host auto-creato
curl http://192.168.20.110:9091/api/pxe/hosts \
  -H "X-Api-Key: TUA_CHIAVE" | python3 -m json.tool

# 4. Verifica CR auto-creato
curl http://192.168.20.110:9091/api/cr \
  -H "X-Api-Key: TUA_CHIAVE" | python3 -m json.tool
```

---

*Report generato il 2026-03-10 — NovaSCM v2.0.0 — PolarisCore Homelab*
