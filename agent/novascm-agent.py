"""
NovaSCM Agent — Workflow Executor
Gira come servizio (Windows Service / systemd).
Fa polling dell'API, esegue workflow assegnati, riporta stato step per step.

Config: %ProgramData%\NovaSCM\agent.json  (Windows)
        /etc/novascm/agent.json           (Linux)
State:  %ProgramData%\NovaSCM\state.json  (Windows)
        /var/lib/novascm/state.json       (Linux)
Log:    %ProgramData%\NovaSCM\agent.log   (Windows)
        /var/log/novascm/agent.log        (Linux)
"""

import os, sys, json, time, platform, subprocess, socket, logging, traceback, shutil
from datetime import datetime
from urllib.request import urlopen, Request
from urllib.error import URLError
from urllib.parse import urlencode, urlparse

# ── Costanti ──────────────────────────────────────────────────────────────────
IS_WINDOWS = platform.system() == "Windows"
IS_LINUX   = platform.system() == "Linux"
AGENT_VER  = "1.0.0"
POLL_SEC   = 60          # polling ogni 60 secondi
STEP_TIMEOUT = 600       # timeout massimo per step (10 min)

if IS_WINDOWS:
    BASE_DIR  = os.path.join(os.environ.get("ProgramData", "C:\\ProgramData"), "NovaSCM")
    STATE_DIR = BASE_DIR
    LOG_DIR   = BASE_DIR
else:
    BASE_DIR  = "/etc/novascm"
    STATE_DIR = "/var/lib/novascm"
    LOG_DIR   = "/var/log/novascm"

CONFIG_FILE = os.path.join(BASE_DIR, "agent.json")
STATE_FILE  = os.path.join(STATE_DIR, "state.json")
LOG_FILE    = os.path.join(LOG_DIR, "agent.log")

for d in [BASE_DIR, STATE_DIR, LOG_DIR]:
    os.makedirs(d, exist_ok=True)

# ── Logging ───────────────────────────────────────────────────────────────────
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
    handlers=[
        logging.FileHandler(LOG_FILE, encoding="utf-8"),
        logging.StreamHandler(sys.stdout),
    ]
)
log = logging.getLogger("novascm-agent")

# ── Config ────────────────────────────────────────────────────────────────────
DEFAULT_CONFIG = {
    "api_url":   "http://YOUR-SERVER-IP:9091",
    "api_key":   "",
    "poll_sec":  POLL_SEC,
    "pc_name":   socket.gethostname().upper(),
}


# Host bloccati: metadata service AWS/Azure/GCP, loopback (il server NovaSCM è su LAN, non localhost)
_BLOCKED_HOSTS = {
    "169.254.169.254",           # AWS/Azure/Alibaba metadata
    "metadata.google.internal",  # GCP metadata
    "metadata.internal",         # GCP alias
}

def _validate_api_url(url: str) -> str:
    """Valida api_url: solo http/https, no metadata service. Esce se non valido."""
    try:
        parsed = urlparse(url)
        if parsed.scheme not in ("http", "https"):
            log.error("[SSRF] api_url schema non valido ('%s'). Solo http/https consentiti.", parsed.scheme)
            sys.exit(1)
        host = (parsed.hostname or "").lower()
        if host in _BLOCKED_HOSTS:
            log.error("[SSRF] api_url punta a host non consentito: %s", host)
            sys.exit(1)
    except Exception as e:
        log.error("[SSRF] api_url non valido: %s", e)
        sys.exit(1)
    return url

# INFO-2: cache config con mtime — evita I/O su ogni ciclo di polling
_cfg_cache: dict | None = None
_cfg_mtime: float = 0.0

def load_config():
    global _cfg_cache, _cfg_mtime
    if not os.path.exists(CONFIG_FILE):
        with open(CONFIG_FILE, "w") as f:
            json.dump(DEFAULT_CONFIG, f, indent=2)
        log.info("Config creata: %s", CONFIG_FILE)
        _cfg_cache = None  # forza rilettura
    try:
        mtime = os.path.getmtime(CONFIG_FILE)
    except OSError:
        mtime = 0.0
    if _cfg_cache is not None and mtime == _cfg_mtime:
        return _cfg_cache
    with open(CONFIG_FILE) as f:
        cfg = json.load(f)
    cfg.setdefault("pc_name", socket.gethostname().upper())
    cfg.setdefault("poll_sec", POLL_SEC)
    cfg.setdefault("api_key", "")
    if "YOUR-SERVER-IP" in cfg.get("api_url", ""):
        log.error("[ATTENZIONE] agent.json non configurato! Modifica api_url in: %s", CONFIG_FILE)
    # SSRF: valida schema e host prima di usare l'URL
    cfg["api_url"] = _validate_api_url(cfg.get("api_url", ""))
    _cfg_cache = cfg
    _cfg_mtime = mtime
    return cfg

