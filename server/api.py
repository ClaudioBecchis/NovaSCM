"""
NovaSCM API — CR Management + Workflow Engine
Porta: 9091 (configurabile con PORT env var)
DB:    /data/novascm.db (configurabile con NOVASCM_DB env var)
"""
from flask import Flask, request, jsonify, Response, send_from_directory
from werkzeug.middleware.proxy_fix import ProxyFix
import sqlite3, json, datetime, os, functools, hmac, logging, re, secrets, threading, time
import urllib.request as _urllib_req
from xml.sax.saxutils import escape as _xe
from contextlib import contextmanager

try:
    from pxe_server import start_tftp_server as _start_tftp
    _pxe_server_available = True
except ImportError:
    _pxe_server_available = False

try:
    from pythonjsonlogger import jsonlogger as _jsonlogger
    _json_log_available = True
except ImportError:
    _json_log_available = False
try:
    from flask_limiter import Limiter
    from flask_limiter.util import get_remote_address
    _limiter_available = True
except ImportError:
    _limiter_available = False
    log_startup = logging.getLogger("novascm-api")
    log_startup.warning("flask-limiter non disponibile — rate limiting disabilitato (pip install flask-limiter)")

app = Flask(__name__)
app.wsgi_app = ProxyFix(app.wsgi_app, x_for=1, x_proto=1, x_host=1, x_prefix=1)

_CORS_ORIGINS = [o.strip() for o in os.environ.get("NOVASCM_CORS_ORIGINS", "").split(",") if o.strip()]

@app.after_request
def add_cors(response):
    origin = request.headers.get("Origin", "")
    if _CORS_ORIGINS and origin in _CORS_ORIGINS:
        response.headers["Access-Control-Allow-Origin"]  = origin
        response.headers["Access-Control-Allow-Headers"] = "X-Api-Key, Content-Type"
        response.headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS"
        response.headers["Vary"] = "Origin"
    return response

@app.route("/api/<path:_>", methods=["OPTIONS"])
def cors_preflight(_):
    r = Response()
    origin = request.headers.get("Origin", "")
    if _CORS_ORIGINS and origin in _CORS_ORIGINS:
        r.headers["Access-Control-Allow-Origin"]  = origin
        r.headers["Access-Control-Allow-Headers"] = "X-Api-Key, Content-Type"
        r.headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS"
        r.headers["Vary"] = "Origin"
    return r, 204

# ── Rate limiting (facoltativo: richiede flask-limiter) ───────────────────────
if _limiter_available:
    limiter = Limiter(
        get_remote_address,
        app=app,
        default_limits=["300 per minute", "3000 per hour"],
        storage_uri=os.environ.get("NOVASCM_RATE_LIMIT_STORAGE", "memory://"),
    )
else:
    # Stub no-op quando flask-limiter non è installato
    class _NoOpLimiter:
        def limit(self, *a, **kw):
            return lambda f: f
        def exempt(self, f):
            return f
    limiter = _NoOpLimiter()

# Percorso DB: variabile d'ambiente NOVASCM_DB, default /data/novascm.db
DB = os.environ.get("NOVASCM_DB", "/data/novascm.db")

# URL pubblico del server (M-2: evita Host header injection dietro reverse proxy)
# Se impostato, sovrascrive request.host_url negli installer generati dinamicamente
_PUBLIC_URL = os.environ.get("NOVASCM_PUBLIC_URL", "").rstrip("/")

def _get_public_url() -> str:
    """Restituisce l'URL pubblico del server, con priorità a NOVASCM_PUBLIC_URL."""
    return _PUBLIC_URL if _PUBLIC_URL else request.host_url.rstrip("/")

# ── Logging ───────────────────────────────────────────────────────────────────
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s — %(message)s"
)
log = logging.getLogger("novascm-api")

if os.environ.get("NOVASCM_LOG_JSON", "").lower() in ("1", "true", "yes"):
    if _json_log_available:
        _handler = logging.StreamHandler()
        _handler.setFormatter(_jsonlogger.JsonFormatter(
            "%(asctime)s %(name)s %(levelname)s %(message)s"
        ))
        logging.getLogger().handlers = [_handler]
    else:
        log.warning("NOVASCM_LOG_JSON=1 ma python-json-logger non installato — logging testuale attivo")

# ── Autenticazione API Key ────────────────────────────────────────────────────
API_KEY = os.environ.get("NOVASCM_API_KEY", "")

# Secret bootstrap: se l'env var è assente, usa /data/.api_key (o genera una nuova chiave)
if not API_KEY:
    _key_file = os.path.join(os.path.dirname(os.environ.get("NOVASCM_DB", "/data/novascm.db")), ".api_key")
    if os.path.isfile(_key_file):
        with open(_key_file) as _f:
            API_KEY = _f.read().strip()
        log.info("NOVASCM_API_KEY caricata da %s", _key_file)
    else:
        API_KEY = secrets.token_hex(32)
        try:
            os.makedirs(os.path.dirname(_key_file), exist_ok=True)
            with open(_key_file, "w") as _f:
                _f.write(API_KEY)
            os.chmod(_key_file, 0o600)
            log.warning("NOVASCM_API_KEY non impostata — generata e salvata in %s", _key_file)
        except OSError:
            log.warning("NOVASCM_API_KEY non impostata — generata in memoria (non persistita)")
        log.warning("API Key generata: %s", API_KEY)

def require_auth(fn):
    """Decorator: richiede header X-Api-Key corrispondente a NOVASCM_API_KEY."""
    @functools.wraps(fn)
    def wrapper(*args, **kwargs):
        if not API_KEY:
            log.error("NOVASCM_API_KEY non configurata — accesso bloccato")
            return jsonify({"error": "Server non configurato: NOVASCM_API_KEY mancante"}), 500
        token = request.headers.get("X-Api-Key", "")
        if not hmac.compare_digest(token, API_KEY):
            log.warning("Accesso non autorizzato da %s", request.remote_addr)
            return jsonify({"error": "Non autorizzato"}), 401
        return fn(*args, **kwargs)
    return wrapper

# ── DB ────────────────────────────────────────────────────────────────────────
# I-7: connessione thread-local — evita riapertura per ogni richiesta
_db_local = threading.local()

def get_db():
    conn = getattr(_db_local, "conn", None)
    if conn is None:
        conn = sqlite3.connect(DB, timeout=30, check_same_thread=False)
        conn.row_factory = sqlite3.Row
        conn.execute("PRAGMA journal_mode=WAL")
        conn.execute("PRAGMA busy_timeout=5000")
        conn.execute("PRAGMA synchronous=NORMAL")
        conn.execute("PRAGMA foreign_keys = ON")
        _db_local.conn = conn
    return conn

@contextmanager
def get_db_ctx():
    conn = get_db()
    try:
        yield conn
    except Exception:
        conn.rollback()
        raise

def init_db():
    os.makedirs(os.path.dirname(DB), exist_ok=True)
    with get_db_ctx() as conn:
        conn.execute("""
            CREATE TABLE IF NOT EXISTS cr (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                pc_name       TEXT NOT NULL UNIQUE,
                domain        TEXT NOT NULL,
                ou            TEXT,
                dc_ip         TEXT,
                join_user     TEXT,
                join_pass     TEXT,
                odj_blob      TEXT,
                admin_pass    TEXT,
                software      TEXT DEFAULT '[]',
                assigned_user TEXT,
                notes         TEXT,
                status        TEXT DEFAULT 'open',
                created_at    TEXT,
                completed_at  TEXT,
                last_seen     TEXT
            )
        """)
        conn.execute("""
            CREATE TABLE IF NOT EXISTS cr_steps (
                id        INTEGER PRIMARY KEY AUTOINCREMENT,
                cr_id     INTEGER NOT NULL,
                step_name TEXT NOT NULL,
                status    TEXT DEFAULT 'done',
                timestamp TEXT,
                UNIQUE(cr_id, step_name)
            )
        """)
        conn.execute("""
            CREATE TABLE IF NOT EXISTS settings (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL DEFAULT ''
            )
        """)
        conn.execute("""
            CREATE TABLE IF NOT EXISTS workflows (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                nome        TEXT NOT NULL UNIQUE,
                descrizione TEXT DEFAULT '',
                versione    INTEGER DEFAULT 1,
                created_at  TEXT,
                updated_at  TEXT
            )
        """)
        conn.execute("""
            CREATE TABLE IF NOT EXISTS workflow_steps (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                workflow_id INTEGER NOT NULL REFERENCES workflows(id) ON DELETE CASCADE,
                ordine      INTEGER NOT NULL,
                nome        TEXT NOT NULL,
                tipo        TEXT NOT NULL,
                parametri   TEXT DEFAULT '{}',
                condizione  TEXT DEFAULT '',
                su_errore   TEXT DEFAULT 'stop',
                platform    TEXT DEFAULT 'all',
                UNIQUE(workflow_id, ordine)
            )
        """)
        conn.execute("""
            CREATE TABLE IF NOT EXISTS pc_workflows (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                pc_name      TEXT NOT NULL,
                workflow_id  INTEGER NOT NULL REFERENCES workflows(id),
                status       TEXT DEFAULT 'pending',
                assigned_at  TEXT,
                started_at   TEXT,
                completed_at TEXT,
                last_seen    TEXT,
                UNIQUE(pc_name, workflow_id)
            )
        """)
        conn.execute("""
            CREATE TABLE IF NOT EXISTS pc_workflow_steps (
                id             INTEGER PRIMARY KEY AUTOINCREMENT,
                pc_workflow_id INTEGER NOT NULL REFERENCES pc_workflows(id) ON DELETE CASCADE,
                step_id        INTEGER NOT NULL REFERENCES workflow_steps(id),
                status         TEXT DEFAULT 'pending',
                output         TEXT DEFAULT '',
                timestamp      TEXT,
                UNIQUE(pc_workflow_id, step_id)
            )
        """)
        conn.execute("""
            CREATE TABLE IF NOT EXISTS pxe_hosts (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                mac          TEXT    NOT NULL UNIQUE,
                pc_name      TEXT    DEFAULT '',
                cr_id        INTEGER REFERENCES cr(id) ON DELETE SET NULL,
                workflow_id  INTEGER REFERENCES workflows(id) ON DELETE SET NULL,
                boot_action  TEXT    DEFAULT 'auto',
                last_boot_at TEXT,
                boot_count   INTEGER DEFAULT 0,
                last_ip      TEXT    DEFAULT '',
                notes        TEXT    DEFAULT '',
                created_at   TEXT
            )
        """)
        conn.execute("""
            CREATE TABLE IF NOT EXISTS pxe_boot_log (
                id       INTEGER PRIMARY KEY AUTOINCREMENT,
                mac      TEXT NOT NULL,
                pc_name  TEXT DEFAULT '',
                ip       TEXT DEFAULT '',
                action   TEXT DEFAULT '',
                pw_id    INTEGER,
                ts       TEXT
            )
        """)
        conn.commit()
        # Indici per query frequenti
        _idx = [
            ("idx_cr_pc_name",            "CREATE INDEX IF NOT EXISTS idx_cr_pc_name ON cr(pc_name)"),
            ("idx_pw_pc_name",            "CREATE INDEX IF NOT EXISTS idx_pw_pc_name ON pc_workflows(pc_name)"),
            ("idx_pw_status",             "CREATE INDEX IF NOT EXISTS idx_pw_status ON pc_workflows(status)"),
            ("idx_pws_pc_workflow_id",    "CREATE INDEX IF NOT EXISTS idx_pws_pc_workflow_id ON pc_workflow_steps(pc_workflow_id)"),
            ("idx_pxe_mac",               "CREATE INDEX IF NOT EXISTS idx_pxe_mac ON pxe_hosts(mac)"),
            ("idx_pxe_cr",                "CREATE INDEX IF NOT EXISTS idx_pxe_cr  ON pxe_hosts(cr_id)"),
        ]
        for _name, _sql in _idx:
            conn.execute(_sql)
        conn.commit()
        # Migrazioni colonne (DB esistenti)
        migrations = [
            ("cr", "last_seen",   "TEXT"),
            ("cr", "odj_blob",    "TEXT"),
            ("cr", "workflow_id", "INTEGER"),
            ("workflow_steps", "platform", "TEXT DEFAULT 'all'"),
            ("pc_workflows", "hardware_json",  "TEXT"),
            ("pc_workflows", "log_text",       "TEXT"),
            ("pc_workflows", "screenshot_b64", "TEXT"),
            ("pc_workflow_steps", "elapsed_sec",  "REAL DEFAULT 0"),
        ("pc_workflows",      "archived",     "INTEGER DEFAULT 0"),
        ("workflows",         "timeout_min",  "INTEGER DEFAULT 120"),
        ]
        # M-7: tabella enrollment token monouso
        conn.execute("""
            CREATE TABLE IF NOT EXISTS enrollment_tokens (
                token      TEXT PRIMARY KEY,
                expires_at REAL NOT NULL,
                used       INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL
            )
        """)
        for table, col, typ in migrations:
            try:
                conn.execute(f"ALTER TABLE {table} ADD COLUMN {col} {typ}")
                conn.commit()
            except sqlite3.OperationalError as e:
                if "duplicate column name" not in str(e).lower():
                    log.warning("Migrazione %s.%s: %s", table, col, e)

