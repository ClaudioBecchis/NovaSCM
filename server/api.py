"""
NovaSCM API — CR Management + Workflow Engine
Porta: 9091 (configurabile con PORT env var)
DB:    /data/novascm.db (configurabile con NOVASCM_DB env var)
"""
from flask import Flask, request, jsonify, Response, send_from_directory
import sqlite3, json, datetime, os, functools, hmac, logging, re
from contextlib import contextmanager

app = Flask(__name__)

# Percorso DB: variabile d'ambiente NOVASCM_DB, default /data/novascm.db
DB = os.environ.get("NOVASCM_DB", "/data/novascm.db")

# ── Logging ───────────────────────────────────────────────────────────────────
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s — %(message)s"
)
log = logging.getLogger("novascm-api")

# ── Autenticazione API Key ────────────────────────────────────────────────────
API_KEY = os.environ.get("NOVASCM_API_KEY", "")

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
    conn = get_db()
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
        except Exception:
            pass
    conn.close()

def row_to_dict(row):
    d = dict(row)
    d["software"] = json.loads(d.get("software") or "[]")
    return d

# ── CR CRUD ───────────────────────────────────────────────────────────────────

@app.route("/api/cr", methods=["GET"])
@require_auth
def list_cr():
    conn = get_db()
    rows = conn.execute("SELECT * FROM cr ORDER BY id DESC").fetchall()
    conn.close()
    return jsonify([row_to_dict(r) for r in rows])

@app.route("/api/cr", methods=["POST"])
@require_auth
def create_cr():
    data = request.get_json(force=True)
    for f in ("pc_name", "domain"):
        if not data.get(f):
            return jsonify({"error": f"Campo obbligatorio: {f}"}), 400
    now = datetime.datetime.now().isoformat()
    conn = get_db()
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
        conn.close()
        return jsonify(row_to_dict(row)), 201
    except sqlite3.IntegrityError:
        conn.close()
        return jsonify({"error": f"PC '{data['pc_name']}' esiste già"}), 409

@app.route("/api/cr/<int:cr_id>", methods=["GET"])
@require_auth
def get_cr(cr_id):
    conn = get_db()
    row = conn.execute("SELECT * FROM cr WHERE id=?", (cr_id,)).fetchone()
    conn.close()
    if not row:
        return jsonify({"error": "Non trovato"}), 404
    return jsonify(row_to_dict(row))

@app.route("/api/cr/by-name/<pc_name>", methods=["GET"])
@require_auth
def get_cr_by_name(pc_name):
    conn = get_db()
    row = conn.execute("SELECT * FROM cr WHERE pc_name=?",
                       (pc_name.upper().strip(),)).fetchone()
    conn.close()
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
    conn = get_db()
    conn.execute("UPDATE cr SET status=?, completed_at=? WHERE id=?",
                 (status, now, cr_id))
    conn.commit()
    row = conn.execute("SELECT * FROM cr WHERE id=?", (cr_id,)).fetchone()
    conn.close()
    if not row:
        return jsonify({"error": "Non trovato"}), 404
    return jsonify(row_to_dict(row))

@app.route("/api/cr/<int:cr_id>", methods=["DELETE"])
@require_auth
def delete_cr(cr_id):
    conn = get_db()
    affected = conn.execute("DELETE FROM cr WHERE id=?", (cr_id,)).rowcount
    conn.execute("DELETE FROM cr_steps WHERE cr_id=?", (cr_id,))
    conn.commit()
    conn.close()
    if affected == 0:
        return jsonify({"error": "Non trovato"}), 404
    return jsonify({"ok": True})

# ── Autounattend.xml generato dal server ──────────────────────────────────────