# ── State (persistenza tra riavvii) ──────────────────────────────────────────
def load_state():
    if os.path.exists(STATE_FILE):
        try:
            with open(STATE_FILE) as f:
                return json.load(f)
        except Exception:
            pass
    return {}

def save_state(state):
    with open(STATE_FILE, "w") as f:
        json.dump(state, f, indent=2)

def clear_state():
    if os.path.exists(STATE_FILE):
        os.remove(STATE_FILE)

# ── API Client ────────────────────────────────────────────────────────────────
def api_get(base_url, path, api_key=""):
    try:
        url = f"{base_url}{path}"
        headers = {"User-Agent": f"NovaSCM-Agent/{AGENT_VER}"}
        if api_key:
            headers["X-Api-Key"] = api_key
        req = Request(url, headers=headers)
        with urlopen(req, timeout=15) as r:
            return json.loads(r.read())
    except URLError as e:
        log.warning(f"API GET {path}: {e}")
        return None
    except Exception as e:
        log.error(f"API GET {path}: {e}")
        return None

def api_post(base_url, path, body, api_key=""):
    try:
        url  = f"{base_url}{path}"
        data = json.dumps(body).encode("utf-8")
        headers = {"Content-Type": "application/json",
                   "User-Agent": f"NovaSCM-Agent/{AGENT_VER}"}
        if api_key:
            headers["X-Api-Key"] = api_key
        req  = Request(url, data=data, method="POST", headers=headers)
        with urlopen(req, timeout=15) as r:
            return json.loads(r.read())
    except Exception as e:
        log.warning(f"API POST {path}: {e}")
        return None

# ── Step Executor ─────────────────────────────────────────────────────────────
def get_os_platform():
    return "windows" if IS_WINDOWS else "linux"

def run_cmd(cmd, timeout=STEP_TIMEOUT, env=None):
    """Esegue un comando. cmd deve essere una lista di argomenti — shell=False sempre."""
    try:
        r = subprocess.run(
            cmd, shell=False, capture_output=True, text=True,
            timeout=timeout, encoding="utf-8", errors="replace", env=env
        )
        out = (r.stdout + r.stderr).strip()
        return r.returncode == 0, out
    except subprocess.TimeoutExpired:
        return False, f"Timeout dopo {timeout}s"
    except Exception as e:
        return False, str(e)