_SENSITIVE = {"join_pass", "admin_pass"}

def row_to_dict(row, include_sensitive: bool = False):
    d = dict(row)
    d["software"] = json.loads(d.get("software") or "[]")
    if not include_sensitive:
        for k in _SENSITIVE:
            d.pop(k, None)
    return d

# ── Webhook notifiche ─────────────────────────────────────────────────────────

def _fire_webhook(event: str, data: dict):
    """Chiama il webhook configurato in modo fire-and-forget (thread daemon)."""
    with get_db_ctx() as conn:
        row_url = conn.execute("SELECT value FROM settings WHERE key='webhook_url'").fetchone()
        row_en  = conn.execute("SELECT value FROM settings WHERE key='webhook_enabled'").fetchone()
    url     = row_url["value"] if row_url else ""
    enabled = (row_en["value"] if row_en else "0") == "1"
    if not url or not enabled:
        return
    payload = json.dumps({
        "event": event,
        "ts":    datetime.datetime.utcnow().isoformat(),
        **data
    }).encode()
    def _send():
        try:
            req = _urllib_req.Request(
                url, data=payload,
                headers={"Content-Type": "application/json"}, method="POST"
            )
            _urllib_req.urlopen(req, timeout=5)
        except Exception as exc:
            log.warning("Webhook failed (%s): %s", url, exc)
    threading.Thread(target=_send, daemon=True).start()


# ── Cleanup workflow in stallo ─────────────────────────────────────────────────

def _cleanup_stale_workflows():
    """Marca come 'error' i workflow running da più di timeout_min minuti."""
    now = datetime.datetime.utcnow()
    with get_db_ctx() as conn:
        stale = conn.execute("""
            SELECT pw.id, pw.pc_name, pw.started_at, COALESCE(w.timeout_min, 120) AS timeout_min
            FROM pc_workflows pw
            JOIN workflows w ON w.id = pw.workflow_id
            WHERE pw.status = 'running'
              AND pw.started_at IS NOT NULL
        """).fetchall()
        for row in stale:
            try:
                started = datetime.datetime.fromisoformat(row["started_at"])
            except (ValueError, TypeError):
                continue
            if (now - started).total_seconds() > row["timeout_min"] * 60:
                log.warning("Workflow timeout: pw_id=%s pc=%s", row["id"], row["pc_name"])
                conn.execute(
                    "UPDATE pc_workflows SET status='error', completed_at=? WHERE id=?",
                    (now.isoformat(), row["id"])
                )
                _fire_webhook("workflow_timeout", {
                    "pc_name": row["pc_name"],
                    "pw_id":   row["id"],
                    "status":  "error",
                })
        conn.commit()


def _start_background_jobs():
    def _loop():
        while True:
            try:
                _cleanup_stale_workflows()
            except Exception as exc:
                log.warning("Background cleanup error: %s", exc)
            time.sleep(300)
    threading.Thread(target=_loop, daemon=True).start()


# ── CR CRUD ───────────────────────────────────────────────────────────────────

@app.route("/api/cr", methods=["GET"])
@require_auth
def list_cr():
    with get_db_ctx() as conn:
        rows = conn.execute("SELECT * FROM cr ORDER BY id DESC").fetchall()
    return jsonify([row_to_dict(r) for r in rows])

@app.route("/api/cr", methods=["POST"])
@require_auth
def create_cr():
    data = request.get_json(force=True)
    for f in ("pc_name", "domain"):
        if not data.get(f):
            return jsonify({"error": f"Campo obbligatorio: {f}"}), 400
    now = datetime.datetime.now().isoformat()
    with get_db_ctx() as conn:
        try:
            conn.execute("""
                INSERT INTO cr (pc_name, domain, ou, dc_ip, join_user, join_pass,
                                odj_blob, admin_pass, software, assigned_user, notes, status, created_at, workflow_id)
                VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?)
            """, (
                data["pc_name"].upper().strip(),
                data["domain"],
                data.get("ou", ""),
                data.get("dc_ip", ""),
                data.get("join_user", ""),
                data.get("join_pass", ""),
                data.get("odj_blob", ""),
                data.get("admin_pass", ""),
                json.dumps(data.get("software", [])),
                data.get("assigned_user", ""),
                data.get("notes", ""),
                "open",
                now,
                data.get("workflow_id") or None
            ))
            conn.commit()
            row = conn.execute("SELECT * FROM cr WHERE pc_name=?",
                               (data["pc_name"].upper().strip(),)).fetchone()
            return jsonify(row_to_dict(row)), 201
        except sqlite3.IntegrityError:
            return jsonify({"error": f"PC '{data['pc_name']}' esiste già"}), 409

@app.route("/api/cr/<int:cr_id>", methods=["GET"])
@require_auth
def get_cr(cr_id):
    with get_db_ctx() as conn:
        row = conn.execute("SELECT * FROM cr WHERE id=?", (cr_id,)).fetchone()
    if not row:
        return jsonify({"error": "Non trovato"}), 404
    return jsonify(row_to_dict(row))

@app.route("/api/cr/by-name/<pc_name>", methods=["GET"])
@require_auth
def get_cr_by_name(pc_name):
    with get_db_ctx() as conn:
        row = conn.execute("SELECT * FROM cr WHERE pc_name=?",
                           (pc_name.upper().strip(),)).fetchone()
    if not row:
        return jsonify({"error": "CR non trovato"}), 404
    return jsonify(row_to_dict(row))

@app.route("/api/cr/<int:cr_id>/status", methods=["PUT"])
@require_auth
def update_status(cr_id):
    data = request.get_json(force=True)
    status = data.get("status")
    if status not in ("open", "in_progress", "completed"):
        return jsonify({"error": "Stato non valido"}), 400
    now = datetime.datetime.now().isoformat() if status == "completed" else None
    with get_db_ctx() as conn:
        rowcount = conn.execute("UPDATE cr SET status=?, completed_at=? WHERE id=?",
                                (status, now, cr_id)).rowcount
        if rowcount == 0:
            return jsonify({"error": "Non trovato"}), 404
        conn.commit()
        row = conn.execute("SELECT * FROM cr WHERE id=?", (cr_id,)).fetchone()
    return jsonify(row_to_dict(row))

@app.route("/api/cr/<int:cr_id>", methods=["DELETE"])
@require_auth
def delete_cr(cr_id):
    with get_db_ctx() as conn:
        row = conn.execute("SELECT pc_name FROM cr WHERE id=?", (cr_id,)).fetchone()
        if not row:
            return jsonify({"error": "Non trovato"}), 404
        pc_name = row["pc_name"]
        conn.execute("DELETE FROM cr_steps    WHERE cr_id=?",    (cr_id,))
        conn.execute("DELETE FROM pc_workflows WHERE pc_name=?", (pc_name,))
        conn.execute("DELETE FROM cr           WHERE id=?",      (cr_id,))
        conn.commit()
    return jsonify({"ok": True})

# ── Autounattend.xml generato dal server ──────────────────────────────────────

