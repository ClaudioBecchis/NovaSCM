# NovaSCM — Integrazione PXE Server
## Report per Claude Code — v2.2.0

**Data:** 2026-03-12
**Repository:** https://github.com/ClaudioBecchis/NovaSCM
**Scope:** Server iPXE/TFTP integrato in NovaSCM — boot intelligente per MAC, auto-creazione CR, deploy Windows via wimboot, sezione PXE nella UI

---

## ISTRUZIONI PER CLAUDE CODE

> **LEGGERE ATTENTAMENTE PRIMA DI INIZIARE.**
>
> Questo documento descrive l'integrazione PXE in NovaSCM.
> **iVentoy NON è più in uso.** Il deploy Windows avviene direttamente tramite
> iPXE + wimboot + WinPE serviti via HTTP da NovaSCM.
>
> Implementare le parti nell'ordine indicato nella sezione ORDINE DI IMPLEMENTAZIONE.
>
> **Regole:**
> - NON modificare endpoint o tabelle esistenti che non siano menzionati qui
> - Rispettare lo stile del codice esistente in `api.py` (Flask, SQLite, `get_db_ctx()`)
> - Ogni modifica deve essere testabile singolarmente
> - I default di rete usano il dominio `polariscore.it` (NON `.local`)
> - L'API key per l'agente usa la chiave globale del server (campo `api_key` nella tabella `settings`), NON troncature di password
> - **NON** fare riferimento a iVentoy in nessun punto del codice

---

## ARCHITETTURA

```
PC acceso (BIOS → PXE boot)
    │
    ▼
DHCP (UCG-Fiber, VLAN 10 Trusted)
    next-server = 192.168.20.103   ← CT 103 (NovaSCM)
    filename    = "ipxe.efi"
    │
    ▼
TFTP → NovaSCM (CT 103):69/udp
    serve ipxe.efi (binario statico ~400KB)
    │
    ▼
iPXE chainload HTTP
    GET http://192.168.20.103:9091/api/boot/{mac}
    (nessuna auth — endpoint protetto da subnet allow-list)
    │
    ├─ MAC conosciuto + workflow assegnato → script iPXE deploy
    ├─ MAC sconosciuto + auto-provision  → auto-crea CR + pxe_host → script iPXE deploy
    └─ Nessun workflow                   → script iPXE → boot da disco locale
    │
    ▼ (se deploy)
iPXE carica wimboot + WinPE via HTTP da NovaSCM
    kernel  http://192.168.20.103:9091/api/pxe/file/wimboot
    initrd  http://192.168.20.103:9091/api/pxe/file/BCD          BCD
    initrd  http://192.168.20.103:9091/api/pxe/file/boot.sdi     boot.sdi
    initrd  http://192.168.20.103:9091/api/pxe/file/boot.wim     boot.wim
    initrd  http://192.168.20.103:9091/api/autounattend/{pc_name} autounattend.xml
    boot
    │
    ▼
WinPE avvia Windows Setup con autounattend.xml
    → install.wim da SMB share: \\192.168.20.XXX\deploy\install.wim
    → oppure da partizione locale se presente
    │
    ▼
Windows installato → FirstLogonCommands scarica NovaSCMAgent
    │
    ▼
NovaSCMAgent avviato → DeployScreen → workflow
```

### Componenti da aggiungere

| Componente | Dove | Descrizione |
|---|---|---|
| TFTP server | `server/pxe_server.py` | Thread separato con health check, porta 69/udp, serve solo `ipxe.efi` |
| Endpoint `/api/boot/<mac>` | `server/api.py` | Script iPXE dinamico per MAC (no auth, subnet allow-list) |
| Endpoint `/api/pxe/file/<name>` | `server/api.py` | Serve file WinPE statici (wimboot, BCD, boot.sdi, boot.wim) |
| Endpoint `/api/autounattend/<pc>` | `server/api.py` | Genera autounattend.xml dinamico per PC (no auth, subnet allow-list) |
| Endpoint `/api/pxe/status` | `server/api.py` | Health check TFTP thread + stato file WinPE |
| Endpoint `/api/pxe/hosts` | `server/api.py` | CRUD host PXE + log boot |
| Endpoint `/api/pxe/settings` | `server/api.py` | Config PXE (workflow default, dominio, SMB share) |
| Tabella `pxe_hosts` | SQLite | Mappa MAC → CR + storico boot |
| Tabella `pxe_boot_log` | SQLite | Log boot con cleanup automatico |
| Sezione UI PXE | `server/web/index.html` | Tab "PXE / Boot" nella UI principale |
| Docker | `server/docker-compose.yml` | Esponi porta 69/udp + cap_add NET_BIND_SERVICE |
| File `ipxe.efi` | `server/dist/ipxe.efi` | Binario iPXE da scaricare |
| Cartella WinPE | `server/dist/winpe/` | wimboot + BCD + boot.sdi + boot.wim (vedi PARTE 9) |

### Rete di riferimento

| Risorsa | IP | VLAN | Note |
|---|---|---|---|
| NovaSCM (CT 103) | 192.168.20.103 | 20 (Servers) | API :9091, TFTP :69 |
| PC client di test | 192.168.10.x | 10 (Trusted) | DHCP option 66/67 qui |
| SMB share install.wim | da configurare | 20 (Servers) | TrueNAS o Windows Server |
| UCG-Fiber (gateway) | 192.168.10.1 / 192.168.20.1 | tutte | DHCP server |

---

## PARTE 1 — DATABASE: nuove tabelle