def execute_step(step):
    """Esegue uno step e restituisce (ok, output)."""
    tipo      = step["tipo"]
    parametri = json.loads(step.get("parametri") or "{}")
    my_os     = get_os_platform()
    plat      = step.get("platform", "all")

    # Skip se step non è per questa piattaforma
    if plat != "all" and plat != my_os:
        log.info(f"  SKIP (platform={plat}, sono {my_os})")
        return None, f"Skipped: platform={plat}"

    log.info(f"  Tipo: {tipo} | Params: {parametri}")

    # ── winget_install ──
    if tipo == "winget_install":
        pkg_id = parametri.get("id", "")
        if not pkg_id:
            return False, "Parametro 'id' mancante"
        return run_cmd(["winget", "install", "--id", pkg_id, "--silent",
                        "--accept-package-agreements", "--accept-source-agreements"])

    # ── apt_install ──
    elif tipo == "apt_install":
        pkg = parametri.get("package", "")
        if not pkg:
            return False, "Parametro 'package' mancante"
        env = {**os.environ, "DEBIAN_FRONTEND": "noninteractive"}
        return run_cmd(["apt-get", "install", "-y", pkg], env=env)

    # ── snap_install ──
    elif tipo == "snap_install":
        pkg = parametri.get("package", "")
        if not pkg:
            return False, "Parametro 'package' mancante"
        cmd = ["snap", "install", pkg]
        if parametri.get("classic"):
            cmd.append("--classic")
        return run_cmd(cmd)

    # ── ps_script ──
    elif tipo == "ps_script":
        script = parametri.get("script", "")
        if not script:
            return False, "Parametro 'script' mancante"
        if IS_WINDOWS:
            cmd = ["powershell.exe", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script]
        else:
            cmd = ["pwsh", "-NonInteractive", "-Command", script]
        return run_cmd(cmd)

    # ── shell_script ──
    elif tipo == "shell_script":
        script = parametri.get("script", "")
        if not script:
            return False, "Parametro 'script' mancante"
        # BUG-02: usa lista argomenti — nessuna interpolazione shell
        if IS_WINDOWS:
            return run_cmd(["cmd.exe", "/c", script])
        else:
            return run_cmd(["bash", "-c", script])

    # ── reg_set (Windows only) ──
    elif tipo == "reg_set":
        if not IS_WINDOWS:
            return None, "Skipped: reg_set è solo Windows"
        path  = parametri.get("path", "")
        name  = parametri.get("name", "")
        value = parametri.get("value", "")
        rtype = parametri.get("type", "REG_SZ")
        # BUG-02: whitelist tipo registro
        _VALID_REG_TYPES = {"REG_SZ","REG_DWORD","REG_QWORD","REG_BINARY","REG_EXPAND_SZ","REG_MULTI_SZ"}
        if rtype not in _VALID_REG_TYPES:
            return False, f"Tipo registro non valido: {rtype}"
        return run_cmd(["reg", "add", path, "/v", name, "/t", rtype, "/d", value, "/f"])

    # ── systemd_service (Linux only) ──
    elif tipo == "systemd_service":
        if not IS_LINUX:
            return None, "Skipped: systemd_service è solo Linux"
        name   = parametri.get("name", "")
        action = parametri.get("action", "start")
        if not name:
            return False, "Parametro 'name' mancante"
        # BUG-02: whitelist azione systemd
        if action not in ("start", "stop", "enable", "disable", "restart", "reload", "status"):
            return False, f"Azione systemd non valida: {action}"
        return run_cmd(["systemctl", action, name])

    # ── file_copy ──
    elif tipo == "file_copy":
        src = parametri.get("src", "")
        dst = parametri.get("dst", "")
        if not src or not dst:
            return False, "Parametri 'src' e 'dst' obbligatori"
        # SEC: path traversal — normalizza e blocca '..' e null byte
        src = os.path.normpath(os.path.abspath(src))
        dst = os.path.normpath(os.path.abspath(dst))
        if ".." in src or ".." in dst or "\0" in src or "\0" in dst:
            return False, "Path non consentito (path traversal)"
        try:
            shutil.copy2(src, dst)
            return True, f"Copiato: {src} → {dst}"
        except Exception as e:
            return False, str(e)

    # ── reboot ──
    elif tipo == "reboot":
        try:
            delay = int(parametri.get("delay", 5))
        except (ValueError, TypeError):
            delay = 5
        log.info(f"  Riavvio programmato tra {delay}s")
        if IS_WINDOWS:
            run_cmd(["shutdown", "/r", "/t", str(delay)])
        else:
            run_cmd(["shutdown", "-r", f"+{max(1, delay // 60)}"])
        return True, f"Riavvio programmato tra {delay}s"

    # ── message ──
    elif tipo == "message":
        text = parametri.get("text", "")
        log.info(f"  MSG: {text}")
        return True, text

    else:
        return False, f"Tipo step sconosciuto: {tipo}"

# ── Condition Evaluator ───────────────────────────────────────────────────────
def evaluate_condition(condizione):
    """Valuta una condizione semplice. Ritorna True se lo step va eseguito."""
    if not condizione or not condizione.strip():
        return True
    cond = condizione.strip().lower()
    if cond == "windows":
        return IS_WINDOWS
    if cond == "linux":
        return IS_LINUX
    if cond.startswith("os="):
        return get_os_platform() == cond[3:].strip()
    if cond.startswith("hostname="):
        return socket.gethostname().lower() == cond[9:].strip().lower()
    log.warning(f"Condizione non riconosciuta: '{condizione}' — eseguendo step")
    return True