@app.route("/api/cr/by-name/<pc_name>/autounattend.xml", methods=["GET"])
@require_auth
def get_autounattend(pc_name):
    with get_db_ctx() as conn:
        row = conn.execute("SELECT * FROM cr WHERE pc_name=?",
                           (pc_name.upper().strip(),)).fetchone()
    if not row:
        return "CR non trovato", 404
    d = row_to_dict(row, include_sensitive=True)
    pkgs = d.get("software", [])
    def _safe_pkg(pkg_id):
        """Sanifica package ID winget: solo caratteri alfanumerici, punto, trattino, underscore."""
        return re.sub(r"[^a-zA-Z0-9.\-_]", "", str(pkg_id))
    winget_block = "\n".join(
        f"winget install --id {_safe_pkg(p)} --silent --accept-package-agreements --accept-source-agreements"
        for p in pkgs if p and _safe_pkg(p)
    ) if pkgs else "# Nessun software configurato"

    # SEC-1: escape di tutti i valori interpolati nel XML
    xpc_name   = _xe(d.get("pc_name") or "")
    xadmin_pw  = _xe(d.get("admin_pass") or "")
    xdc_ip     = _xe(d.get("dc_ip") or "")
    xdomain    = _xe(d.get("domain") or "")
    xjoin_user = _xe(d.get("join_user") or "")
    xjoin_pass = _xe(d.get("join_pass") or "")
    odj_blob   = _xe((d.get("odj_blob") or "").strip())

    has_domain = bool(d.get("domain") and d.get("dc_ip") and d.get("join_user"))
    run_sync = ""
    odj_component = ""
    if odj_blob:
        # ODJ mode — nessuna password nel XML, blob generato da djoin.exe sul DC
        odj_component = f"""
    <component name="Microsoft-Windows-UnattendedJoin"
               processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35"
               language="neutral" versionScope="nonSxS">
      <OfflineDomainJoin>
        <RequestODJBlob>{odj_blob}</RequestODJBlob>
      </OfflineDomainJoin>
    </component>"""
    elif has_domain:
        # Credenziali classiche — Add-Computer via RunSynchronousCommands
        run_sync = f"""
      <RunSynchronousCommands wcm:action="add">
        <RunSynchronousCommand wcm:action="add">
          <Order>1</Order>
          <Path>powershell.exe -NonInteractive -Command "for($i=0;$i-lt30;$i++){{$n=Get-NetAdapter|?{{$_.Status-eq'Up'-and$_.HardwareInterface}}|Select -First 1;if($n){{Set-DnsClientServerAddress -InterfaceIndex $n.InterfaceIndex -ServerAddresses '{xdc_ip}';break}};Start-Sleep 2}}"</Path>
          <Description>Attendi rete e imposta DNS DC</Description>
        </RunSynchronousCommand>
        <RunSynchronousCommand wcm:action="add">
          <Order>2</Order>
          <Path>powershell.exe -NonInteractive -Command "Add-Computer -DomainName '{xdomain}' -Credential (New-Object PSCredential('{xdomain}\\{xjoin_user}',(ConvertTo-SecureString '{xjoin_pass}' -AsPlainText -Force))) -Force -ErrorAction SilentlyContinue"</Path>
          <Description>Join dominio AD</Description>
        </RunSynchronousCommand>
      </RunSynchronousCommands>"""

    xml = f"""<?xml version="1.0" encoding="utf-8"?>
<unattend xmlns="urn:schemas-microsoft-com:unattend"
          xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State"
          xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">

  <settings pass="windowsPE">
    <component name="Microsoft-Windows-International-Core-WinPE"
               processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35"
               language="neutral" versionScope="nonSxS">
      <SetupUILanguage><UILanguage>it-IT</UILanguage></SetupUILanguage>
      <InputLocale>it-IT</InputLocale>
      <SystemLocale>it-IT</SystemLocale>
      <UILanguage>it-IT</UILanguage>
      <UserLocale>it-IT</UserLocale>
    </component>
    <component name="Microsoft-Windows-Setup"
               processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35"
               language="neutral" versionScope="nonSxS">
      <DiskConfiguration>
        <WillShowUI>OnError</WillShowUI>
        <Disk wcm:action="add">
          <DiskID>0</DiskID><WillWipeDisk>true</WillWipeDisk>
          <CreatePartitions>
            <CreatePartition wcm:action="add"><Order>1</Order><Type>EFI</Type><Size>100</Size></CreatePartition>
            <CreatePartition wcm:action="add"><Order>2</Order><Type>MSR</Type><Size>16</Size></CreatePartition>
            <CreatePartition wcm:action="add"><Order>3</Order><Type>Primary</Type><Extend>true</Extend></CreatePartition>
          </CreatePartitions>
          <ModifyPartitions>
            <ModifyPartition wcm:action="add"><Order>1</Order><PartitionID>1</PartitionID><Format>FAT32</Format><Label>System</Label></ModifyPartition>
            <ModifyPartition wcm:action="add"><Order>2</Order><PartitionID>3</PartitionID><Format>NTFS</Format><Label>Windows</Label><Letter>C</Letter></ModifyPartition>
          </ModifyPartitions>
        </Disk>
      </DiskConfiguration>
      <ImageInstall>
        <OSImage>
          <InstallTo><DiskID>0</DiskID><PartitionID>3</PartitionID></InstallTo>
          <InstallFrom>
            <MetaData wcm:action="add"><Key>/IMAGE/NAME</Key><Value>Windows 11 Pro</Value></MetaData>
          </InstallFrom>
          <WillShowUI>OnError</WillShowUI>
        </OSImage>
      </ImageInstall>
      <UserData><AcceptEula>true</AcceptEula><FullName>Utente</FullName><Organization>NovaSCM</Organization></UserData>
    </component>
  </settings>

  <settings pass="specialize">
    <component name="Microsoft-Windows-Shell-Setup"
               processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35"
               language="neutral" versionScope="nonSxS">
      <ComputerName>{xpc_name}</ComputerName>
      <TimeZone>W. Europe Standard Time</TimeZone>
      <RegisteredOrganization>NovaSCM</RegisteredOrganization>{run_sync}
    </component>
    <component name="Microsoft-Windows-International-Core"
               processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35"
               language="neutral" versionScope="nonSxS">
      <InputLocale>it-IT</InputLocale><SystemLocale>it-IT</SystemLocale>
      <UILanguage>it-IT</UILanguage><UserLocale>it-IT</UserLocale>
    </component>{odj_component}
  </settings>

  <settings pass="oobeSystem">
    <component name="Microsoft-Windows-Shell-Setup"
               processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35"
               language="neutral" versionScope="nonSxS">
      <OOBE>
        <HideEULAPage>true</HideEULAPage>
        <HideOEMRegistrationScreen>true</HideOEMRegistrationScreen>
        <HideWirelessSetupInOOBE>true</HideWirelessSetupInOOBE>
        <ProtectYourPC>3</ProtectYourPC>
      </OOBE>
      <UserAccounts>
        <LocalAccounts>
          <LocalAccount wcm:action="add">
            <Name>Administrator</Name>
            <Group>Administrators</Group>
            <Password>
              <Value>{xadmin_pw}</Value>
              <PlainText>true</PlainText>
            </Password>
          </LocalAccount>
        </LocalAccounts>
      </UserAccounts>
      <AutoLogon>
        <Password><Value>{xadmin_pw}</Value><PlainText>true</PlainText></Password>
        <Enabled>true</Enabled><LogonCount>1</LogonCount>
        <Username>Administrator</Username>
      </AutoLogon>
      <FirstLogonCommands>
        <SynchronousCommand wcm:action="add">
          <Order>1</Order>
          <CommandLine>cmd /c for %d in (D E F G H I J K L M N O P Q R S T U V W X Y Z) do if exist %d:\\postinstall.ps1 copy /Y %d:\\postinstall.ps1 C:\\Windows\\postinstall.ps1</CommandLine>
          <Description>NovaSCM: recupera postinstall.ps1</Description>
        </SynchronousCommand>
        <SynchronousCommand wcm:action="add">
          <Order>2</Order>
          <CommandLine>powershell.exe -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File C:\\Windows\\postinstall.ps1</CommandLine>
          <Description>NovaSCM post-install</Description>
        </SynchronousCommand>
      </FirstLogonCommands>
    </component>
  </settings>

</unattend>
<!-- Generato da NovaSCM API — CR #{d['id']} — {_xe(d['pc_name'])} -->"""

    return Response(xml, mimetype="application/xml",
                    headers={"Content-Disposition": "attachment; filename=autounattend.xml"})

# ── Check-in + Step tracking ──────────────────────────────────────────────────

@app.route("/api/cr/by-name/<pc_name>/checkin", methods=["POST"])
@require_auth
def checkin_cr(pc_name):
    now = datetime.datetime.now().isoformat()
    with get_db_ctx() as conn:
        affected = conn.execute("UPDATE cr SET last_seen=? WHERE pc_name=?",
                                (now, pc_name.upper().strip())).rowcount
        conn.commit()
        if affected == 0:
            return jsonify({"error": "CR non trovato"}), 404
        row = conn.execute("SELECT * FROM cr WHERE pc_name=?",
                           (pc_name.upper().strip(),)).fetchone()
    return jsonify({"ok": True, "last_seen": now, "cr": row_to_dict(row)})

# ── Settings ──────────────────────────────────────────────────────────────────

@app.route("/api/settings", methods=["GET"])
@require_auth
def get_settings():
    with get_db_ctx() as conn:
        rows = conn.execute("SELECT key, value FROM settings").fetchall()
    return jsonify({r["key"]: r["value"] for r in rows})

SETTINGS_SCHEMA = {
    "default_workflow_id": int,
    "webhook_url":         str,
    "webhook_enabled":     str,
}

@app.route("/api/settings", methods=["PUT"])
@require_auth
def update_settings():
    data = request.get_json(force=True)
    for key in data:
        if key not in SETTINGS_SCHEMA:
            return jsonify({"error": f"Chiave non valida: {key}. Valori ammessi: {list(SETTINGS_SCHEMA)}"}), 400
    with get_db_ctx() as conn:
        for key, value in data.items():
            try:
                value = SETTINGS_SCHEMA[key](value) if value not in (None, "") else None
            except (ValueError, TypeError):
                return jsonify({"error": f"Tipo non valido per {key}"}), 400
            conn.execute(
                "INSERT INTO settings (key, value) VALUES (?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value",
                (key, str(value) if value is not None else "")
            )
        conn.commit()
        rows = conn.execute("SELECT key, value FROM settings").fetchall()
    return jsonify({r["key"]: r["value"] for r in rows})

@app.route("/api/cr/by-name/<pc_name>/step", methods=["POST"])
@require_auth
def report_step(pc_name):
    data   = request.get_json(force=True)
    step   = data.get("step", "")
    status = data.get("status", "done")
    ts     = datetime.datetime.now().isoformat()
    with get_db_ctx() as conn:
        row = conn.execute("SELECT id FROM cr WHERE pc_name=?",
                           (pc_name.upper().strip(),)).fetchone()
        if not row:
            return jsonify({"error": "CR non trovato"}), 404
        cr_id = row["id"]
        conn.execute("""
            INSERT INTO cr_steps (cr_id, step_name, status, timestamp) VALUES (?,?,?,?)
            ON CONFLICT(cr_id, step_name) DO UPDATE SET status=excluded.status, timestamp=excluded.timestamp
        """, (cr_id, step, status, ts))
        conn.execute("UPDATE cr SET last_seen=? WHERE id=?", (ts, cr_id))
        conn.commit()
    return jsonify({"ok": True, "step": step, "status": status})

@app.route("/api/cr/by-name/<pc_name>/steps", methods=["GET"])
@require_auth
def get_steps_by_name(pc_name):
    """Restituisce CR + steps per hostname — usato da deploy-client.html sul PC client."""
    with get_db_ctx() as conn:
        cr = conn.execute("SELECT * FROM cr WHERE pc_name=?",
                          (pc_name.upper().strip(),)).fetchone()
        if not cr:
            return jsonify({"error": "CR non trovato"}), 404
        steps = conn.execute(
            "SELECT step_name, status, timestamp FROM cr_steps WHERE cr_id=? ORDER BY id ASC",
            (cr["id"],)).fetchall()
    return jsonify({
        "cr_id":    cr["id"],
        "pc_name":  cr["pc_name"],
        "status":   cr["status"],
        "steps":    [dict(s) for s in steps],
    })

@app.route("/api/cr/<int:cr_id>/steps", methods=["GET"])
@require_auth
def get_steps(cr_id):
    page     = max(1, request.args.get("page",     1,   type=int))
    per_page = min(500, max(1, request.args.get("per_page", 100, type=int)))
    offset   = (page - 1) * per_page
    with get_db_ctx() as conn:
        total = conn.execute(
            "SELECT COUNT(*) FROM cr_steps WHERE cr_id=?", (cr_id,)).fetchone()[0]
        rows  = conn.execute(
            "SELECT step_name, status, timestamp FROM cr_steps WHERE cr_id=? "
            "ORDER BY id ASC LIMIT ? OFFSET ?",
            (cr_id, per_page, offset)).fetchall()
    return jsonify({
        "page":     page,
        "per_page": per_page,
        "total":    total,
        "items":    [dict(r) for r in rows],
    })