### 1.1 — Aggiungere tabelle in `init_db()` (`server/api.py`)

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
("idx_pxe_mac",    "CREATE INDEX IF NOT EXISTS idx_pxe_mac    ON pxe_hosts(mac)"),
("idx_pxe_cr",     "CREATE INDEX IF NOT EXISTS idx_pxe_cr     ON pxe_hosts(cr_id)"),
("idx_pxe_log_ts", "CREATE INDEX IF NOT EXISTS idx_pxe_log_ts ON pxe_boot_log(ts)"),
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
import time

log = logging.getLogger("novascm-tftp")

_tftp_thread: threading.Thread | None = None
_tftp_healthy: bool = False
_dist_dir: str = ""
_host: str = "0.0.0.0"
_port: int = 69


def start_tftp_server(dist_dir: str, host: str = "0.0.0.0", port: int = 69) -> None:
    """
    Avvia il server TFTP in background.
    dist_dir: cartella contenente ipxe.efi (e altri file da servire via TFTP).
    """
    global _tftp_thread, _dist_dir, _host, _port

    _dist_dir = dist_dir
    _host = host
    _port = port

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

    _launch_tftp_thread()


def _launch_tftp_thread() -> None:
    """Crea e avvia il thread TFTP."""
    global _tftp_thread, _tftp_healthy
    import tftpy  # type: ignore

    def _run() -> None:
        global _tftp_healthy
        server = tftpy.TftpServer(_dist_dir)
        log.info("TFTP server avviato su %s:%d — dist_dir=%s", _host, _port, _dist_dir)
        _tftp_healthy = True
        try:
            server.listen(_host, _port)
        except PermissionError:
            _tftp_healthy = False
            log.error(
                "Permesso negato per porta %d. "
                "Su Linux la porta 69 richiede root o CAP_NET_BIND_SERVICE. "
                "Nel container Docker usare cap_add: NET_BIND_SERVICE e "
                "assicurarsi che la porta sia esposta come 69:69/udp.",
                _port,
            )
        except OSError as exc:
            _tftp_healthy = False
            log.error("TFTP server errore: %s", exc)

    _tftp_thread = threading.Thread(target=_run, name="tftp-server", daemon=True)
    _tftp_thread.start()
    log.info("Thread TFTP avviato (daemon)")


def is_tftp_alive() -> bool:
    """Verifica se il thread TFTP è attivo. Usato dal health check endpoint."""
    return _tftp_healthy and _tftp_thread is not None and _tftp_thread.is_alive()


def restart_tftp_if_dead() -> bool:
    """Riavvia il TFTP se il thread è morto. Ritorna True se riavviato."""
    if _tftp_thread is not None and not _tftp_thread.is_alive() and _dist_dir:
        log.warning("TFTP thread morto — tentativo di restart...")
        _launch_tftp_thread()
        return True
    return False
```

### 2.1 — Avviare TFTP da `api.py`

In `api.py`, **dopo** la chiamata `init_db()` (che avviene nella sezione di startup), aggiungere:

```python
# Import in cima al file, con gli altri import
import ipaddress  # per subnet allow-list PXE
import re         # per _normalize_mac (verificare che non sia già importato)
from pxe_server import start_tftp_server, is_tftp_alive, restart_tftp_if_dead

# Dopo init_db(), aggiungere:
# ── TFTP Server PXE (opzionale — richiede tftpy e porta 69) ──────────────────
_PXE_ENABLED = os.environ.get("NOVASCM_PXE_ENABLED", "1").lower() not in ("0", "false", "no")
_DIST_DIR    = os.path.join(os.path.dirname(__file__), "dist")
_WINPE_DIR   = os.path.join(_DIST_DIR, "winpe")

if _PXE_ENABLED:
    start_tftp_server(
        dist_dir = _DIST_DIR,
        host     = os.environ.get("NOVASCM_TFTP_HOST", "0.0.0.0"),
        port     = int(os.environ.get("NOVASCM_TFTP_PORT", "69")),
    )
```

---

## PARTE 3 — ENDPOINT `/api/boot/<mac>` e file WinPE

Questo è l'endpoint più importante. iPXE lo chiama senza header di autenticazione. L'endpoint è protetto da una **allow-list di subnet** configurabile via env var.

**Cambiamento principale rispetto a v2.1:** lo script iPXE deploy usa `wimboot` per caricare WinPE direttamente da NovaSCM via HTTP. Non c'è più chainload verso iVentoy.

Aggiungere in `api.py`:

```python
# ── PXE SUBNET ALLOW-LIST ────────────────────────────────────────────────────

# Subnet autorizzate per gli endpoint PXE senza auth (/api/boot, /api/pxe/file, /api/autounattend)
# Configurabile via env: NOVASCM_PXE_ALLOWED_SUBNETS="192.168.10.0/24,192.168.20.0/24"
_PXE_ALLOWED_SUBNETS = [
    ipaddress.ip_network(s.strip())
    for s in os.environ.get(
        "NOVASCM_PXE_ALLOWED_SUBNETS",
        "192.168.10.0/24,192.168.20.0/24"
    ).split(",")
    if s.strip()
]


def _is_pxe_allowed(client_ip: str) -> bool:
    """Verifica se l'IP client è in una subnet autorizzata per PXE."""
    try:
        addr = ipaddress.ip_address(client_ip)
        return any(addr in net for net in _PXE_ALLOWED_SUBNETS)
    except ValueError:
        return False


def _get_client_ip() -> str:
    """Estrae l'IP client dalla request, considerando X-Forwarded-For."""
    client_ip = request.headers.get("X-Forwarded-For", request.remote_addr)
    if client_ip:
        client_ip = client_ip.split(",")[0].strip()
    return client_ip or ""