@app.route("/api/cr/by-name/<pc_name>/autounattend.xml", methods=["GET"])
@require_auth
def get_autounattend(pc_name):
    conn = get_db()
    row = conn.execute("SELECT * FROM cr WHERE pc_name=?",
                       (pc_name.upper().strip(),)).fetchone()
    conn.close()
    if not row:
        return "CR non trovato", 404
    d = row_to_dict(row)
    pkgs = d.get("software", [])
    def _safe_pkg(pkg_id):
        """Sanifica package ID winget: solo caratteri alfanumerici, punto, trattino, underscore."""
        return re.sub(r"[^a-zA-Z0-9.\-_]", "", str(pkg_id))
    winget_block = "\n".join(
        f"winget install --id {_safe_pkg(p)} --silent --accept-package-agreements --accept-source-agreements"
        for p in pkgs if p and _safe_pkg(p)
    ) if pkgs else "# Nessun software configurato"

    odj_blob   = (d.get("odj_blob") or "").strip()
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
          <Path>powershell.exe -NonInteractive -Command "for($i=0;$i-lt30;$i++){{$n=Get-NetAdapter|?{{$_.Status-eq'Up'-and$_.HardwareInterface}}|Select -First 1;if($n){{Set-DnsClientServerAddress -InterfaceIndex $n.InterfaceIndex -ServerAddresses '{d['dc_ip']}';break}};Start-Sleep 2}}"</Path>
          <Description>Attendi rete e imposta DNS DC</Description>
        </RunSynchronousCommand>
        <RunSynchronousCommand wcm:action="add">
          <Order>2</Order>
          <Path>powershell.exe -NonInteractive -Command "Add-Computer -DomainName '{d['domain']}' -Credential (New-Object PSCredential('{d['domain']}\\{d['join_user']}',(ConvertTo-SecureString '{d['join_pass']}' -AsPlainText -Force))) -Force -ErrorAction SilentlyContinue"</Path>
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
      <ComputerName>{d['pc_name']}</ComputerName>
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
              <Value>{d['admin_pass']}</Value>
              <PlainText>true</PlainText>
            </Password>
          </LocalAccount>
        </LocalAccounts>
      </UserAccounts>
      <AutoLogon>
        <Password><Value>{d['admin_pass']}</Value><PlainText>true</PlainText></Password>
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
<!-- Generato da NovaSCM API — CR #{d['id']} — {d['pc_name']} -->"""

    return Response(xml, mimetype="application/xml",
                    headers={"Content-Disposition": "attachment; filename=autounattend.xml"})

# ── Check-in + Step tracking ──────────────────────────────────────────────────

@app.route("/api/cr/by-name/<pc_name>/checkin", methods=["POST"])
@require_auth
def checkin_cr(pc_name):
    now = datetime.datetime.now().isoformat()
    conn = get_db()
    affected = conn.execute("UPDATE cr SET last_seen=? WHERE pc_name=?",
                            (now, pc_name.upper().strip())).rowcount
    conn.commit()
    if affected == 0:
        conn.close()
        return jsonify({"error": "CR non trovato"}), 404
    row = conn.execute("SELECT * FROM cr WHERE pc_name=?",
                       (pc_name.upper().strip(),)).fetchone()
    conn.close()
    return jsonify({"ok": True, "last_seen": now, "cr": row_to_dict(row)})

# ── Settings ──────────────────────────────────────────────────────────────────

@app.route("/api/settings", methods=["GET"])
@require_auth
def get_settings():
    conn = get_db()
    rows = conn.execute("SELECT key, value FROM settings").fetchall()
    conn.close()
    return jsonify({r["key"]: r["value"] for r in rows})

@app.route("/api/settings", methods=["PUT"])
@require_auth
def update_settings():
    data = request.get_json(force=True)
    conn = get_db()
    for key, value in data.items():
        conn.execute(
            "INSERT INTO settings (key, value) VALUES (?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value",
            (key, str(value) if value is not None else "")
        )
    conn.commit()
    rows = conn.execute("SELECT key, value FROM settings").fetchall()
    conn.close()
    return jsonify({r["key"]: r["value"] for r in rows})