# ── Workflow Engine — Workflow CRUD ───────────────────────────────────────────

@app.route("/api/workflows", methods=["GET"])
@require_auth
def list_workflows():
    with get_db_ctx() as conn:
        rows = conn.execute("SELECT * FROM workflows ORDER BY id ASC").fetchall()
    return jsonify([dict(r) for r in rows])

@app.route("/api/workflows", methods=["POST"])
@require_auth
def create_workflow():
    data = request.get_json(force=True)
    if not data.get("nome"):
        return jsonify({"error": "Campo obbligatorio: nome"}), 400
    now = datetime.datetime.now().isoformat()
    with get_db_ctx() as conn:
        try:
            conn.execute(
                "INSERT INTO workflows (nome, descrizione, versione, created_at, updated_at) VALUES (?,?,?,?,?)",
                (data["nome"].strip(), data.get("descrizione", ""), data.get("versione", 1), now, now)
            )
            conn.commit()
            row = conn.execute("SELECT * FROM workflows WHERE nome=?", (data["nome"].strip(),)).fetchone()
            return jsonify(dict(row)), 201
        except sqlite3.IntegrityError:
            return jsonify({"error": f"Workflow '{data['nome']}' esiste già"}), 409

@app.route("/api/workflows/<int:wf_id>", methods=["GET"])
@require_auth
def get_workflow(wf_id):
    with get_db_ctx() as conn:
        wf = conn.execute("SELECT * FROM workflows WHERE id=?", (wf_id,)).fetchone()
        if not wf:
            return jsonify({"error": "Non trovato"}), 404
        steps = conn.execute(
            "SELECT * FROM workflow_steps WHERE workflow_id=? ORDER BY ordine ASC", (wf_id,)
        ).fetchall()
    result = dict(wf)
    result["steps"] = [dict(s) for s in steps]
    return jsonify(result)

@app.route("/api/workflows/<int:wf_id>", methods=["PUT"])
@require_auth
def update_workflow(wf_id):
    data = request.get_json(force=True)
    if not data.get("nome", "").strip():
        return jsonify({"error": "Campo obbligatorio: nome"}), 400
    now  = datetime.datetime.now().isoformat()
    with get_db_ctx() as conn:
        conn.execute(
            "UPDATE workflows SET nome=?, descrizione=?, versione=versione+1, updated_at=? WHERE id=?",
            (data.get("nome", "").strip(), data.get("descrizione", ""), now, wf_id)
        )
        conn.commit()
        row = conn.execute("SELECT * FROM workflows WHERE id=?", (wf_id,)).fetchone()
    if not row:
        return jsonify({"error": "Non trovato"}), 404
    return jsonify(dict(row))

@app.route("/api/workflows/<int:wf_id>", methods=["DELETE"])
@require_auth
def delete_workflow(wf_id):
    with get_db_ctx() as conn:
        if not conn.execute("SELECT 1 FROM workflows WHERE id=?", (wf_id,)).fetchone():
            return jsonify({"error": "Non trovato"}), 404
        conn.execute("DELETE FROM workflow_steps WHERE workflow_id=?", (wf_id,))
        conn.execute("DELETE FROM pc_workflows   WHERE workflow_id=?", (wf_id,))
        conn.execute("DELETE FROM workflows      WHERE id=?",          (wf_id,))
        conn.commit()
    return jsonify({"ok": True})

# ── Workflow Engine — Steps CRUD ───────────────────────────────────────────────

@app.route("/api/workflows/<int:wf_id>/steps", methods=["GET"])
@require_auth
def list_steps(wf_id):
    with get_db_ctx() as conn:
        rows = conn.execute(
            "SELECT * FROM workflow_steps WHERE workflow_id=? ORDER BY ordine ASC", (wf_id,)
        ).fetchall()
    return jsonify([dict(r) for r in rows])

@app.route("/api/workflows/<int:wf_id>/export", methods=["GET"])
@require_auth
def export_workflow(wf_id):
    """Esporta workflow completo (metadati + step) come JSON scaricabile."""
    with get_db_ctx() as conn:
        wf = conn.execute("SELECT * FROM workflows WHERE id=?", (wf_id,)).fetchone()
        if not wf:
            return jsonify({"error": "Non trovato"}), 404
        steps = conn.execute(
            "SELECT ordine, nome, tipo, parametri, condizione, su_errore, platform "
            "FROM workflow_steps WHERE workflow_id=? ORDER BY ordine ASC", (wf_id,)
        ).fetchall()
    export_data = {
        "novascm_export": "1.0",
        "exported_at":    datetime.datetime.utcnow().isoformat(),
        "workflow": {
            "nome":        dict(wf)["nome"],
            "descrizione": dict(wf).get("descrizione", ""),
            "steps":       [dict(s) for s in steps],
        }
    }
    return Response(
        json.dumps(export_data, indent=2, ensure_ascii=False),
        mimetype="application/json",
        headers={"Content-Disposition": f"attachment; filename=workflow-{wf_id}.json"}
    )


@app.route("/api/workflows/import", methods=["POST"])
@require_auth
def import_workflow():
    """Importa workflow da JSON esportato con /export."""
    data = request.get_json(force=True)
    if data.get("novascm_export") != "1.0":
        return jsonify({"error": "Formato non valido (atteso novascm_export=1.0)"}), 400
    wf_data = data.get("workflow", {})
    if not wf_data.get("nome"):
        return jsonify({"error": "Campo obbligatorio: workflow.nome"}), 400
    now = datetime.datetime.utcnow().isoformat()
    with get_db_ctx() as conn:
        try:
            conn.execute(
                "INSERT INTO workflows (nome, descrizione, versione, created_at, updated_at) VALUES (?,?,1,?,?)",
                (wf_data["nome"].strip(), wf_data.get("descrizione", ""), now, now)
            )
            conn.commit()
            wf_id = conn.execute(
                "SELECT id FROM workflows WHERE nome=?", (wf_data["nome"].strip(),)
            ).fetchone()["id"]
            for step in wf_data.get("steps", []):
                parametri = step.get("parametri", "{}")
                if isinstance(parametri, dict):
                    parametri = json.dumps(parametri)
                conn.execute("""
                    INSERT INTO workflow_steps
                        (workflow_id, ordine, nome, tipo, parametri, condizione, su_errore, platform)
                    VALUES (?,?,?,?,?,?,?,?)
                """, (
                    wf_id, step["ordine"], step["nome"], step["tipo"],
                    parametri, step.get("condizione", ""),
                    step.get("su_errore", "stop"), step.get("platform", "all")
                ))
            conn.commit()
            return jsonify({"ok": True, "workflow_id": wf_id}), 201
        except sqlite3.IntegrityError:
            return jsonify({"error": f"Workflow '{wf_data['nome']}' esiste già"}), 409


SU_ERRORE_VALID = ("stop", "continue", "retry")

@app.route("/api/workflows/<int:wf_id>/steps", methods=["POST"])
@require_auth
def add_step(wf_id):
    data = request.get_json(force=True)
    for f in ("nome", "tipo", "ordine"):
        if data.get(f) is None:
            return jsonify({"error": f"Campo obbligatorio: {f}"}), 400
    tipi_validi = (
        # Cross-platform
        "shell_script", "file_copy", "reboot", "message",
        # Windows
        "winget_install", "ps_script", "reg_set", "windows_update",
        # Linux
        "apt_install", "snap_install", "systemd_service",
    )
    if data["tipo"] not in tipi_validi:
        return jsonify({"error": f"tipo non valido. Valori: {tipi_validi}"}), 400
    if data.get("su_errore", "stop") not in SU_ERRORE_VALID:
        return jsonify({"error": f"su_errore non valido. Valori: {SU_ERRORE_VALID}"}), 400
    parametri = data.get("parametri", {})
    if isinstance(parametri, dict):
        parametri = json.dumps(parametri)
    with get_db_ctx() as conn:
        try:
            conn.execute("""
                INSERT INTO workflow_steps (workflow_id, ordine, nome, tipo, parametri, condizione, su_errore, platform)
                VALUES (?,?,?,?,?,?,?,?)
            """, (
                wf_id, data["ordine"], data["nome"].strip(), data["tipo"],
                parametri, data.get("condizione", ""), data.get("su_errore", "stop"),
                data.get("platform", "all")
            ))
            conn.execute("UPDATE workflows SET versione=versione+1, updated_at=? WHERE id=?",
                         (datetime.datetime.now().isoformat(), wf_id))
            conn.commit()
            row = conn.execute(
                "SELECT * FROM workflow_steps WHERE workflow_id=? AND ordine=?", (wf_id, data["ordine"])
            ).fetchone()
            return jsonify(dict(row)), 201
        except sqlite3.IntegrityError:
            return jsonify({"error": f"Ordine {data['ordine']} già esistente in questo workflow"}), 409

@app.route("/api/workflows/<int:wf_id>/steps/<int:step_id>", methods=["PUT"])
@require_auth
def update_step(wf_id, step_id):
    data = request.get_json(force=True)
    tipi_validi = (
        "shell_script", "file_copy", "reboot", "message",
        "winget_install", "ps_script", "reg_set", "windows_update",
        "apt_install", "snap_install", "systemd_service",
    )
    if data.get("tipo") and data["tipo"] not in tipi_validi:
        return jsonify({"error": f"tipo non valido. Valori: {tipi_validi}"}), 400
    if data.get("su_errore") and data["su_errore"] not in SU_ERRORE_VALID:
        return jsonify({"error": f"su_errore non valido. Valori: {SU_ERRORE_VALID}"}), 400
    parametri = data.get("parametri", {})
    if isinstance(parametri, dict):
        parametri = json.dumps(parametri)
    with get_db_ctx() as conn:
        conn.execute("""
            UPDATE workflow_steps SET ordine=?, nome=?, tipo=?, parametri=?, condizione=?, su_errore=?, platform=?
            WHERE id=? AND workflow_id=?
        """, (
            data.get("ordine"), data.get("nome", "").strip(), data.get("tipo"),
            parametri, data.get("condizione", ""), data.get("su_errore", "stop"),
            data.get("platform", "all"),
            step_id, wf_id
        ))
        conn.execute("UPDATE workflows SET versione=versione+1, updated_at=? WHERE id=?",
                     (datetime.datetime.now().isoformat(), wf_id))
        conn.commit()
        row = conn.execute("SELECT * FROM workflow_steps WHERE id=?", (step_id,)).fetchone()
    if not row:
        return jsonify({"error": "Step non trovato"}), 404
    return jsonify(dict(row))

