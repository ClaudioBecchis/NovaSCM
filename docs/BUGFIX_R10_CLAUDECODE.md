# NovaSCM — Bug Analysis Round 10
## Versione: v2.1.0 | Commit: c3ca934

**Repository:** https://github.com/ClaudioBecchis/NovaSCM  
**Stack:** Python 3 · Flask · SQLite (WAL) · C# WPF  
**File esaminati:** `server/api.py` (~1900 righe), `deploy/postinstall.ps1`, `deploy/autounattend.xml`, `deploy/deploy.html`

---

## Status Round 9 — Parzialmente Aperti

| ID | Descrizione | Status |
|---|---|---|
| C-4 | API key iniettata nel meta tag HTML (`ui_index()`) | 🔴 PENDING |
| C-5 | OSD scaricato via HTTP senza SHA256 | 🔴 PENDING |
| M-3 | URL agent errato: `/agent/install.ps1` non esiste | 🟡 PENDING |
| M-4 | CORS `Access-Control-Allow-Origin: *` su tutti gli endpoint | 🟡 PENDING |
| I-4 | `/deploy-client` e `/web/` senza autenticazione | 🔵 PENDING |
| I-5 | `deploy/deploy.html` con IP e API key hardcoded | 🔵 PENDING |

---

## Nuovi Bug v2.1.0

### 🔴 C-6 — `/api/boot/<mac>` senza auth permette enumerazione MAC e auto-provisioning
**File:** `server/api.py` → `pxe_boot_script()` ~L1662

```python
@app.route("/api/boot/<mac>")
# ← nessun @require_auth — corretto per iPXE
def pxe_boot_script(mac: str):
    ...
    # Se pxe_auto_provision=1 e MAC sconosciuto → crea CR con credenziali dominio default
    if settings.get("pxe_auto_provision") == "1":
        conn.execute(
            "INSERT INTO change_requests (pc_name, domain, ou, assigned_user, status, ...) VALUES (?,?,?,?,?,...)",
            (mac, settings.get("default_domain"), ..., settings.get("default_join_pass"), ...)
        )
```

Qualsiasi host in rete può:
1. Inviare GET `/api/boot/AA:BB:CC:DD:EE:FF` per ogni MAC possibile e scoprire quali PC sono registrati dal tipo di risposta (`#!ipxe` boot vs `exit`)
2. Con `pxe_auto_provision=1` (default), ogni MAC anonimo genera automaticamente una CR con `default_join_pass` e `default_admin_pass` in chiaro nel DB

**Fix — aggiungere HMAC validation nell'URL iPXE e disabilitare auto-provision di default:**
```python
import hmac, hashlib

# In pxe_boot_script(): valida token HMAC prima di rispondere con azioni
def validate_pxe_token(mac: str, token: str) -> bool:
    secret = current_app.config.get("PXE_HMAC_SECRET", "")
    if not secret:
        return True  # backward compat se non configurato
    expected = hmac.new(secret.encode(), mac.lower().encode(), hashlib.sha256).hexdigest()[:16]
    return hmac.compare_digest(expected, token)

# In PXE settings: cambiare default
"pxe_auto_provision": "0"  # era "1"
```

Il template iPXE deve includere il token:
```
chain http://${NOVASCM}/api/boot/${net0/mac}?t=${TOKEN}
```

---

### 🔴 C-7 — Catena C-4 + M-4: DOM key + CORS wildcard → full account takeover cross-origin
**File:** `server/api.py` → `ui_index()` + `add_cors()`

Entrambi C-4 e M-4 sono ancora aperti. La loro combinazione crea un attacco completo in due righe JS, eseguibile da qualsiasi pagina web visitata dall'utente mentre la UI NovaSCM è aperta:

```javascript
// Step 1: legge l'API key dal meta tag (C-4)
const key = document.querySelector('meta[name="x-api-key"]').content;

// Step 2: CORS wildcard (M-4) permette richieste cross-origin con la key
const res = await fetch('http://192.168.20.110:9091/api/cr', {
    headers: { 'X-Api-Key': key }
});
// → accesso completo a tutte le CR, credenziali di dominio in chiaro, workflow, step
```