@app.route("/api/cr/by-name/<pc_name>/step", methods=["POST"])
@require_auth
def report_step(pc_name):
    data   = request.get_json(force=True)
    step   = data.get("step", "")
    status = data.get("status", "done")
    ts     = datetime.datetime.now().isoformat()  # BUG-08: timestamp sempre server-side
    conn   = get_db()
    row    = conn.execute("SELECT id FROM cr WHERE pc_name=?",
                          (pc_name.upper().strip(),)).fetchone()
    if not row:
        conn.close()
        return jsonify({"error": "CR non trovato"}), 404
    cr_id = row["id"]
    conn.execute("""
        INSERT INTO cr_steps (cr_id, step_name, status, timestamp) VALUES (?,?,?,?)
        ON CONFLICT(cr_id, step_name) DO UPDATE SET status=excluded.status, timestamp=excluded.timestamp
    """, (cr_id, step, status, ts))
    conn.execute("UPDATE cr SET last_seen=? WHERE id=?", (ts, cr_id))
    conn.commit()
    conn.close()
    return jsonify({"ok": True, "step": step, "status": status})

@app.route("/api/cr/<int:cr_id>/steps", methods=["GET"])
@require_auth
def get_steps(cr_id):
    conn = get_db()
    rows = conn.execute(
        "SELECT step_name, status, timestamp FROM cr_steps WHERE cr_id=? ORDER BY id ASC",
        (cr_id,)).fetchall()
    conn.close()
    return jsonify([dict(r) for r in rows])

# ── Workflow Engine — Workflow CRUD ───────────────────────────────────────────

@app.route("/api/workflows", methods=["GET"])
@require_auth
def list_workflows():
    conn = get_db()
    rows = conn.execute("SELECT * FROM workflows ORDER BY id ASC").fetchall()
    conn.close()
    return jsonify([dict(r) for r in rows])

@app.route("/api/workflows", methods=["POST"])
@require_auth
def create_workflow():
    data = request.get_json(force=True)
    if not data.get("nome"):
        return jsonify({"error": "Campo obbligatorio: nome"}), 400
    now = datetime.datetime.now().isoformat()
    conn = get_db()
    try:
        conn.execute(
            "INSERT INTO workflows (nome, descrizione, versione, created_at, updated_at) VALUES (?,?,?,?,?)",
            (data["nome"].strip(), data.get("descrizione", ""), data.get("versione", 1), now, now)
        )
        conn.commit()
        row = conn.execute("SELECT * FROM workflows WHERE nome=?", (data["nome"].strip(),)).fetchone()
        conn.close()
        return jsonify(dict(row)), 201
    except sqlite3.IntegrityError:
        conn.close()
        return jsonify({"error": f"Workflow '{data['nome']}' esiste già"}), 409

@app.route("/api/workflows/<int:wf_id>", methods=["GET"])
@require_auth
def get_workflow(wf_id):
    conn = get_db()
    wf = conn.execute("SELECT * FROM workflows WHERE id=?", (wf_id,)).fetchone()
    if not wf:
        conn.close()
        return jsonify({"error": "Non trovato"}), 404
    steps = conn.execute(
        "SELECT * FROM workflow_steps WHERE workflow_id=? ORDER BY ordine ASC", (wf_id,)
    ).fetchall()
    conn.close()
    result = dict(wf)
    result["steps"] = [dict(s) for s in steps]
    return jsonify(result)

@app.route("/api/workflows/<int:wf_id>", methods=["PUT"])
@require_auth
def update_workflow(wf_id):
    data = request.get_json(force=True)
    now  = datetime.datetime.now().isoformat()
    conn = get_db()
    conn.execute(
        "UPDATE workflows SET nome=?, descrizione=?, versione=versione+1, updated_at=? WHERE id=?",
        (data.get("nome", "").strip(), data.get("descrizione", ""), now, wf_id)
    )
    conn.commit()
    row = conn.execute("SELECT * FROM workflows WHERE id=?", (wf_id,)).fetchone()
    conn.close()
    if not row:
        return jsonify({"error": "Non trovato"}), 404
    return jsonify(dict(row))