@app.route("/api/workflows/<int:wf_id>/steps/<int:step_id>", methods=["DELETE"])
@require_auth
def delete_step(wf_id, step_id):
    with get_db_ctx() as conn:
        affected = conn.execute(
            "DELETE FROM workflow_steps WHERE id=? AND workflow_id=?", (step_id, wf_id)
        ).rowcount
        conn.execute("UPDATE workflows SET versione=versione+1, updated_at=? WHERE id=?",
                     (datetime.datetime.now().isoformat(), wf_id))
        conn.commit()
    if affected == 0:
        return jsonify({"error": "Step non trovato"}), 404
    return jsonify({"ok": True})

# ── Workflow Engine — PC Assignments ──────────────────────────────────────────

@app.route("/api/pc-workflows", methods=["GET"])
@require_auth
def list_pc_workflows():
    with get_db_ctx() as conn:
        rows = conn.execute("""
            SELECT pw.*, w.nome as workflow_nome
            FROM pc_workflows pw
            JOIN workflows w ON w.id = pw.workflow_id
            WHERE COALESCE(pw.archived, 0) = 0
            ORDER BY pw.id DESC
        """).fetchall()
    return jsonify([dict(r) for r in rows])


@app.route("/api/pc-workflows/history", methods=["GET"])
@require_auth
def pc_workflow_history():
    """Storico completo deploy per un PC (inclusi archiviati)."""
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

@app.route("/api/pc-workflows", methods=["POST"])
@require_auth
def assign_workflow():
    data = request.get_json(force=True)
    for f in ("pc_name", "workflow_id"):
        if not data.get(f):
            return jsonify({"error": f"Campo obbligatorio: {f}"}), 400
    now = datetime.datetime.now().isoformat()
    with get_db_ctx() as conn:
        wf = conn.execute("SELECT id FROM workflows WHERE id=?", (data["workflow_id"],)).fetchone()
        if not wf:
            return jsonify({"error": "Workflow non trovato"}), 404
        try:
            conn.execute(
                "INSERT INTO pc_workflows (pc_name, workflow_id, status, assigned_at) VALUES (?,?,?,?)",
                (data["pc_name"].upper().strip(), data["workflow_id"], "pending", now)
            )
            conn.commit()
            row = conn.execute(
                "SELECT * FROM pc_workflows WHERE pc_name=? AND workflow_id=?",
                (data["pc_name"].upper().strip(), data["workflow_id"])
            ).fetchone()
            return jsonify(dict(row)), 201
        except sqlite3.IntegrityError:
            return jsonify({"error": "Workflow già assegnato a questo PC"}), 409

@app.route("/api/pc-workflows/<int:pw_id>", methods=["GET"])
@require_auth
def get_pc_workflow(pw_id):
    with get_db_ctx() as conn:
        pw = conn.execute("""
            SELECT pw.*, w.nome as workflow_nome
            FROM pc_workflows pw JOIN workflows w ON w.id=pw.workflow_id
            WHERE pw.id=?
        """, (pw_id,)).fetchone()
        if not pw:
            return jsonify({"error": "Non trovato"}), 404
        steps = conn.execute("""
            SELECT ws.id as step_id, ws.ordine, ws.nome, ws.tipo, ws.parametri, ws.su_errore,
                   COALESCE(pws.status,'pending') as status,
                   COALESCE(pws.elapsed_sec, 0) as elapsed_sec,
                   0 as est_sec,
                   pws.output as log, pws.timestamp
            FROM workflow_steps ws
            LEFT JOIN pc_workflow_steps pws ON pws.step_id=ws.id AND pws.pc_workflow_id=?
            WHERE ws.workflow_id=?
            ORDER BY ws.ordine ASC
        """, (pw_id, dict(pw)["workflow_id"])).fetchall()
    result = dict(pw)
    hw_raw = result.pop("hardware_json", None)
    result["hardware"]   = json.loads(hw_raw) if hw_raw else None
    result["screenshot"] = result.pop("screenshot_b64", None)
    result["log"]        = result.pop("log_text", None)
    result["steps"] = [dict(s) for s in steps]
    return jsonify(result)

@app.route("/api/pc-workflows/<int:pw_id>", methods=["DELETE"])
@require_auth
def delete_pc_workflow(pw_id):
    with get_db_ctx() as conn:
        affected = conn.execute("DELETE FROM pc_workflows WHERE id=?", (pw_id,)).rowcount
        conn.commit()
    if affected == 0:
        return jsonify({"error": "Non trovato"}), 404
    return jsonify({"ok": True})

# ── Deploy auto-start ─────────────────────────────────────────────────────────

DEPLOY_WIN11_STEPS = [
    ( 1, "Partizionamento disco",         "ps_script"),
    ( 2, "Formattazione partizioni",       "ps_script"),
    ( 3, "Installazione Windows 11",       "windows_update"),
    ( 4, "Configurazione OOBE",            "ps_script"),
    ( 5, "Driver chipset",                 "winget_install"),
    ( 6, "Driver scheda di rete",          "winget_install"),
    ( 7, "Driver audio",                   "winget_install"),
    ( 8, "Driver GPU",                     "winget_install"),
    ( 9, "Windows Update — critico",       "windows_update"),
    (10, "Windows Update — cumulativo",    "windows_update"),
    (11, "Visual C++ 2022",                "winget_install"),
    (12, ".NET Runtime 8",                 "winget_install"),
    (13, "Agente sicurezza",               "winget_install"),
    (14, "Configurazione firewall",        "ps_script"),
    (15, "Join dominio / workgroup",       "ps_script"),
    (16, "Sincronizzazione GPO",           "ps_script"),
    (17, "Registrazione certificato",      "ps_script"),
    (18, "Installazione applicazioni",     "winget_install"),
    (19, "Configurazione Outlook",         "reg_set"),
    (20, "OneDrive",                       "reg_set"),
    (21, "Profilo predefinito",            "reg_set"),
    (22, "Agente NovaSCM",                 "ps_script"),
    (23, "Pulizia file temporanei",        "shell_script"),
    (24, "Riavvio finale",                 "reboot"),
]
DEPLOY_WF_NAME = "Deploy Windows 11 Pro"

@app.route("/api/deploy/start", methods=["POST"])
@require_auth
def deploy_start():
    """Crea (o riusa) il workflow standard Deploy Win11, assegna al PC, ritorna pw_id.
    Body: { "pc_name": "PC-XXXXX" }"""
    data    = request.get_json(force=True)
    pc_name = data.get("pc_name", "").upper().strip()
    if not pc_name:
        return jsonify({"error": "pc_name obbligatorio"}), 400
    now = datetime.datetime.now().isoformat()
    with get_db_ctx() as conn:
        # Trova o crea workflow standard
        wf = conn.execute("SELECT id FROM workflows WHERE nome=?", (DEPLOY_WF_NAME,)).fetchone()
        if not wf:
            conn.execute(
                "INSERT INTO workflows (nome, descrizione, versione, created_at, updated_at) VALUES (?,?,?,?,?)",
                (DEPLOY_WF_NAME, "Deploy automatico Windows 11 Pro via PXE/MDT", 1, now, now)
            )
            conn.commit()
            wf = conn.execute("SELECT id FROM workflows WHERE nome=?", (DEPLOY_WF_NAME,)).fetchone()
            wf_id = wf["id"]
            for (ordine, nome, tipo) in DEPLOY_WIN11_STEPS:
                conn.execute(
                    "INSERT INTO workflow_steps (workflow_id, ordine, nome, tipo, parametri, su_errore) VALUES (?,?,?,?,?,?)",
                    (wf_id, ordine, nome, tipo, "{}", "continue")
                )
            conn.commit()
        else:
            wf_id = wf["id"]
        # M-6: archivia il workflow precedente invece di cancellarlo
        conn.execute(
            "UPDATE pc_workflows SET archived=1 WHERE pc_name=? AND workflow_id=? AND archived=0",
            (pc_name, wf_id)
        )
        conn.commit()
        # Crea nuovo pc_workflow in stato running
        conn.execute(
            "INSERT INTO pc_workflows (pc_name, workflow_id, status, assigned_at) VALUES (?,?,?,?)",
            (pc_name, wf_id, "running", now)
        )
        conn.commit()
        pw = conn.execute(
            "SELECT id FROM pc_workflows WHERE pc_name=? AND workflow_id=? ORDER BY id DESC LIMIT 1",
            (pc_name, wf_id)
        ).fetchone()
    return jsonify({"pw_id": pw["id"], "workflow_id": wf_id, "pc_name": pc_name}), 201


@app.route("/api/deploy/<int:pw_id>/step", methods=["POST"])
@require_auth
def deploy_step(pw_id):
    """Aggiorna stato di uno step per ordine. Body: { ordine, status, output?, elapsed_sec? }"""
    data    = request.get_json(force=True)
    ordine  = data.get("ordine")
    status  = data.get("status", "done")
    output  = data.get("output", "")
    elapsed = data.get("elapsed_sec", 0)
    now     = datetime.datetime.now().isoformat()
    if ordine is None:
        return jsonify({"error": "ordine obbligatorio"}), 400
    with get_db_ctx() as conn:
        pw = conn.execute("SELECT workflow_id FROM pc_workflows WHERE id=?", (pw_id,)).fetchone()
        if not pw:
            return jsonify({"error": "pc_workflow non trovato"}), 404
        ws = conn.execute(
            "SELECT id FROM workflow_steps WHERE workflow_id=? AND ordine=?",
            (pw["workflow_id"], ordine)
        ).fetchone()
        if not ws:
            return jsonify({"ok": True, "skipped": True})
        conn.execute("""
            INSERT INTO pc_workflow_steps
                (pc_workflow_id, step_id, status, output, timestamp, elapsed_sec)
            VALUES (?,?,?,?,?,?)
            ON CONFLICT(pc_workflow_id, step_id) DO UPDATE SET
                status=excluded.status, output=excluded.output,
                timestamp=excluded.timestamp, elapsed_sec=excluded.elapsed_sec
        """, (pw_id, ws["id"], status, output, now, elapsed))
        # Aggiorna stato globale pc_workflow
        if status == "error":
            conn.execute("UPDATE pc_workflows SET status='failed' WHERE id=?", (pw_id,))
        else:
            total = conn.execute(
                "SELECT COUNT(*) FROM workflow_steps WHERE workflow_id=?", (pw["workflow_id"],)
            ).fetchone()[0]
            done = conn.execute(
                "SELECT COUNT(*) FROM pc_workflow_steps WHERE pc_workflow_id=? AND status='done'",
                (pw_id,)
            ).fetchone()[0]
            if done >= total:
                conn.execute(
                    "UPDATE pc_workflows SET status='completed', completed_at=? WHERE id=?",
                    (now, pw_id)
                )
        conn.commit()
    return jsonify({"ok": True})


# ── DeployScreen endpoints ────────────────────────────────────────────────────

@app.route("/api/pc-workflows/<int:pw_id>/hardware", methods=["POST"])
@require_auth
def post_pc_workflow_hardware(pw_id):
    data = request.get_json(force=True)
    with get_db_ctx() as conn:
        affected = conn.execute(
            "UPDATE pc_workflows SET hardware_json=? WHERE id=?",
            (json.dumps(data), pw_id)
        ).rowcount
        conn.commit()
    if affected == 0:
        return jsonify({"error": "Non trovato"}), 404
    return jsonify({"ok": True})