# ── PXE BOOT SCRIPT ──────────────────────────────────────────────────────────

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
    Protetto da subnet allow-list (solo VLAN 10 e 20 di default).
    Risponde con script iPXE testuale (Content-Type: text/plain).

    Logica:
      1. Verifica IP client in subnet autorizzata
      2. Normalizza MAC
      3. Cerca host in pxe_hosts
      4. Se non esiste → auto-crea CR + pxe_host con nome generato da MAC
      5. Controlla boot_action e workflow assegnato
      6. Risponde con script iPXE appropriato (wimboot per deploy)
      7. Cleanup vecchi log se necessario
    """
    client_ip = _get_client_ip()

    # Subnet allow-list
    if not _is_pxe_allowed(client_ip):
        log.warning("PXE boot rifiutato: IP %s non in subnet autorizzata", client_ip)
        return _ipxe_local("Accesso negato"), 403, {"Content-Type": "text/plain"}

    norm_mac = _normalize_mac(mac)
    if not norm_mac:
        log.warning("Boot PXE: MAC non valido: %s", mac)
        return _ipxe_local("MAC non valido"), 200, {"Content-Type": "text/plain"}

    now = datetime.datetime.utcnow().isoformat()

    # Riavvia TFTP se morto (best-effort)
    restart_tftp_if_dead()

    # Leggi config PXE globale
    pxe_cfg = _get_pxe_settings()

    with get_db_ctx() as conn:
        host = conn.execute(
            "SELECT * FROM pxe_hosts WHERE mac=?", (norm_mac,)
        ).fetchone()

        if not host:
            # MAC sconosciuto — auto-crea CR e pxe_host
            pc_name = _generate_pc_name(conn, norm_mac, pxe_cfg)
            log.info("PXE: MAC sconosciuto %s — auto-creo CR '%s'", norm_mac, pc_name)

            # Crea CR (solo se auto_provision abilitato nelle settings)
            cr_id = None
            if pxe_cfg.get("auto_provision", "1") == "1":
                try:
                    cursor = conn.execute("""
                        INSERT INTO cr
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
                    cr_id = cursor.lastrowid
                except Exception as exc:
                    log.warning("PXE auto-provision CR fallito: %s", exc)
                    # Se fallisce per nome duplicato, cerchiamo il CR esistente
                    if "UNIQUE" in str(exc):
                        cr_row = conn.execute(
                            "SELECT id FROM cr WHERE pc_name=?", (pc_name,)
                        ).fetchone()
                        cr_id = cr_row["id"] if cr_row else None

            # Crea pxe_host
            wf_id = int(pxe_cfg["default_workflow_id"]) if pxe_cfg.get("default_workflow_id") else None
            conn.execute("""
                INSERT OR IGNORE INTO pxe_hosts
                    (mac, pc_name, cr_id, workflow_id, boot_action,
                     last_boot_at, boot_count, last_ip, created_at)
                VALUES (?,?,?,?,'auto',?,0,?,?)
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

        # Cleanup: mantieni solo gli ultimi 10000 log
        conn.execute("""
            DELETE FROM pxe_boot_log
            WHERE id NOT IN (SELECT id FROM pxe_boot_log ORDER BY id DESC LIMIT 10000)
        """)

        conn.commit()

    log.info("PXE boot: MAC=%s PC=%s IP=%s action=%s pw_id=%s",
             norm_mac, pc_name, client_ip, action, pw_id)

    # Leggi URL base del server per gli script iPXE
    server_url = _get_public_url()

    if action == "block":
        return _ipxe_block(pc_name), 200, {"Content-Type": "text/plain"}
    elif action == "deploy":
        return _ipxe_deploy(pc_name, server_url), 200, {"Content-Type": "text/plain"}
    else:
        return _ipxe_local(pc_name), 200, {"Content-Type": "text/plain"}


# ── HELPER PXE ────────────────────────────────────────────────────────────────

def _get_pxe_settings() -> dict:
    """Legge tutte le settings con prefisso 'pxe_' dalla tabella settings."""
    with get_db_ctx() as conn:
        rows = conn.execute(
            "SELECT key, value FROM settings WHERE key LIKE 'pxe_%'"
        ).fetchall()
    return {r["key"][4:]: r["value"] for r in rows}  # rimuove prefisso 'pxe_'


def _generate_pc_name(conn, mac: str, cfg: dict) -> str:
    """
    Genera nome PC univoco da MAC: prefisso configurabile + ultimi 6 char MAC senza ':'.
    Se il nome esiste già nella tabella cr, aggiunge un suffisso numerico.
    """
    prefix = cfg.get("pc_prefix", "PC")
    suffix = mac.replace(":", "")[-6:].upper()
    base_name = f"{prefix}-{suffix}"

    # Verifica unicità
    existing = conn.execute(
        "SELECT COUNT(*) as cnt FROM cr WHERE pc_name=?", (base_name,)
    ).fetchone()
    if existing["cnt"] == 0:
        return base_name

    # Se esiste, aggiungi suffisso numerico
    for i in range(2, 100):
        candidate = f"{base_name}-{i}"
        existing = conn.execute(
            "SELECT COUNT(*) as cnt FROM cr WHERE pc_name=?", (candidate,)
        ).fetchone()
        if existing["cnt"] == 0:
            return candidate

    # Fallback improbabile
    import time as _time
    return f"{base_name}-{int(_time.time()) % 10000}"


# ── SCRIPT iPXE ───────────────────────────────────────────────────────────────

def _ipxe_deploy(pc_name: str, server_url: str) -> str:
    """
    Script iPXE: avvia deploy Windows via wimboot + WinPE.
    wimboot carica BCD, boot.sdi, boot.wim e autounattend.xml via HTTP.
    """
    return f"""#!ipxe
echo ============================================
echo  NovaSCM Deploy — {pc_name}
echo ============================================
echo.
echo Caricamento WinPE via HTTP da {server_url}...
echo Questo potrebbe richiedere qualche minuto per boot.wim (~300-500MB)
echo.

kernel {server_url}/api/pxe/file/wimboot
initrd {server_url}/api/pxe/file/BCD         BCD
initrd {server_url}/api/pxe/file/boot.sdi    boot.sdi
initrd {server_url}/api/pxe/file/boot.wim    boot.wim
initrd {server_url}/api/autounattend/{pc_name} autounattend.xml
boot || goto failed

:failed
echo.
echo ERRORE: caricamento WinPE fallito
echo Verificare che i file WinPE siano presenti su NovaSCM
echo (wimboot, BCD, boot.sdi, boot.wim in server/dist/winpe/)
echo.
echo Boot da disco locale in 10 secondi...
sleep 10
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


# ── ENDPOINT FILE WINPE (no auth, subnet allow-list) ─────────────────────────

# File consentiti da servire via /api/pxe/file/<name> (whitelist statica)
_WINPE_ALLOWED_FILES = {"wimboot", "BCD", "boot.sdi", "boot.wim"}


@app.route("/api/pxe/file/<name>", methods=["GET"])
def serve_pxe_file(name: str):
    """
    Serve file statici WinPE per iPXE (wimboot, BCD, boot.sdi, boot.wim).
    NESSUNA autenticazione — protetto da subnet allow-list.
    I file devono essere in server/dist/winpe/.
    """
    client_ip = _get_client_ip()
    if not _is_pxe_allowed(client_ip):
        return "Accesso negato", 403

    # Whitelist nomi file — NESSUN path traversal possibile
    if name not in _WINPE_ALLOWED_FILES:
        log.warning("PXE file: richiesta file non consentito: %s da %s", name, client_ip)
        return "File non consentito", 404

    filepath = os.path.join(_WINPE_DIR, name)
    if not os.path.isfile(filepath):
        log.warning("PXE file: %s non trovato in %s", name, _WINPE_DIR)
        return f"File {name} non trovato — copiare in server/dist/winpe/", 404

    mime = "application/octet-stream"

    log.info("PXE file: serving %s (%s) a %s", name, _sizeof_fmt(os.path.getsize(filepath)), client_ip)
    return send_file(filepath, mimetype=mime, as_attachment=False)


def _sizeof_fmt(num: int) -> str:
    """Formatta dimensione file in formato leggibile."""
    for unit in ("B", "KB", "MB", "GB"):
        if abs(num) < 1024.0:
            return f"{num:.1f}{unit}"
        num /= 1024.0
    return f"{num:.1f}TB"


# ── ENDPOINT AUTOUNATTEND DINAMICO (no auth, subnet allow-list) ──────────────

@app.route("/api/autounattend/<pc_name>", methods=["GET"])
def serve_autounattend(pc_name: str):
    """
    Genera e serve autounattend.xml dinamico per il PC specificato.
    NESSUNA autenticazione — protetto da subnet allow-list.
    Usa i dati del CR associato al pc_name.
    """
    client_ip = _get_client_ip()
    if not _is_pxe_allowed(client_ip):
        return "Accesso negato", 403

    with get_db_ctx() as conn:
        cr = conn.execute(
            "SELECT * FROM cr WHERE pc_name=?", (pc_name,)
        ).fetchone()

    if not cr:
        log.warning("autounattend: CR non trovato per pc_name=%s", pc_name)
        return "CR non trovato", 404

    # Genera autounattend.xml usando la funzione esistente get_autounattend()
    # NOTA: se get_autounattend() è già implementato in api.py, riusare quella funzione.
    # Altrimenti, chiamare la logica esistente passando dict(cr).
    cr_dict = dict(cr)
    xml = _build_autounattend_xml(cr_dict)

    log.info("autounattend: servito XML per %s a %s", pc_name, client_ip)
    return xml, 200, {"Content-Type": "application/xml"}


def _build_autounattend_xml(d: dict) -> str:
    """
    Genera il contenuto XML di autounattend.xml per un CR.
    NOTA per Claude Code: se esiste già una funzione get_autounattend() in api.py,
    riutilizzare quella logica qui. Questa è una versione minimale di riferimento.
    Adattare ai campi reali della tabella cr.
    """
    import xml.sax.saxutils as saxutils
    _xe = saxutils.escape  # escape XML

    server_url = _get_public_url()
    api_key = _get_setting("api_key", "")

    xpc_name = _xe(d.get("pc_name", ""))
    xdomain  = _xe(d.get("domain", ""))
    xou      = _xe(d.get("ou", ""))
    xdc_ip   = _xe(d.get("dc_ip", ""))
    xjoin_u  = _xe(d.get("join_user", ""))
    xjoin_p  = _xe(d.get("join_pass", ""))
    xadmin_p = _xe(d.get("admin_pass", ""))

    # NOTA: questo è un template minimale. La versione completa dovrebbe includere
    # tutte le sezioni di autounattend.xml (disk config, locale, OOBE, ecc.).
    # Adattare al template già presente nel codebase.
    return f"""<?xml version="1.0" encoding="utf-8"?>
<unattend xmlns="urn:schemas-microsoft-com:unattend">
  <settings pass="windowsPE">
    <component name="Microsoft-Windows-International-Core-WinPE"
               processorArchitecture="amd64" language="neutral"
               xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State">
      <SetupUILanguage><UILanguage>it-IT</UILanguage></SetupUILanguage>
      <InputLocale>it-IT</InputLocale>
      <SystemLocale>it-IT</SystemLocale>
      <UILanguage>it-IT</UILanguage>
      <UserLocale>it-IT</UserLocale>
    </component>
  </settings>
  <settings pass="specialize">
    <component name="Microsoft-Windows-Shell-Setup"
               processorArchitecture="amd64" language="neutral"
               xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State">
      <ComputerName>{xpc_name}</ComputerName>
    </component>
    <component name="Microsoft-Windows-UnattendedJoin"
               processorArchitecture="amd64" language="neutral"
               xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State">
      <Identification>
        <JoinDomain>{xdomain}</JoinDomain>
        <MachineObjectOU>{xou}</MachineObjectOU>
        <Credentials>
          <Domain>{xdomain}</Domain>
          <Username>{xjoin_u}</Username>
          <Password>{xjoin_p}</Password>
        </Credentials>
      </Identification>
    </component>
  </settings>
  <settings pass="oobeSystem">
    <component name="Microsoft-Windows-Shell-Setup"
               processorArchitecture="amd64" language="neutral"
               xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State">
      <OOBE>
        <HideEULAPage>true</HideEULAPage>
        <HideLocalAccountScreen>true</HideLocalAccountScreen>
        <HideOEMRegistrationScreen>true</HideOEMRegistrationScreen>
        <HideOnlineAccountScreens>true</HideOnlineAccountScreens>
        <HideWirelessSetupInOOBE>true</HideWirelessSetupInOOBE>
        <ProtectYourPC>3</ProtectYourPC>
      </OOBE>
      <UserAccounts>
        <AdministratorPassword>
          <Value>{xadmin_p}</Value>
          <PlainText>true</PlainText>
        </AdministratorPassword>
      </UserAccounts>
      <AutoLogon>
        <Enabled>true</Enabled>
        <Username>Administrator</Username>
        <Password><Value>{xadmin_p}</Value><PlainText>true</PlainText></Password>
        <LogonCount>3</LogonCount>
      </AutoLogon>
      <FirstLogonCommands>
        <SynchronousCommand wcm:action="add">
          <Order>1</Order>
          <CommandLine>cmd /c mkdir C:\\ProgramData\\NovaSCM\\logs</CommandLine>
          <Description>NovaSCM: crea cartella</Description>
        </SynchronousCommand>
        <SynchronousCommand wcm:action="add">
          <Order>2</Order>
          <CommandLine>powershell.exe -NonInteractive -ExecutionPolicy Bypass -Command "Invoke-WebRequest -Uri '{server_url}/api/download/agent' -Headers @{{'X-Api-Key'='{_xe(api_key)}'}} -OutFile 'C:\\ProgramData\\NovaSCM\\NovaSCMAgent.exe' -UseBasicParsing"</CommandLine>
          <Description>NovaSCM: scarica agente</Description>
        </SynchronousCommand>
        <SynchronousCommand wcm:action="add">
          <Order>3</Order>
          <CommandLine>powershell.exe -NonInteractive -ExecutionPolicy Bypass -Command "Invoke-WebRequest -Uri '{server_url}/api/download/deploy-screen' -Headers @{{'X-Api-Key'='{_xe(api_key)}'}} -OutFile 'C:\\ProgramData\\NovaSCM\\NovaSCMDeployScreen.exe' -UseBasicParsing"</CommandLine>
          <Description>NovaSCM: scarica DeployScreen</Description>
        </SynchronousCommand>
        <SynchronousCommand wcm:action="add">
          <Order>4</Order>
          <CommandLine>powershell.exe -NonInteractive -ExecutionPolicy Bypass -Command "@{{api_url='{server_url}';api_key='{_xe(api_key)}';pc_name='{xpc_name}';poll_sec=30;domain='{xdomain}'}}|ConvertTo-Json|Set-Content 'C:\\ProgramData\\NovaSCM\\agent.json' -Encoding UTF8"</CommandLine>
          <Description>NovaSCM: crea config agente</Description>
        </SynchronousCommand>
        <SynchronousCommand wcm:action="add">
          <Order>5</Order>
          <CommandLine>cmd /c start "" /b "C:\\ProgramData\\NovaSCM\\NovaSCMAgent.exe"</CommandLine>
          <Description>NovaSCM: avvia agente</Description>
        </SynchronousCommand>
      </FirstLogonCommands>
    </component>
  </settings>
</unattend>"""


def _get_setting(key: str, default: str = "") -> str:
    """Legge un singolo valore dalla tabella settings."""
    with get_db_ctx() as conn:
        row = conn.execute("SELECT value FROM settings WHERE key=?", (key,)).fetchone()
    return row["value"] if row else default
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
    # Whitelist campi aggiornabili — protegge da SQL injection
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

## PARTE 5 — ENDPOINT SETTINGS PXE e STATUS (`server/api.py`)

**iVentoy rimosso.** Le settings ora contengono solo configurazione dominio, auto-provision e prefisso PC.

```python
# ── PXE SETTINGS ──────────────────────────────────────────────────────────────

# Valori default per settings PXE (NO iVentoy)
_PXE_SETTINGS_DEFAULTS = {
    "pxe_enabled":            "1",
    "pxe_auto_provision":     "1",       # crea CR automaticamente per MAC sconosciuto
    "pxe_pc_prefix":          "PC",      # prefisso nome PC auto-generato
    "pxe_default_domain":     "polariscore.it",
    "pxe_default_ou":         "OU=Workstations,OU=PolarisCore,DC=polariscore,DC=it",
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
    # Non restituire password in chiaro nella risposta API
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


# ── PXE STATUS (health check TFTP + stato file WinPE) ────────────────────────

@app.route("/api/pxe/status", methods=["GET"])
@require_auth
def get_pxe_status():
    """Stato del server PXE: TFTP alive, PXE enabled, file WinPE presenti, conteggi."""
    # Verifica file WinPE
    winpe_files = {}
    for fname in ("wimboot", "BCD", "boot.sdi", "boot.wim"):
        fpath = os.path.join(_WINPE_DIR, fname)
        if os.path.isfile(fpath):
            winpe_files[fname] = _sizeof_fmt(os.path.getsize(fpath))
        else:
            winpe_files[fname] = None  # file mancante

    # Verifica ipxe.efi
    ipxe_ok = os.path.isfile(os.path.join(_DIST_DIR, "ipxe.efi"))

    with get_db_ctx() as conn:
        host_count = conn.execute("SELECT COUNT(*) as cnt FROM pxe_hosts").fetchone()["cnt"]
        boot_today = conn.execute(
            "SELECT COUNT(*) as cnt FROM pxe_boot_log WHERE ts >= date('now')"
        ).fetchone()["cnt"]

    return jsonify({
        "pxe_enabled": _PXE_ENABLED,
        "tftp_alive": is_tftp_alive(),
        "ipxe_efi": ipxe_ok,
        "winpe_files": winpe_files,
        "winpe_ready": all(v is not None for v in winpe_files.values()),
        "host_count": host_count,
        "boot_today": boot_today,
    })
```

---

## PARTE 6 — DOCKER (`server/docker-compose.yml`)

Aggiungere la porta TFTP, la capability e le variabili d'ambiente nel servizio `novascm`:

```yaml
services:
  novascm:
    # ... configurazione esistente ...
    ports:
      - "9091:9091"      # HTTP API (già presente)
      - "69:69/udp"      # TFTP per PXE  ← AGGIUNGERE
    cap_add:                             # ← AGGIUNGERE
      - NET_BIND_SERVICE               # per porta 69/udp senza root
    environment:
      # ... variabili esistenti ...
      NOVASCM_PXE_ENABLED: "1"                                          # ← AGGIUNGERE
      NOVASCM_TFTP_HOST:   "0.0.0.0"                                    # ← AGGIUNGERE
      NOVASCM_TFTP_PORT:   "69"                                          # ← AGGIUNGERE
      NOVASCM_PXE_ALLOWED_SUBNETS: "192.168.10.0/24,192.168.20.0/24"    # ← AGGIUNGERE
    volumes:
      # ... volumi esistenti ...
      - ./dist:/app/dist:ro       # ipxe.efi + winpe/ (solo lettura)  ← AGGIUNGERE se non già presente
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
- **Settings PXE** form per auto-provision, prefisso PC, dominio default (**NO iVentoy**)
- **Badge stato** TFTP + WinPE (verde/rosso) — usa endpoint `/api/pxe/status`

### 7.1 — Aggiungere tab nella navbar

Trovare la sezione `<nav>` o la lista dei tab nella UI e aggiungere:

```html
<button class="tab-btn" data-tab="pxe" onclick="showTab('pxe')">
  <span class="tab-icon">🖧</span>
  <span class="tab-label">PXE / Boot</span>
</button>
```

### 7.2 — Aggiungere sezione PXE

```html
<section id="tab-pxe" class="tab-section" style="display:none">

  <!-- Header -->
  <div class="section-header">
    <div>
      <h2>PXE / Boot Manager</h2>
      <p class="section-desc">Gestione boot di rete — iPXE + wimboot → WinPE</p>
    </div>
    <div class="header-actions">
      <span id="tftp-status" class="badge badge-dim">PXE ...</span>
      <span id="winpe-status" class="badge badge-dim">WinPE ...</span>
      <button class="btn btn-primary" onclick="openPxeHostModal()">+ Aggiungi Host</button>
      <button class="btn btn-ghost" onclick="loadPxeData()">↻ Aggiorna</button>
    </div>
  </div>

  <!-- Settings rapide -->
  <div class="card card-compact" style="margin-bottom:16px">
    <div class="card-grid-3">
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
      <div class="field-group">
        <label>Dominio default</label>
        <input type="text" id="pxe-default-domain" placeholder="polariscore.it">
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

```javascript
// ── PXE Manager ───────────────────────────────────────────────
async function loadPxeData() {
  await loadPxeHosts();
  await loadPxeSettings();
  await loadPxeLog();
  checkPxeStatus();
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
  document.getElementById('pxe-pc-prefix').value       = cfg.pxe_pc_prefix       || 'PC';
  document.getElementById('pxe-auto-provision').value   = cfg.pxe_auto_provision  || '1';
  document.getElementById('pxe-default-domain').value   = cfg.pxe_default_domain  || '';
}

async function savePxeSettings() {
  const data = {
    pxe_pc_prefix:       document.getElementById('pxe-pc-prefix').value,
    pxe_auto_provision:  document.getElementById('pxe-auto-provision').value,
    pxe_default_domain:  document.getElementById('pxe-default-domain').value,
  };
  await apiPut('/api/pxe/settings', data);
  showToast('Impostazioni PXE salvate');
}

async function deletePxeHost(mac) {
  if (!confirm(`Eliminare host ${mac}?`)) return;
  await apiDelete(`/api/pxe/hosts/${mac}`);
  await loadPxeHosts();
}

async function checkPxeStatus() {
  const tftpBadge = document.getElementById('tftp-status');
  const winpeBadge = document.getElementById('winpe-status');
  try {
    const s = await apiGet('/api/pxe/status');
    if (!s) throw new Error('no data');

    // TFTP badge
    if (s.tftp_alive) {
      tftpBadge.textContent = `TFTP ON (${s.host_count} host, ${s.boot_today} boot oggi)`;
      tftpBadge.className = 'badge badge-ok';
    } else if (!s.pxe_enabled) {
      tftpBadge.textContent = 'PXE DISABILITATO';
      tftpBadge.className = 'badge badge-warn';
    } else {
      tftpBadge.textContent = 'TFTP DOWN';
      tftpBadge.className = 'badge badge-err';
    }

    // WinPE badge
    if (s.winpe_ready) {
      winpeBadge.textContent = 'WinPE OK';
      winpeBadge.className = 'badge badge-ok';
    } else {
      const missing = Object.entries(s.winpe_files || {})
        .filter(([k, v]) => v === null)
        .map(([k]) => k);
      winpeBadge.textContent = `WinPE: manca ${missing.join(', ')}`;
      winpeBadge.className = 'badge badge-err';
    }
  } catch {
    tftpBadge.textContent = 'SERVER OFF';
    tftpBadge.className = 'badge badge-err';
    winpeBadge.textContent = '—';
    winpeBadge.className = 'badge badge-dim';
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

Questa configurazione va fatta **una volta sola** sull'UCG-Fiber. Non è codice da scrivere.

### 8.1 — Configurazione DHCP Option 66/67 su VLAN 10 (Trusted — dove sono i PC client)

> **IMPORTANTE:** le option 66/67 vanno sulla VLAN dei **client** (VLAN 10), NON sulla VLAN server.

**Percorso:** Settings → Networks → VLAN 10 (Trusted) → DHCP → Advanced

```
DHCP Option 66 (TFTP Server):  192.168.20.103
DHCP Option 67 (Boot File):    ipxe.efi
```

### 8.2 — Firewall rules VLAN 10 → VLAN 20

La VLAN 10 deve poter raggiungere CT 103 (192.168.20.103) sulle porte:
- **69/udp** — TFTP (ipxe.efi)
- **9091/tcp** — HTTP API NovaSCM (boot script, file WinPE, autounattend.xml)

Verificare le firewall rules su UCG-Fiber. La VLAN 10 (Trusted) ha `allow all` quindi dovrebbe già funzionare, ma verificare che non ci siano regole di blocco esplicite verso la VLAN 20.

---

## PARTE 9 — FILE WINPE (setup iniziale)

### 9.1 — Scaricare `ipxe.efi`

```bash
mkdir -p /path/to/NovaSCM/server/dist
wget https://boot.ipxe.org/ipxe.efi \
     -O /path/to/NovaSCM/server/dist/ipxe.efi
# Atteso: ~400-500KB
```

**Oppure compilare iPXE custom** con NovaSCM embedded (consigliato):

```bash
git clone https://github.com/ipxe/ipxe.git
cd ipxe/src

cat > novascm.ipxe << 'EOF'
#!ipxe
dhcp
chain http://192.168.20.103:9091/api/boot/${net0/mac} || sanboot --no-describe --drive 0x80
EOF

make bin-x86_64-efi/ipxe.efi EMBED=novascm.ipxe
cp bin-x86_64-efi/ipxe.efi /path/to/NovaSCM/server/dist/
```

### 9.2 — Preparare i file WinPE

I file WinPE vanno nella cartella `server/dist/winpe/`. Si estraggono da una ISO di Windows o dal Windows ADK.

**Metodo 1 — Da ISO Windows (più semplice):**

```bash
# Monta la ISO Windows
mkdir -p /mnt/win_iso
mount -o loop Win11_*.iso /mnt/win_iso

# Crea cartella WinPE
mkdir -p /path/to/NovaSCM/server/dist/winpe

# Copia i file necessari
cp /mnt/win_iso/boot/boot.sdi     /path/to/NovaSCM/server/dist/winpe/
cp /mnt/win_iso/sources/boot.wim  /path/to/NovaSCM/server/dist/winpe/

# BCD va generato o copiato da un WinPE funzionante
# Opzione rapida: copiare da un Windows ADK o da un WinPE esistente
# Il BCD deve puntare a \Windows\system32\winload.efi
```

**Metodo 2 — Da Windows ADK (più pulito):**

```cmd
REM Su un PC Windows con ADK installato:
REM Start → "Deployment and Imaging Tools Environment" (as Admin)

copype amd64 C:\WinPE_amd64

REM I file si trovano in:
REM   C:\WinPE_amd64\media\boot\BCD
REM   C:\WinPE_amd64\media\boot\boot.sdi
REM   C:\WinPE_amd64\media\sources\boot.wim

REM Copiare questi 3 file in server/dist/winpe/
```

**Scaricare wimboot:**

```bash
# wimboot è il bootloader che fa da ponte iPXE → WinPE
wget https://github.com/ipxe/wimboot/releases/latest/download/wimboot \
     -O /path/to/NovaSCM/server/dist/winpe/wimboot
# Atteso: ~50-70KB
```

### 9.3 — Verifica file

```bash
ls -lh /path/to/NovaSCM/server/dist/winpe/
# Atteso:
#   wimboot    ~50-70KB
#   BCD        ~30-70KB
#   boot.sdi   ~3MB
#   boot.wim   ~300-500MB (questo è il file grosso)
```

> **NOTA:** `boot.wim` è ~300-500MB. Il trasferimento via HTTP su rete gigabit richiede ~3-5 secondi, su 100Mbit ~30-50 secondi. Il PC deve avere abbastanza RAM per caricarlo (minimo 2GB).

### 9.4 — `.gitignore`

Aggiungere a `.gitignore`:
```
server/dist/ipxe.efi
server/dist/winpe/
```

---

## RIEPILOGO FILE DA MODIFICARE/CREARE

| File | Tipo | Note |
|---|---|---|
| `server/pxe_server.py` | **NUOVO** | TFTP server thread con health check e auto-restart |
| `server/api.py` | Modifica | Tabelle DB, endpoint `/api/boot`, `/api/pxe/*`, `/api/autounattend` |
| `server/docker-compose.yml` | Modifica | Porta 69/udp + cap_add + env vars + volume dist |
| `server/requirements.txt` | Modifica | Aggiungere `tftpy==0.8.2` |
| `server/web/index.html` | Modifica | Tab PXE + HTML + JS (senza riferimenti iVentoy) |
| `server/dist/ipxe.efi` | **NUOVO** | Da scaricare (.gitignore) |
| `server/dist/winpe/wimboot` | **NUOVO** | Da scaricare (.gitignore) |
| `server/dist/winpe/BCD` | **NUOVO** | Da ISO o ADK (.gitignore) |
| `server/dist/winpe/boot.sdi` | **NUOVO** | Da ISO o ADK (.gitignore) |
| `server/dist/winpe/boot.wim` | **NUOVO** | Da ISO o ADK (.gitignore) |

---

## ORDINE DI IMPLEMENTAZIONE CONSIGLIATO

1. DB — nuove tabelle `pxe_hosts` e `pxe_boot_log` in `init_db()`
2. `pxe_server.py` + avvio da `api.py` + `docker-compose.yml`
3. Scaricare `ipxe.efi` in `server/dist/`
4. Preparare file WinPE in `server/dist/winpe/` (wimboot + BCD + boot.sdi + boot.wim)
5. Endpoint `/api/boot/<mac>` — il più critico
6. Endpoint `/api/pxe/file/<name>` — serve file WinPE via HTTP
7. Endpoint `/api/autounattend/<pc_name>` — genera XML dinamico
8. Endpoint `/api/pxe/status` — health check
9. Endpoint CRUD `/api/pxe/hosts` e `/api/pxe/settings`
10. UI — tab PXE
11. Configurare DHCP su UCG-Fiber — **option 66/67 sulla VLAN 10** (client)
12. Test: spegnere e riaccendere un PC sulla VLAN 10, verificare nel boot log

---

## TEST END-TO-END

```bash
# 1. Verifica TFTP risponde
tftp 192.168.20.103 -c get ipxe.efi /tmp/test.efi && ls -lh /tmp/test.efi

# 2. Simula boot iPXE (curl)
curl http://192.168.20.103:9091/api/boot/AA:BB:CC:DD:EE:FF
# Atteso: script iPXE con kernel wimboot + initrd BCD/boot.sdi/boot.wim

# 3. Verifica file WinPE serviti
curl -I http://192.168.20.103:9091/api/pxe/file/wimboot
# Atteso: 200 OK
curl -I http://192.168.20.103:9091/api/pxe/file/boot.wim
# Atteso: 200 OK (Content-Length ~300-500MB)

# 4. Verifica autounattend.xml dinamico
# (prima creare un CR di test con pc_name=TEST-PC)
curl http://192.168.20.103:9091/api/autounattend/TEST-PC
# Atteso: XML autounattend con ComputerName=TEST-PC

# 5. Verifica accesso negato da subnet non autorizzata
curl http://192.168.20.103:9091/api/boot/AA:BB:CC:DD:EE:FF \
  -H "X-Forwarded-For: 10.0.0.1"
# Atteso: 403

# 6. Verifica host auto-creato
curl http://192.168.20.103:9091/api/pxe/hosts \
  -H "X-Api-Key: TUA_CHIAVE" | python3 -m json.tool

# 7. Verifica health check PXE completo
curl http://192.168.20.103:9091/api/pxe/status \
  -H "X-Api-Key: TUA_CHIAVE" | python3 -m json.tool
# Atteso: tftp_alive: true, winpe_ready: true, winpe_files con dimensioni
```

---

## CHANGELOG v2.1 → v2.2

| Fix | Parte | Descrizione |
|---|---|---|
| **Architettura** | Tutte | **Rimosso iVentoy completamente** — deploy via iPXE + wimboot + WinPE |
| **Nuovo** | 3 | Script iPXE deploy usa `wimboot` per caricare WinPE via HTTP da NovaSCM |
| **Nuovo** | 3 | Endpoint `/api/pxe/file/<name>` per servire wimboot, BCD, boot.sdi, boot.wim |
| **Nuovo** | 3 | Endpoint `/api/autounattend/<pc_name>` per XML dinamico (no auth, subnet allow-list) |
| **Nuovo** | 5 | `/api/pxe/status` include stato file WinPE (presenti/mancanti con dimensioni) |
| **Nuovo** | 9 | Documentazione completa setup WinPE (da ISO e da ADK) + download wimboot |
| **Rimosso** | 5 | Setting `pxe_iventoy_ip` e `pxe_iventoy_port` eliminate |
| **Rimosso** | 7 | Campi iVentoy IP/porta dalla UI |
| **Aggiunto** | 7 | Badge separato stato WinPE con lista file mancanti |
| **Aggiunto** | 7 | Campo dominio default nella UI settings |
| **Config** | Tutte | IP NovaSCM aggiornato a 192.168.20.103 (CT 103) |
| **Sicurezza** | 3 | File WinPE serviti solo da whitelist statica (`_WINPE_ALLOWED_FILES`) |
| **Docker** | 6 | Aggiunto volume `./dist:/app/dist:ro` per file PXE |

---

*Report generato il 2026-03-12 — NovaSCM v2.2.0 — PolarisCore Homelab*