C-4 e M-4 devono essere fixati **entrambi e insieme** — risolverne uno solo riduce ma non elimina il rischio.

**Fix C-4** (rimuovere il meta tag, usare un token di sessione breve):
```python
# server/api.py → ui_index()

# RIMUOVERE:
# html = html.replace("</head>", f'<meta name="x-api-key" content="{API_KEY}">\n</head>')

# AGGIUNGERE: session token firmato, valido 8h, non leggibile da JS
import secrets, time
from flask import session

@app.route("/")
def ui_index():
    session["ui_token"] = secrets.token_hex(32)
    session["ui_token_exp"] = time.time() + 28800
    html = open(UI_PATH).read()
    return html
```

**Fix M-4** (CORS configurabile):
```python
# server/api.py → add_cors()
CORS_ORIGINS = os.environ.get("NOVASCM_CORS_ORIGINS", "").split(",")

@app.after_request
def add_cors(response):
    origin = request.headers.get("Origin", "")
    if CORS_ORIGINS and origin in CORS_ORIGINS:
        response.headers["Access-Control-Allow-Origin"] = origin
        response.headers["Vary"] = "Origin"
    # rimuovere il wildcard "*"
    return response
```

---

### 🟡 M-5 — Auto-provisioning deposita credenziali dominio per MAC non verificati
**File:** `server/api.py` → `pxe_boot_script()` ~L1707-1740

```python
# Quando pxe_auto_provision=1 e MAC non in pxe_hosts:
conn.execute("""
    INSERT INTO change_requests (pc_name, domain, ou, assigned_user, status, notes, ...)
    VALUES (?, ?, ?, ?, 'pending', ?, ...)
""", (mac, default_domain, default_ou, default_user, f"join_pass={default_join_pass} admin_pass={default_admin_pass}"))
```

Le credenziali di dominio default vengono salvate nel campo `notes` della CR in chiaro, associate a un MAC mai verificato. Un attaccante che fa bootstap con MAC falsi popola il DB con CR che contengono le credenziali reali.

**Fix — non salvare mai credenziali in note; richiedere approvazione esplicita del MAC:**
```python
# Cambiare default
pxe_settings_defaults = {
    "pxe_auto_provision": "0",  # ← default OFF
    ...
}

# Nella CR auto-creata: non includere credenziali, solo segnalare MAC sconosciuto
conn.execute("""
    INSERT INTO change_requests (pc_name, status, notes, created_at)
    VALUES (?, 'pending_approval', 'MAC sconosciuto — approvazione manuale richiesta', ?)
""", (mac, datetime.now().isoformat()))
```

---

### 🟡 M-6 — `deploy_start()` cancella storico deploy senza archivio
**File:** `server/api.py` → `deploy_start()` ~L1159

```python
# ATTUALE — distrugge sempre il record precedente
conn.execute(
    "DELETE FROM pc_workflows WHERE pc_name=? AND workflow_id=?",
    (pc_name, wf_id)
)
conn.execute(
    "INSERT INTO pc_workflows (pc_name, workflow_id, status, started_at) VALUES (?,?,?,?)",
    (pc_name, wf_id, "running", datetime.now().isoformat())
)
```

Se il postinstall.ps1 si riavvia (crash, retry), tutta la storia del deployment precedente — step, elapsed_sec, log — viene cancellata.

**Fix — archiviare invece di cancellare:**
```python
# Aggiungere colonna allo schema:
# ALTER TABLE pc_workflows ADD COLUMN archived INTEGER NOT NULL DEFAULT 0;

# In deploy_start():
conn.execute(
    "UPDATE pc_workflows SET archived=1 WHERE pc_name=? AND workflow_id=? AND archived=0",
    (pc_name, wf_id)
)
conn.execute(
    "INSERT INTO pc_workflows (pc_name, workflow_id, status, started_at, archived) VALUES (?,?,?,?,0)",
    (pc_name, wf_id, "running", datetime.now().isoformat())
)
```