@app.route("/api/pc-workflows/<int:pw_id>/log", methods=["POST"])
@require_auth
def post_pc_workflow_log(pw_id):
    data = request.get_json(force=True)
    text = data.get("text", "")
    with get_db_ctx() as conn:
        affected = conn.execute(
            "UPDATE pc_workflows SET log_text=? WHERE id=?",
            (text, pw_id)
        ).rowcount
        conn.commit()
    if affected == 0:
        return jsonify({"error": "Non trovato"}), 404
    return jsonify({"ok": True})

@app.route("/api/pc-workflows/<int:pw_id>/screenshot", methods=["POST"])
@require_auth
def post_pc_workflow_screenshot(pw_id):
    data = request.get_json(force=True)
    b64 = data.get("image_b64", "")
    with get_db_ctx() as conn:
        affected = conn.execute(
            "UPDATE pc_workflows SET screenshot_b64=? WHERE id=?",
            (b64, pw_id)
        ).rowcount
        conn.commit()
    if affected == 0:
        return jsonify({"error": "Non trovato"}), 404
    return jsonify({"ok": True})

# ── Workflow Engine — Agent endpoints ─────────────────────────────────────────

@app.route("/api/pc/<pc_name>/workflow", methods=["GET"])
@require_auth
def get_pc_assigned_workflow(pc_name):
    """Agent: scarica il workflow assegnato al PC.
    Auto-assign logic:
      1. Cerca workflow pending/running già assegnato a questo PC
      2. Se non trovato: cerca workflow_id nella CR del PC
      3. Se non trovato: usa default_workflow_id dalle impostazioni
    """
    pc = pc_name.upper().strip()
    now = datetime.datetime.now().isoformat()
    with get_db_ctx() as conn:
        # 1. Cerca assegnazione esistente
        pw = conn.execute("""
            SELECT pw.*, w.nome as workflow_nome
            FROM pc_workflows pw JOIN workflows w ON w.id=pw.workflow_id
            WHERE pw.pc_name=? AND pw.status IN ('pending','running')
            ORDER BY pw.id DESC LIMIT 1
        """, (pc,)).fetchone()

        # 2. Auto-assign: cerca workflow_id nella CR
        if not pw:
            cr = conn.execute(
                "SELECT workflow_id FROM cr WHERE pc_name=? AND workflow_id IS NOT NULL", (pc,)
            ).fetchone()
            wf_id = cr["workflow_id"] if cr else None

            # 3. Fallback: default workflow dalle impostazioni
            if not wf_id:
                setting = conn.execute(
                    "SELECT value FROM settings WHERE key='default_workflow_id'"
                ).fetchone()
                wf_id = int(setting["value"]) if setting and setting["value"] else None

            if wf_id:
                wf_exists = conn.execute("SELECT id FROM workflows WHERE id=?", (wf_id,)).fetchone()
                if wf_exists:
                    conn.execute(
                        "INSERT OR IGNORE INTO pc_workflows (pc_name, workflow_id, status, assigned_at) VALUES (?,?,?,?)",
                        (pc, wf_id, "pending", now)
                    )
                    conn.commit()
                    pw = conn.execute("""
                        SELECT pw.*, w.nome as workflow_nome
                        FROM pc_workflows pw JOIN workflows w ON w.id=pw.workflow_id
                        WHERE pw.pc_name=? AND pw.status IN ('pending','running')
                        ORDER BY pw.id DESC LIMIT 1
                    """, (pc,)).fetchone()

        if not pw:
            return jsonify({"error": "Nessun workflow assegnato"}), 404

        pw_dict = dict(pw)
        steps = conn.execute("""
            SELECT ws.id as step_id, ws.ordine, ws.nome, ws.tipo, ws.parametri,
                   ws.condizione, ws.su_errore, ws.platform,
                   COALESCE(pws.status,'pending') as status
            FROM workflow_steps ws
            LEFT JOIN pc_workflow_steps pws ON pws.step_id=ws.id AND pws.pc_workflow_id=?
            WHERE ws.workflow_id=?
            ORDER BY ws.ordine ASC
        """, (pw_dict["id"], pw_dict["workflow_id"])).fetchall()

        if pw_dict["status"] == "pending":
            conn.execute("UPDATE pc_workflows SET status='running', started_at=? WHERE id=?",
                         (now, pw_dict["id"]))
            conn.commit()
            pw_dict["status"] = "running"

    pw_dict["steps"] = [dict(s) for s in steps]
    return jsonify(pw_dict)

@app.route("/api/pc/<pc_name>/workflow/step", methods=["POST"])
@require_auth
def report_wf_step(pc_name):
    """Agent: riporta lo stato di uno step di un workflow."""
    data        = request.get_json(force=True)
    step_id     = data.get("step_id")
    status      = data.get("status", "done")  # running | done | error | skipped
    output      = data.get("output", "")
    elapsed_sec = float(data.get("elapsed_sec") or 0)
    ts          = datetime.datetime.now().isoformat()
    if not step_id:
        return jsonify({"error": "Campo obbligatorio: step_id"}), 400
    with get_db_ctx() as conn:
        pw = conn.execute(
            "SELECT id, workflow_id FROM pc_workflows WHERE pc_name=? AND status='running' ORDER BY id DESC LIMIT 1",
            (pc_name.upper().strip(),)
        ).fetchone()
        if not pw:
            return jsonify({"error": "Nessun workflow in esecuzione"}), 404
        pw_id = pw["id"]
        conn.execute("""
            INSERT INTO pc_workflow_steps (pc_workflow_id, step_id, status, output, timestamp, elapsed_sec)
            VALUES (?,?,?,?,?,?)
            ON CONFLICT(pc_workflow_id, step_id)
            DO UPDATE SET status=excluded.status, output=excluded.output,
                          timestamp=excluded.timestamp, elapsed_sec=excluded.elapsed_sec
        """, (pw_id, step_id, status, output, ts, elapsed_sec))
        conn.execute("UPDATE pc_workflows SET last_seen=? WHERE id=?", (ts, pw_id))
        # Se tutti gli step sono done/skipped → completa workflow
        total = conn.execute(
            "SELECT COUNT(*) FROM workflow_steps WHERE workflow_id=?", (pw["workflow_id"],)
        ).fetchone()[0]
        done = conn.execute("""
            SELECT COUNT(*) FROM pc_workflow_steps
            WHERE pc_workflow_id=? AND status IN ('done','skipped','error')
        """, (pw_id,)).fetchone()[0]
        workflow_completed = False
        if total > 0 and done >= total:
            conn.execute("UPDATE pc_workflows SET status='completed', completed_at=? WHERE id=?",
                         (ts, pw_id))
            workflow_completed = True
        conn.commit()
    if workflow_completed:
        _fire_webhook("workflow_completed", {
            "pc_name": pc_name.upper().strip(),
            "pw_id":   pw_id,
            "status":  "completed",
        })
    return jsonify({"ok": True, "step_id": step_id, "status": status})

@app.route("/api/pc/<pc_name>/workflow/checkin", methods=["POST"])
@require_auth
def checkin_wf(pc_name):
    """Agent: heartbeat durante esecuzione workflow."""
    now = datetime.datetime.now().isoformat()
    with get_db_ctx() as conn:
        affected = conn.execute(
            "UPDATE pc_workflows SET last_seen=? WHERE pc_name=? AND status='running'",
            (now, pc_name.upper().strip())
        ).rowcount
        conn.commit()
    if affected == 0:
        return jsonify({"error": "Nessun workflow in esecuzione"}), 404
    return jsonify({"ok": True, "last_seen": now})

# ── Auto-update ───────────────────────────────────────────────────────────────

VERSION_FILE = os.path.join(os.path.dirname(DB), "version.json")
EXE_FILE     = os.path.join(os.path.dirname(DB), "NovaSCM.exe")

@app.route("/api/version", methods=["GET"])
@require_auth
def get_version():
    """Restituisce la versione corrente disponibile per il download."""
    if os.path.exists(VERSION_FILE):
        try:
            return jsonify(json.loads(open(VERSION_FILE).read()))
        except Exception:
            pass
    return jsonify({"version": "1.0.0", "url": "", "notes": ""}), 200

@app.route("/api/download/NovaSCM.exe", methods=["GET"])
@require_auth
def download_exe():
    """Serve il file NovaSCM.exe per l'auto-update."""
    if not os.path.exists(EXE_FILE):
        return jsonify({"error": "File non disponibile"}), 404
    from flask import send_file
    return send_file(EXE_FILE, as_attachment=True,
                     download_name="NovaSCM.exe",
                     mimetype="application/octet-stream")

@app.route("/api/download/setup", methods=["GET"])
@require_auth
def download_setup():
    """Serve l'installer per nuovi utenti."""
    setup_file = os.path.join(os.path.dirname(DB), "NovaSCM-Setup.exe")
    if not os.path.exists(setup_file):
        return jsonify({"error": "Installer non disponibile"}), 404
    from flask import send_file
    return send_file(setup_file, as_attachment=True,
                     download_name="NovaSCM-Setup.exe",
                     mimetype="application/octet-stream")