@app.route("/api/workflows/<int:wf_id>", methods=["DELETE"])
@require_auth
def delete_workflow(wf_id):
    conn = get_db()
    affected = conn.execute("DELETE FROM workflows WHERE id=?", (wf_id,)).rowcount
    conn.commit()
    conn.close()
    if affected == 0:
        return jsonify({"error": "Non trovato"}), 404
    return jsonify({"ok": True})

# ── Workflow Engine — Steps CRUD ───────────────────────────────────────────────

@app.route("/api/workflows/<int:wf_id>/steps", methods=["GET"])
@require_auth
def list_steps(wf_id):
    conn = get_db()
    rows = conn.execute(
        "SELECT * FROM workflow_steps WHERE workflow_id=? ORDER BY ordine ASC", (wf_id,)
    ).fetchall()
    conn.close()
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
    conn = get_db()
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
        conn.close()
        return jsonify(dict(row)), 201
    except sqlite3.IntegrityError:
        conn.close()
        return jsonify({"error": f"Ordine {data['ordine']} già esistente in questo workflow"}), 409

@app.route("/api/workflows/<int:wf_id>/steps/<int:step_id>", methods=["PUT"])
@require_auth
def update_step(wf_id, step_id):
    data = request.get_json(force=True)
    parametri = data.get("parametri", {})
    if isinstance(parametri, dict):
        parametri = json.dumps(parametri)
    conn = get_db()
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
    conn.close()
    if not row:
        return jsonify({"error": "Step non trovato"}), 404
    return jsonify(dict(row))

@app.route("/api/workflows/<int:wf_id>/steps/<int:step_id>", methods=["DELETE"])
@require_auth
def delete_step(wf_id, step_id):
    conn = get_db()
    affected = conn.execute(
        "DELETE FROM workflow_steps WHERE id=? AND workflow_id=?", (step_id, wf_id)
    ).rowcount
    conn.execute("UPDATE workflows SET versione=versione+1, updated_at=? WHERE id=?",
                 (datetime.datetime.now().isoformat(), wf_id))
    conn.commit()
    conn.close()
    if affected == 0:
        return jsonify({"error": "Step non trovato"}), 404
    return jsonify({"ok": True})

# ── Workflow Engine — PC Assignments ──────────────────────────────────────────

@app.route("/api/pc-workflows", methods=["GET"])
@require_auth
def list_pc_workflows():
    conn = get_db()
    rows = conn.execute("""
        SELECT pw.*, w.nome as workflow_nome
        FROM pc_workflows pw
        JOIN workflows w ON w.id = pw.workflow_id
        ORDER BY pw.id DESC
    """).fetchall()
    conn.close()
    return jsonify([dict(r) for r in rows])

@app.route("/api/pc-workflows", methods=["POST"])
@require_auth
def assign_workflow():
    data = request.get_json(force=True)
    for f in ("pc_name", "workflow_id"):
        if not data.get(f):
            return jsonify({"error": f"Campo obbligatorio: {f}"}), 400
    now = datetime.datetime.now().isoformat()
    conn = get_db()
    wf = conn.execute("SELECT id FROM workflows WHERE id=?", (data["workflow_id"],)).fetchone()
    if not wf:
        conn.close()
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
        conn.close()
        return jsonify(dict(row)), 201
    except sqlite3.IntegrityError:
        conn.close()
        return jsonify({"error": "Workflow già assegnato a questo PC"}), 409

@app.route("/api/pc-workflows/<int:pw_id>", methods=["GET"])
@require_auth
def get_pc_workflow(pw_id):
    conn = get_db()
    pw = conn.execute("""
        SELECT pw.*, w.nome as workflow_nome
        FROM pc_workflows pw JOIN workflows w ON w.id=pw.workflow_id
        WHERE pw.id=?
    """, (pw_id,)).fetchone()
    if not pw:
        conn.close()
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
    conn.close()
    result = dict(pw)
    result["steps"] = [dict(s) for s in steps]
    return jsonify(result)