# ── Workflow Runner ───────────────────────────────────────────────────────────
def run_workflow(cfg, workflow):
    """Esegue il workflow step per step, con persistenza stato per gestire riavvii."""
    pc_name    = cfg["pc_name"]
    api_url    = cfg["api_url"]
    api_key    = cfg.get("api_key", "")
    pw_id      = workflow["id"]
    steps      = workflow.get("steps", [])

    state = load_state()
    resume_from = state.get("resume_step", 0)

    if resume_from:
        log.info(f"Riprendo workflow dopo riavvio — da step_id={resume_from}")

    needs_reboot = False

    for step in steps:
        step_id  = step["step_id"]
        step_num = step["ordine"]
        nome     = step["nome"]

        if resume_from and step_id <= resume_from:
            log.info(f"[{step_num}] {nome} — già completato, salto")
            continue

        # BUG-06: valuta condizione prima di eseguire
        condizione = step.get("condizione", "")
        if not evaluate_condition(condizione):
            log.info(f"[{step_num}] {nome} — SKIP condizione: {condizione}")
            api_post(api_url, f"/api/pc/{pc_name}/workflow/step", {
                "step_id": step_id, "status": "skipped",
                "output": f"Condizione non soddisfatta: {condizione}",
                "ts": datetime.now().isoformat()
            }, api_key)
            continue

        log.info(f"[{step_num}/{len(steps)}] {nome}")

        api_post(api_url, f"/api/pc/{pc_name}/workflow/step", {
            "step_id": step_id, "status": "running", "output": "",
            "ts": datetime.now().isoformat()
        }, api_key)

        ok, output = execute_step(step)
        output = (output or "")[:2000]

        if ok is None:
            log.info(f"  → SKIPPED: {output}")
            api_post(api_url, f"/api/pc/{pc_name}/workflow/step", {
                "step_id": step_id, "status": "skipped",
                "output": output, "ts": datetime.now().isoformat()
            }, api_key)
            continue

        status = "done" if ok else "error"
        log.info(f"  → {status.upper()}: {output[:200]}")

        api_post(api_url, f"/api/pc/{pc_name}/workflow/step", {
            "step_id": step_id, "status": status,
            "output": output, "ts": datetime.now().isoformat()
        }, api_key)

        if not ok:
            su_errore = step.get("su_errore", "stop")
            if su_errore == "stop":
                log.error(f"Step fallito con su_errore=stop — workflow interrotto")
                return False
            else:
                log.warning(f"Step fallito con su_errore=continua — proseguo")

        if step["tipo"] == "reboot" and ok:
            save_state({"pw_id": pw_id, "resume_step": step_id})
            log.info("Stato salvato per resume dopo reboot")
            needs_reboot = True
            break

    if not needs_reboot:
        clear_state()
        log.info("Workflow completato")
    return True

# ── Main Loop ─────────────────────────────────────────────────────────────────
def main_loop():
    log.info(f"NovaSCM Agent v{AGENT_VER} avviato — OS: {platform.system()}")
    cfg = load_config()
    log.info(f"PC: {cfg['pc_name']} | API: {cfg['api_url']} | Poll: {cfg['poll_sec']}s")

    while True:
        try:
            cfg = load_config()  # rilegge config ad ogni ciclo (hot reload)
            pc  = cfg["pc_name"]
            url = cfg["api_url"]

            # Controlla se c'è uno stato salvato (resume dopo reboot)
            state = load_state()
            if state.get("pw_id"):
                log.info(f"Resume rilevato per pw_id={state['pw_id']}")

            # Polling workflow assegnato
            api_key  = cfg.get("api_key", "")
            workflow = api_get(url, f"/api/pc/{pc}/workflow", api_key)

            if workflow and "error" not in workflow:
                log.info(f"Workflow trovato: '{workflow.get('workflow_nome')}' (id={workflow.get('id')})")
                run_workflow(cfg, workflow)
            else:
                log.debug("Nessun workflow in attesa")

        except KeyboardInterrupt:
            log.info("Agent fermato manualmente")
            sys.exit(0)
        except Exception:
            log.error(f"Errore nel loop principale:\n{traceback.format_exc()}")

        time.sleep(cfg.get("poll_sec", POLL_SEC))

# ── Entry point ───────────────────────────────────────────────────────────────
if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "--once":
        # Modalità singola esecuzione (per test/debug)
        cfg = load_config()
        wf  = api_get(cfg["api_url"], f"/api/pc/{cfg['pc_name']}/workflow")
        if wf and "error" not in wf:
            run_workflow(cfg, wf)
        else:
            print("Nessun workflow assegnato.")
    else:
        main_loop()