@app.route("/api/download/agent.sha256", methods=["GET"])
@require_auth
def download_agent_sha256():
    """Restituisce l'hash SHA256 del file agente corrente (per verifica integrità)."""
    import hashlib
    ua      = request.headers.get("User-Agent", "").lower()
    is_linux = "linux" in ua or request.args.get("os") == "linux"
    fname   = "NovaSCMAgent-linux" if is_linux else "NovaSCMAgent.exe"
    fpath   = os.path.join(os.path.dirname(DB), fname)
    if not os.path.exists(fpath):
        return jsonify({"error": f"{fname} non disponibile"}), 404
    h = hashlib.sha256()
    with open(fpath, "rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            h.update(chunk)
    return Response(h.hexdigest(), mimetype="text/plain")

@app.route("/api/download/agent", methods=["GET"])
@require_auth
def download_agent():
    """Serve NovaSCMAgent.exe (Windows) o NovaSCMAgent-linux (Linux) in base all'User-Agent."""
    ua = request.headers.get("User-Agent", "").lower()
    is_linux = "linux" in ua or request.args.get("os") == "linux"
    fname  = "NovaSCMAgent-linux" if is_linux else "NovaSCMAgent.exe"
    fpath  = os.path.join(os.path.dirname(DB), fname)
    if not os.path.exists(fpath):
        return jsonify({"error": f"{fname} non disponibile sul server"}), 404
    from flask import send_file
    return send_file(fpath, as_attachment=True, download_name=fname,
                     mimetype="application/octet-stream")

@app.route("/api/enrollment-token", methods=["POST"])
@require_auth
def create_enrollment_token():
    """Genera un token monouso (1h) per l'enrollment agent — M-7."""
    import uuid as _uuid
    token = str(_uuid.uuid4())
    now   = datetime.datetime.utcnow().isoformat()
    exp   = time.time() + 3600
    with get_db_ctx() as conn:
        conn.execute(
            "INSERT INTO enrollment_tokens (token, expires_at, used, created_at) VALUES (?,?,0,?)",
            (token, exp, now)
        )
        conn.commit()
    return jsonify({"token": token, "expires_at": exp, "expires_in_sec": 3600})

def _validate_enrollment_token(token: str) -> bool:
    """Verifica token monouso: deve esistere, non essere scaduto e non essere già usato."""
    with get_db_ctx() as conn:
        row = conn.execute(
            "SELECT expires_at, used FROM enrollment_tokens WHERE token=?", (token,)
        ).fetchone()
        if not row or row["used"] or time.time() > row["expires_at"]:
            return False
        conn.execute("UPDATE enrollment_tokens SET used=1 WHERE token=?", (token,))
        conn.commit()
    return True

@app.route("/api/download/agent-install.ps1", methods=["GET"])
@require_auth
def download_agent_installer_ps1():
    """Genera install-windows.ps1 con token enrollment monouso (M-7)."""
    api_url = _get_public_url()
    # M-7: genera token monouso invece di iniettare API key master
    import uuid as _uuid
    token = str(_uuid.uuid4())
    exp   = time.time() + 3600
    with get_db_ctx() as conn:
        conn.execute(
            "INSERT INTO enrollment_tokens (token, expires_at, used, created_at) VALUES (?,?,0,?)",
            (token, exp, datetime.datetime.utcnow().isoformat())
        )
        conn.commit()
    api_key = token  # token monouso al posto della chiave master
    # SEC-2 + C-7 + M-1: scarica in temp, verifica SHA256, scrivi config con api_key, installa via sc.exe
    ps1 = f"""#Requires -RunAsAdministrator
# NovaSCM Agent Installer — generato automaticamente
$ErrorActionPreference = "Stop"
$ApiUrl   = "{api_url}"
$ApiKey   = "{api_key}"
$AgentDir = "$env:ProgramData\\NovaSCM"
$AgentExe = "$AgentDir\\NovaSCMAgent.exe"
$CfgFile  = "$AgentDir\\agent.json"
$SvcName  = "NovaSCMAgent"

New-Item -ItemType Directory -Force -Path $AgentDir | Out-Null
New-Item -ItemType Directory -Force -Path "$AgentDir\\logs" | Out-Null

# Scarica e verifica SHA256
$TmpAgent = Join-Path $env:TEMP "NovaSCMAgent_$([System.IO.Path]::GetRandomFileName()).exe"
try {{
    $Headers = @{{ "X-Api-Key" = $ApiKey }}
    Invoke-WebRequest -Uri "$ApiUrl/api/download/agent" -OutFile $TmpAgent -UseBasicParsing -Headers $Headers
    $Expected = (Invoke-WebRequest -Uri "$ApiUrl/api/download/agent.sha256" -UseBasicParsing -Headers $Headers).Content.Trim()
    $Actual   = (Get-FileHash $TmpAgent -Algorithm SHA256).Hash
    if (-not ($Actual -ieq $Expected)) {{
        Write-Error "Hash mismatch: atteso $Expected, ottenuto $Actual"; exit 1
    }}
    Copy-Item $TmpAgent $AgentExe -Force
}} finally {{
    if (Test-Path $TmpAgent) {{ Remove-Item -Force $TmpAgent }}
}}

# Scrivi config con api_key
@{{ api_url = $ApiUrl; api_key = $ApiKey; pc_name = $env:COMPUTERNAME.ToUpper(); poll_sec = 60 }} |
    ConvertTo-Json | Set-Content -Path $CfgFile -Encoding UTF8

# Installa come Windows Service
$existing = Get-Service -Name $SvcName -ErrorAction SilentlyContinue
if ($existing) {{ sc.exe delete $SvcName; Start-Sleep 2 }}
sc.exe create $SvcName binPath= "`"$AgentExe`"" start= auto DisplayName= "NovaSCM Agent"
sc.exe description $SvcName "NovaSCM Workflow Agent"
sc.exe start $SvcName
Write-Host "[NovaSCM] Installato. Config: $CfgFile"
"""
    return Response(ps1, mimetype="text/plain",
                    headers={"Content-Disposition": "attachment; filename=agent-install.ps1"})

@app.route("/api/download/agent-install.sh", methods=["GET"])
@require_auth
def download_agent_installer_sh():
    """Genera install-linux.sh con token enrollment monouso (M-7)."""
    api_url = _get_public_url()
    import uuid as _uuid
    token = str(_uuid.uuid4())
    exp   = time.time() + 3600
    with get_db_ctx() as conn:
        conn.execute(
            "INSERT INTO enrollment_tokens (token, expires_at, used, created_at) VALUES (?,?,0,?)",
            (token, exp, datetime.datetime.utcnow().isoformat())
        )
        conn.commit()
    api_key = token
    # SEC-2 + C-7 + M-1: scarica in temp, verifica SHA256, scrivi config con api_key, installa via systemd
    sh = f"""#!/bin/bash
# NovaSCM Agent Installer — generato automaticamente
set -euo pipefail
API_URL="{api_url}"
API_KEY="{api_key}"
PC_NAME=$(hostname | tr '[:lower:]' '[:upper:]')
AGENT_DIR="/opt/novascm-agent"
CONFIG_DIR="/etc/novascm"
STATE_DIR="/var/lib/novascm"
LOG_DIR="/var/log/novascm"
SVC_NAME="novascm-agent"
PYTHON=$(command -v python3 || {{ apt-get install -y -qq python3 && command -v python3; }})

mkdir -p "$AGENT_DIR" "$CONFIG_DIR" "$STATE_DIR" "$LOG_DIR"

TMPAGENT=$(mktemp /tmp/novascm-agent-XXXXXX)
trap 'rm -f "$TMPAGENT"' EXIT

curl -fsSL -H "X-Api-Key: $API_KEY" "$API_URL/api/download/agent" -o "$TMPAGENT"
EXPECTED=$(curl -fsSL -H "X-Api-Key: $API_KEY" "$API_URL/api/download/agent.sha256?os=linux")
ACTUAL=$(sha256sum "$TMPAGENT" | awk '{{print $1}}')
if [ "$ACTUAL" != "$EXPECTED" ]; then
    echo "Hash mismatch: atteso $EXPECTED, ottenuto $ACTUAL" >&2; exit 1
fi
cp "$TMPAGENT" "$AGENT_DIR/novascm-agent.py"
chmod +x "$AGENT_DIR/novascm-agent.py"

cat > "$CONFIG_DIR/agent.json" << AGENTCFG
{{
  "api_url":  "$API_URL",
  "api_key":  "$API_KEY",
  "pc_name":  "$PC_NAME",
  "poll_sec": 60
}}
AGENTCFG

id novascm &>/dev/null || useradd --system --no-create-home --shell /usr/sbin/nologin novascm
chown -R novascm:novascm "$AGENT_DIR" "$STATE_DIR" "$LOG_DIR"

cat > "/etc/systemd/system/$SVC_NAME.service" << SYSD
[Unit]
Description=NovaSCM Workflow Agent
After=network-online.target

[Service]
Type=simple
User=novascm
ExecStart=$PYTHON $AGENT_DIR/novascm-agent.py
Restart=always
RestartSec=30
StandardOutput=append:$LOG_DIR/agent.log
StandardError=append:$LOG_DIR/agent.log
ProtectSystem=strict
ProtectHome=true
NoNewPrivileges=true
ReadWritePaths=$STATE_DIR $LOG_DIR

[Install]
WantedBy=multi-user.target
SYSD

systemctl daemon-reload
systemctl enable "$SVC_NAME"
systemctl restart "$SVC_NAME"
echo "[NovaSCM] Installato. Config: $CONFIG_DIR/agent.json"
"""
    return Response(sh, mimetype="text/plain",
                    headers={"Content-Disposition": "attachment; filename=agent-install.sh"})

# ── PXE helpers ───────────────────────────────────────────────────────────────

def _normalize_mac(mac: str) -> str:
    """Normalizza MAC in formato AA:BB:CC:DD:EE:FF uppercase."""
    clean = re.sub(r"[^0-9a-fA-F]", "", mac)
    if len(clean) != 12:
        return ""
    return ":".join(clean[i:i+2].upper() for i in range(0, 12, 2))


def _get_pxe_settings() -> dict:
    """Legge tutte le settings con prefisso 'pxe_' dalla tabella settings."""
    with get_db_ctx() as conn:
        rows = conn.execute(
            "SELECT key, value FROM settings WHERE key LIKE 'pxe_%'"
        ).fetchall()
    return {r["key"][4:]: r["value"] for r in rows}  # rimuove prefisso 'pxe_'


def _generate_pc_name(mac: str, cfg: dict) -> str:
    """Genera nome PC da MAC: prefisso + ultimi 6 char MAC senza ':'."""
    prefix = cfg.get("pc_prefix", "PC")
    suffix = mac.replace(":", "")[-6:].upper()
    return f"{prefix}-{suffix}"


def _ipxe_deploy(pc_name: str, iventoy_ip: str, iventoy_port: str) -> str:
    return f"""#!ipxe
echo NovaSCM Deploy -- {pc_name}
echo Connessione a iVentoy {iventoy_ip}:{iventoy_port}...
set iventoy http://{iventoy_ip}:{iventoy_port}
chain ${{iventoy}}/boot.ipxe || goto local_boot

:local_boot
echo iVentoy non raggiungibile -- boot da disco locale
sanboot --no-describe --drive 0x80
"""


def _ipxe_local(label: str) -> str:
    return f"""#!ipxe
echo NovaSCM -- {label}
echo Nessun deploy assegnato -- boot da disco locale
sanboot --no-describe --drive 0x80
"""


def _ipxe_block(pc_name: str) -> str:
    return f"""#!ipxe
echo NovaSCM -- {pc_name}
echo ATTENZIONE: questo PC e' bloccato -- contattare l'amministratore
prompt Premi Invio per spegnere...
poweroff
"""


# ── PXE BOOT SCRIPT ───────────────────────────────────────────────────────────

_PXE_ALLOWED_NETS = [
    s.strip() for s in os.environ.get("NOVASCM_PXE_ALLOWED_NETS", "192.168.0.0/16,10.0.0.0/8,172.16.0.0/12").split(",")
    if s.strip()
]

def _ip_in_allowed_nets(ip: str) -> bool:
    """C-6: verifica che l'IP client sia in una delle subnet consentite per PXE."""
    import ipaddress
    try:
        client = ipaddress.ip_address(ip)
        return any(client in ipaddress.ip_network(net, strict=False) for net in _PXE_ALLOWED_NETS)
    except ValueError:
        return False

@app.route("/api/boot/<mac>", methods=["GET"])
@limiter.limit("30 per minute; 200 per hour")
def pxe_boot_script(mac: str):
    """
    Endpoint iPXE — NESSUNA autenticazione (iPXE non può inviare header).
    C-6: accettato solo da subnet LAN configurate (NOVASCM_PXE_ALLOWED_NETS).
    Risponde con script iPXE testuale.
    """
    norm_mac = _normalize_mac(mac)
    if not norm_mac:
        log.warning("Boot PXE: MAC non valido: %s", mac)
        return _ipxe_local("MAC non valido"), 200, {"Content-Type": "text/plain"}

    client_ip = request.headers.get("X-Forwarded-For", request.remote_addr or "").split(",")[0].strip()
    # C-6: rifiuta richieste da IP non in subnet LAN consentite
    if not _ip_in_allowed_nets(client_ip):
        log.warning("Boot PXE: IP non autorizzato: %s MAC: %s", client_ip, mac)
        return _ipxe_local("IP non autorizzato"), 200, {"Content-Type": "text/plain"}
    now = datetime.datetime.utcnow().isoformat()
    pxe_cfg = _get_pxe_settings()

    with get_db_ctx() as conn:
        host = conn.execute(
            "SELECT * FROM pxe_hosts WHERE mac=?", (norm_mac,)
        ).fetchone()

        if not host:
            pc_name = _generate_pc_name(norm_mac, pxe_cfg)
            log.info("PXE: MAC sconosciuto %s — auto-creo host '%s'", norm_mac, pc_name)
            cr_id = None
            if pxe_cfg.get("auto_provision", "0") == "1":
                try:
                    # M-5: non salvare credenziali nelle CR auto; richiedere approvazione manuale
                    conn.execute("""
                        INSERT OR IGNORE INTO cr
                            (pc_name, software, status, created_at, workflow_id)
                        VALUES (?,?,?,?,?)
                    """, (
                        pc_name,
                        "[]", "pending_approval", now,
                        int(pxe_cfg["default_workflow_id"])
                        if pxe_cfg.get("default_workflow_id") else None,
                    ))
                    cr_row = conn.execute(
                        "SELECT id FROM cr WHERE pc_name=?", (pc_name,)
                    ).fetchone()
                    cr_id = cr_row["id"] if cr_row else None
                except Exception as exc:
                    log.warning("PXE auto-provision CR fallito: %s", exc)

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

        # I-6: aggiornamento atomico boot_count
        conn.execute("BEGIN IMMEDIATE")
        conn.execute("""
            UPDATE pxe_hosts
            SET last_boot_at=?, boot_count=boot_count+1, last_ip=?
            WHERE mac=?
        """, (now, client_ip, norm_mac))

        host_dict  = dict(host)
        action     = host_dict.get("boot_action", "auto")
        wf_id      = host_dict.get("workflow_id")
        pc_name    = host_dict.get("pc_name") or norm_mac

        if action == "auto":
            action = "deploy" if wf_id else "local"

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

        conn.execute("""
            INSERT INTO pxe_boot_log (mac, pc_name, ip, action, pw_id, ts)
            VALUES (?,?,?,?,?,?)
        """, (norm_mac, pc_name, client_ip, action, pw_id, now))
        conn.commit()

    log.info("PXE boot: MAC=%s PC=%s IP=%s action=%s pw_id=%s",
             norm_mac, pc_name, client_ip, action, pw_id)

    iventoy_ip   = pxe_cfg.get("iventoy_ip",  "").strip()
    iventoy_port = pxe_cfg.get("iventoy_port", "10809")

    if action == "deploy" and not iventoy_ip:
        log.warning("PXE: iVentoy IP non configurato — fallback local boot per MAC=%s", norm_mac)
        return _ipxe_local(f"{pc_name} (iVentoy non configurato)"), 200, {"Content-Type": "text/plain"}

    if action == "block":
        script = _ipxe_block(pc_name)
    elif action == "deploy":
        script = _ipxe_deploy(pc_name, iventoy_ip, iventoy_port)
    else:
        script = _ipxe_local(pc_name)

    return script, 200, {"Content-Type": "text/plain"}


# ── PXE HOSTS CRUD ────────────────────────────────────────────────────────────

@app.route("/api/pxe/hosts", methods=["GET"])
@require_auth
def list_pxe_hosts():
    with get_db_ctx() as conn:
        rows = conn.execute("""
            SELECT h.*,
                   w.nome AS workflow_nome
            FROM pxe_hosts h
            LEFT JOIN workflows w ON h.workflow_id = w.id
            ORDER BY h.last_boot_at DESC
        """).fetchall()
    return jsonify([dict(r) for r in rows])


@app.route("/api/pxe/hosts/<mac>", methods=["GET"])
@require_auth
def get_pxe_host(mac: str):
    norm = _normalize_mac(mac)
    if not norm:
        return jsonify({"error": "MAC non valido"}), 400
    with get_db_ctx() as conn:
        row = conn.execute("SELECT * FROM pxe_hosts WHERE mac=?", (norm,)).fetchone()
    if not row:
        return jsonify({"error": "Host non trovato"}), 404
    return jsonify(dict(row))


@app.route("/api/pxe/hosts", methods=["POST"])
@require_auth
def create_pxe_host():
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
            row = conn.execute("SELECT * FROM pxe_hosts WHERE mac=?", (mac,)).fetchone()
            return jsonify(dict(row)), 201
        except Exception as exc:
            if "UNIQUE" in str(exc):
                return jsonify({"error": f"MAC {mac} già registrato"}), 409
            raise


@app.route("/api/pxe/hosts/<mac>", methods=["PUT"])
@require_auth
def update_pxe_host(mac: str):
    norm = _normalize_mac(mac)
    if not norm:
        return jsonify({"error": "MAC non valido"}), 400
    data    = request.get_json(silent=True) or {}
    allowed = ("pc_name", "cr_id", "workflow_id", "boot_action", "notes")
    updates = {k: v for k, v in data.items() if k in allowed}
    if not updates:
        return jsonify({"error": "Nessun campo aggiornabile fornito"}), 400
    set_clause = ", ".join(f"{k}=?" for k in updates)
    values     = list(updates.values()) + [norm]
    with get_db_ctx() as conn:
        rowcount = conn.execute(
            f"UPDATE pxe_hosts SET {set_clause} WHERE mac=?", values
        ).rowcount
        if rowcount == 0:
            return jsonify({"error": "Host non trovato"}), 404
        conn.commit()
        row = conn.execute("SELECT * FROM pxe_hosts WHERE mac=?", (norm,)).fetchone()
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
    with get_db_ctx() as conn:
        rows = conn.execute(
            "SELECT * FROM pxe_boot_log ORDER BY id DESC LIMIT 200"
        ).fetchall()
    return jsonify([dict(r) for r in rows])


# ── PXE SETTINGS ──────────────────────────────────────────────────────────────

_PXE_SETTINGS_DEFAULTS = {
    "pxe_enabled":             "1",
    "pxe_iventoy_ip":          "",
    "pxe_iventoy_port":        "10809",
    "pxe_auto_provision":      "0",
    "pxe_pc_prefix":           "PC",
    "pxe_default_domain":      "",
    "pxe_default_ou":          "",
    "pxe_default_dc_ip":       "",
    "pxe_default_join_user":   "",
    "pxe_default_join_pass":   "",
    "pxe_default_admin_pass":  "",
    "pxe_default_workflow_id": "",
}


@app.route("/api/pxe/settings", methods=["GET"])
@require_auth
def get_pxe_settings_api():
    with get_db_ctx() as conn:
        rows = conn.execute(
            "SELECT key, value FROM settings WHERE key LIKE 'pxe_%'"
        ).fetchall()
    result = dict(_PXE_SETTINGS_DEFAULTS)
    result.update({r["key"]: r["value"] for r in rows})
    for k in ("pxe_default_join_pass", "pxe_default_admin_pass"):
        if result.get(k):
            result[k] = "••••••••"
    return jsonify(result)


@app.route("/api/pxe/settings", methods=["PUT"])
@require_auth
def update_pxe_settings_api():
    data        = request.get_json(silent=True) or {}
    allowed_keys = set(_PXE_SETTINGS_DEFAULTS.keys())
    now         = datetime.datetime.utcnow().isoformat()
    with get_db_ctx() as conn:
        for key, value in data.items():
            if key not in allowed_keys:
                continue
            if value == "••••••••":
                continue
            conn.execute("""
                INSERT INTO settings (key, value) VALUES (?,?)
                ON CONFLICT(key) DO UPDATE SET value=excluded.value
            """, (key, str(value)))
        conn.commit()
    return jsonify({"ok": True})


# ── Download dist binaries ────────────────────────────────────────────────────

DIST_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "dist")

