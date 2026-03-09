"""
NovaSCM API — CR Management + Workflow Engine
Porta: 9091 (configurabile con PORT env var)
DB:    /data/novascm.db (configurabile con NOVASCM_DB env var)
"""
from flask import Flask, request, jsonify, Response, send_from_directory
from werkzeug.middleware.proxy_fix import ProxyFix
import sqlite3, json, datetime, os, functools, hmac, logging, re, secrets
from xml.sax.saxutils import escape as _xe
from contextlib import contextmanager

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
def get_db():
    conn = sqlite3.connect(DB, timeout=30, check_same_thread=False)
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA journal_mode=WAL")
    conn.execute("PRAGMA busy_timeout=5000")
    conn.execute("PRAGMA synchronous=NORMAL")
    return conn

@contextmanager
def get_db_ctx():
    conn = get_db()
    try:
        yield conn
    finally:
        conn.close()

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
        conn.commit()
        # Indici per query frequenti
        _idx = [
            ("idx_cr_pc_name",            "CREATE INDEX IF NOT EXISTS idx_cr_pc_name ON cr(pc_name)"),
            ("idx_pw_pc_name",            "CREATE INDEX IF NOT EXISTS idx_pw_pc_name ON pc_workflows(pc_name)"),
            ("idx_pw_status",             "CREATE INDEX IF NOT EXISTS idx_pw_status ON pc_workflows(status)"),
            ("idx_pws_pc_workflow_id",    "CREATE INDEX IF NOT EXISTS idx_pws_pc_workflow_id ON pc_workflow_steps(pc_workflow_id)"),
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
        ]
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
        if not conn.execute("SELECT 1 FROM cr WHERE id=?", (cr_id,)).fetchone():
            return jsonify({"error": "Non trovato"}), 404
        conn.execute("DELETE FROM cr_steps WHERE cr_id=?", (cr_id,))  # figli prima
        conn.execute("DELETE FROM cr WHERE id=?", (cr_id,))
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

@app.route("/api/settings", methods=["PUT"])
@require_auth
def update_settings():
    data = request.get_json(force=True)
    with get_db_ctx() as conn:
        for key, value in data.items():
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
        affected = conn.execute("DELETE FROM workflows WHERE id=?", (wf_id,)).rowcount
        conn.commit()
    if affected == 0:
        return jsonify({"error": "Non trovato"}), 404
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
            ORDER BY pw.id DESC
        """).fetchall()
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
            SELECT ws.ordine, ws.nome, ws.tipo, ws.parametri, ws.su_errore,
                   COALESCE(pws.status,'pending') as status,
                   pws.output, pws.timestamp
            FROM workflow_steps ws
            LEFT JOIN pc_workflow_steps pws ON pws.step_id=ws.id AND pws.pc_workflow_id=?
            WHERE ws.workflow_id=?
            ORDER BY ws.ordine ASC
        """, (pw_id, dict(pw)["workflow_id"])).fetchall()
    result = dict(pw)
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
    data    = request.get_json(force=True)
    step_id = data.get("step_id")
    status  = data.get("status", "done")  # running | done | error | skipped
    output  = data.get("output", "")
    ts      = datetime.datetime.now().isoformat()
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
            INSERT INTO pc_workflow_steps (pc_workflow_id, step_id, status, output, timestamp)
            VALUES (?,?,?,?,?)
            ON CONFLICT(pc_workflow_id, step_id)
            DO UPDATE SET status=excluded.status, output=excluded.output, timestamp=excluded.timestamp
        """, (pw_id, step_id, status, output, ts))
        conn.execute("UPDATE pc_workflows SET last_seen=? WHERE id=?", (ts, pw_id))
        # Se tutti gli step sono done/skipped → completa workflow
        total = conn.execute(
            "SELECT COUNT(*) FROM workflow_steps WHERE workflow_id=?", (pw["workflow_id"],)
        ).fetchone()[0]
        done = conn.execute("""
            SELECT COUNT(*) FROM pc_workflow_steps
            WHERE pc_workflow_id=? AND status IN ('done','skipped','error')
        """, (pw_id,)).fetchone()[0]
        if total > 0 and done >= total:
            conn.execute("UPDATE pc_workflows SET status='completed', completed_at=? WHERE id=?",
                         (ts, pw_id))
        conn.commit()
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

@app.route("/api/download/agent-install.ps1", methods=["GET"])
@require_auth
def download_agent_installer_ps1():
    """Genera install-windows.ps1 con API URL e API Key pre-configurati."""
    api_url = _get_public_url()
    api_key = API_KEY
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
    """Genera install-linux.sh con API URL e API Key pre-configurati."""
    api_url = _get_public_url()
    api_key = API_KEY
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

# ── Web UI ────────────────────────────────────────────────────────────────────

WEB_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "web")

@app.route("/")
def ui_index():
    return send_from_directory(WEB_DIR, "index.html")

@app.route("/web/<path:path>")
def ui_static(path):
    return send_from_directory(WEB_DIR, path)

# ── Health check ──────────────────────────────────────────────────────────────

@app.route("/health", methods=["GET"])
def health():
    return jsonify({"status": "ok"})

# ── Avvio ─────────────────────────────────────────────────────────────────────
init_db()
if __name__ == "__main__":
    port = int(os.environ.get("PORT", 9091))
    app.run(host="0.0.0.0", port=port, debug=False)