---

### 🟡 M-7 — API key in chiaro negli installer `.ps1`/`.sh` generati
**File:** `server/api.py` → `download_agent_installer_ps1()` / `download_agent_installer_sh()`

```python
# ATTUALE — API key master nel corpo del file scaricabile
script = f"""
$ApiUrl  = "{BASE_URL}"
$ApiKey  = "{API_KEY}"   # ← chiunque intercetta il download ottiene la chiave permanente
...
"""
```

**Fix — enrollment token monouso a scadenza:**
```python
import uuid, time

# Aggiungere tabella: enrollment_tokens(token TEXT PK, expires_at REAL, used INTEGER)

@app.route("/api/download/agent-install.ps1")
@require_auth
def download_agent_installer_ps1():
    token = str(uuid.uuid4())
    expires = time.time() + 3600  # 1h
    with get_db() as conn:
        conn.execute("INSERT INTO enrollment_tokens VALUES (?,?,0)", (token, expires))
    
    script = f"""
$EnrollToken = "{token}"   # token monouso, scade tra 1h
$ApiUrl      = "{BASE_URL}"
# Al primo check-in, il server scambia il token con una API key permanente
...
"""
    return Response(script, mimetype="text/plain")
```

---

### 🔵 I-6 — Race condition su `boot_count`
**File:** `server/api.py` → `pxe_boot_script()` ~L1750

```python
# ATTUALE — non atomico
conn.execute(
    "UPDATE pxe_hosts SET last_boot_at=?, boot_count=boot_count+1, last_ip=? WHERE mac=?",
    (now, ip, mac)
)
```

Due boot simultanei dello stesso MAC (PXE retry rapido) possono perdere un incremento.

**Fix:**
```python
# Racchiudere in transazione serializzata o usare RETURNING per garantire atomicità
with conn:
    conn.execute("BEGIN IMMEDIATE")
    conn.execute(
        "UPDATE pxe_hosts SET last_boot_at=?, boot_count=boot_count+1, last_ip=? WHERE mac=?",
        (now, ip, mac)
    )
```

---

### 🔵 I-7 — Connessione SQLite aperta per ogni richiesta
**File:** `server/api.py` → `get_db_ctx()`

```python
@contextmanager
def get_db_ctx():
    conn = sqlite3.connect(DB_PATH)  # ← nuova connessione per ogni handler
    ...
    conn.close()
```

Con deploy multipli paralleli (>5) e WAL mode, la creazione ripetuta di connessioni aumenta la latenza e può causare `SQLITE_BUSY` nonostante `timeout=5`.

**Fix — connessione thread-local:**
```python
import threading
_db_local = threading.local()

def get_thread_db():
    if not hasattr(_db_local, "conn") or _db_local.conn is None:
        _db_local.conn = sqlite3.connect(DB_PATH, check_same_thread=False)
        _db_local.conn.row_factory = sqlite3.Row
        _db_local.conn.execute("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;")
    return _db_local.conn
```

---

## Priorità Fix

| Priorità | Bug | Azione |
|---|---|---|
| 1 — Immediato | C-4 | Rimuovere meta tag API key da `ui_index()` |
| 2 — Immediato | M-4 | Restringere CORS a `NOVASCM_CORS_ORIGINS` |
| 3 — Alta | C-5 | OSD: HTTPS + `X-Api-Key` + SHA256 |
| 4 — Alta | M-3 | Correggere URL a `/api/download/agent-install.ps1` |
| 5 — Media | M-5 | `pxe_auto_provision=0` default; no credenziali nelle CR auto |
| 6 — Media | M-6, M-7 | Archivio deploy; enrollment token monouso |
| 7 — Bassa | C-6, I-6, I-7 | HMAC PXE token; serializzazione boot_count; thread-local DB |