@app.route("/api/download/deploy-screen", methods=["GET"])
@require_auth
def download_deploy_screen():
    """Scarica NovaSCMDeployScreen.exe — usato dall'agent/installer durante il deploy."""
    exe_path = os.path.join(DIST_DIR, "NovaSCMDeployScreen.exe")
    if not os.path.isfile(exe_path):
        return jsonify({"error": "NovaSCMDeployScreen.exe non trovato in server/dist/"}), 404
    return send_from_directory(DIST_DIR, "NovaSCMDeployScreen.exe",
                               as_attachment=True,
                               mimetype="application/octet-stream")

# ── Web UI ────────────────────────────────────────────────────────────────────

WEB_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "web")

@app.route("/")
def ui_index():
    html_path = os.path.join(WEB_DIR, "index.html")
    with open(html_path, encoding="utf-8") as f:
        html = f.read()
    # C-4: non iniettare l'API key nel DOM — il client la legge via /api/ui-token
    return html, 200, {"Content-Type": "text/html; charset=utf-8"}

@app.route("/api/ui-token", methods=["GET"])
@require_auth
def ui_token():
    """Restituisce un session token breve (8h) per la UI, dopo aver verificato l'API key."""
    token = secrets.token_hex(32)
    exp   = time.time() + 28800
    return jsonify({"token": token, "expires_at": exp})

@app.route("/deploy-client")
@require_auth
def ui_deploy_client():
    html_path = os.path.join(WEB_DIR, "deploy-client.html")
    with open(html_path, encoding="utf-8") as f:
        html = f.read()
    return html, 200, {"Content-Type": "text/html; charset=utf-8"}

@app.route("/web/<path:path>")
@require_auth
def ui_static(path):
    return send_from_directory(WEB_DIR, path)

# ── Health check ──────────────────────────────────────────────────────────────

@app.route("/health", methods=["GET"])
def health():
    return jsonify({"status": "ok"})

# ── Avvio ─────────────────────────────────────────────────────────────────────
init_db()
_start_background_jobs()

# ── TFTP Server PXE (opzionale — richiede tftpy e porta 69) ──────────────────
_PXE_ENABLED = os.environ.get("NOVASCM_PXE_ENABLED", "1").lower() not in ("0", "false", "no")
if _PXE_ENABLED and _pxe_server_available:
    _start_tftp(
        dist_dir = DIST_DIR,
        host     = os.environ.get("NOVASCM_TFTP_HOST", "0.0.0.0"),
        port     = int(os.environ.get("NOVASCM_TFTP_PORT", "69")),
    )

if __name__ == "__main__":
    port = int(os.environ.get("PORT", 9091))
    app.run(host="0.0.0.0", port=port, debug=False)