@app.route("/api/pc-workflows/<int:pw_id>", methods=["DELETE"])
@require_auth
def delete_pc_workflow(pw_id):
    conn = get_db()
    affected = conn.execute("DELETE FROM pc_workflows WHERE id=?", (pw_id,)).rowcount
    conn.commit()
    conn.close()
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
    conn = get_db()

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
            # Verifica che il workflow esista
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
        conn.close()
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

    conn.close()
    pw_dict["steps"] = [dict(s) for s in steps]
    return jsonify(pw_dict)

@app.route("/api/pc/<pc_name>/workflow/step", methods=["POST"])
@require_auth
def report_wf_step(pc_name):
    """Agent: riporta lo stato di uno step di un workflow."""
    data   = request.get_json(force=True)
    step_id = data.get("step_id")
    status  = data.get("status", "done")  # running | done | error | skipped
    output  = data.get("output", "")
    ts      = datetime.datetime.now().isoformat()  # BUG-08: timestamp sempre server-side
    if not step_id:
        return jsonify({"error": "Campo obbligatorio: step_id"}), 400
    conn = get_db()
    pw = conn.execute(
        "SELECT id, workflow_id FROM pc_workflows WHERE pc_name=? AND status='running' ORDER BY id DESC LIMIT 1",
        (pc_name.upper().strip(),)
    ).fetchone()
    if not pw:
        conn.close()
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
        WHERE pc_workflow_id=? AND status IN ('done','skipped')
    """, (pw_id,)).fetchone()[0]
    if total > 0 and done >= total:
        conn.execute("UPDATE pc_workflows SET status='completed', completed_at=? WHERE id=?",
                     (ts, pw_id))
    conn.commit()
    conn.close()
    return jsonify({"ok": True, "step_id": step_id, "status": status})

@app.route("/api/pc/<pc_name>/workflow/checkin", methods=["POST"])
@require_auth
def checkin_wf(pc_name):
    """Agent: heartbeat durante esecuzione workflow."""
    now = datetime.datetime.now().isoformat()
    conn = get_db()
    affected = conn.execute(
        "UPDATE pc_workflows SET last_seen=? WHERE pc_name=? AND status='running'",
        (now, pc_name.upper().strip())
    ).rowcount
    conn.commit()
    conn.close()
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
    """Serve l'installer NSIS per nuovi utenti."""
    setup_file = os.path.join(os.path.dirname(DB), "NovaSCM-Setup.exe")
    if not os.path.exists(setup_file):
        return jsonify({"error": "Installer non disponibile"}), 404
    from flask import send_file
    return send_file(setup_file, as_attachment=True,
                     download_name="NovaSCM-Setup.exe",
                     mimetype="application/octet-stream")

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
    """Genera install-windows.ps1 con API URL pre-configurato."""
    api_url = request.host_url.rstrip("/")
    ps1 = f"""# NovaSCM Agent Installer — generato automaticamente
$ApiUrl = "{api_url}"
$PcName = $env:COMPUTERNAME
Invoke-Expression (Invoke-WebRequest -Uri "$ApiUrl/api/download/agent-install-full.ps1" -UseBasicParsing).Content
"""
    return Response(ps1, mimetype="text/plain",
                    headers={"Content-Disposition": "attachment; filename=agent-install.ps1"})

@app.route("/api/download/agent-install.sh", methods=["GET"])
@require_auth
def download_agent_installer_sh():
    """Genera install-linux.sh con API URL pre-configurato."""
    api_url = request.host_url.rstrip("/")
    sh = f"""#!/bin/bash
# NovaSCM Agent Installer — generato automaticamente
curl -fsSL "{api_url}/api/download/agent-install-full.sh" | sudo bash -s -- --api-url "{api_url}"
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
    return jsonify({"status": "ok", "db": DB})

# ── Avvio ─────────────────────────────────────────────────────────────────────
init_db()
if __name__ == "__main__":
    port = int(os.environ.get("PORT", 9091))
    app.run(host="0.0.0.0", port=port, debug=False)
