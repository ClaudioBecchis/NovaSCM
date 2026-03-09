# DeployScreen v3 — Specifiche per Claude Code

## Obiettivo

Aggiornare il progetto `DeployScreen/` da v1 (layout 2 colonne semplice) a v3 con le seguenti nuove feature:

1. **HW Strip** — banda hardware sotto l'header (CPU, RAM, Disco, Rete MAC/IP)
2. **Log in tempo reale** — box scorrevole con output degli step
3. **Stima tempo rimanente** — colonna `~Xs` sugli step futuri + stat "STIMA FIN."
4. **Screenshot finale** — overlay di completamento con anteprima immagine dal server

Il design di riferimento è visibile in `server/web/deploy-client.html` (v3).

---

## File da modificare / creare

```
DeployScreen/
├── NovaSCMDeployScreen.csproj   ← nessuna modifica
├── App.xaml                     ← nessuna modifica
├── App.xaml.cs                  ← nessuna modifica
├── MainWindow.xaml              ← SOSTITUIRE con versione v3
└── MainWindow.xaml.cs           ← SOSTITUIRE con versione v3
```

I file aggiornati si trovano già nel repository a partire dal commit più recente.
**Claude Code deve usare i file già presenti in `DeployScreen/MainWindow.xaml` e `DeployScreen/MainWindow.xaml.cs` come punto di partenza.**

---

## Modifiche API server richieste

Per supportare le nuove feature, `server/api.py` deve essere aggiornato.

### 1. Aggiungere campo `hardware` alla risposta `GET /api/pc-workflows/<id>`

La route deve includere i dati hardware se disponibili nella tabella `pc_hardware`.

**Schema tabella (da aggiungere in `init_db()`):**
```sql
CREATE TABLE IF NOT EXISTS pc_hardware (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    pc_name     TEXT NOT NULL UNIQUE,
    cpu         TEXT,
    ram         TEXT,
    disk        TEXT,
    mac         TEXT,
    ip          TEXT,
    updated_at  TEXT DEFAULT (datetime('now'))
);
```

**Risposta JSON aggiornata:**
```json
{
  "id": 14,
  "pc_name": "WKS-MKTG-042",
  "status": "running",
  "hardware": {
    "cpu": "Intel Core i5-12400",
    "ram": "16 GB DDR4",
    "disk": "Samsung SSD 980 500GB",
    "mac": "00:1A:2B:3C:4D:5E",
    "ip": "192.168.10.42"
  },
  "steps": [ ... ]
}
```

### 2. Aggiungere route `POST /api/pc/<pc_name>/hardware`

L'agente chiama questa route all'avvio per registrare i dati hardware.

**Request body:**
```json
{
  "cpu": "Intel Core i5-12400",
  "ram": "16 GB DDR4",
  "disk": "Samsung SSD 980 500GB",
  "mac": "00:1A:2B:3C:4D:5E",
  "ip": "192.168.10.42"
}
```

**Response:** `{"status": "ok"}`

**Implementazione:**
```python
@app.route("/api/pc/<pc_name>/hardware", methods=["POST"])
@require_api_key
def post_hardware(pc_name):
    data = request.json or {}
    with get_db_ctx() as conn:
        conn.execute("""
            INSERT INTO pc_hardware (pc_name, cpu, ram, disk, mac, ip)
            VALUES (?, ?, ?, ?, ?, ?)
            ON CONFLICT(pc_name) DO UPDATE SET
                cpu=excluded.cpu, ram=excluded.ram, disk=excluded.disk,
                mac=excluded.mac, ip=excluded.ip, updated_at=datetime('now')
        """, (pc_name, data.get("cpu",""), data.get("ram",""),
              data.get("disk",""), data.get("mac",""), data.get("ip","")))
    return jsonify({"status": "ok"})
```

### 3. Aggiungere campo `log` agli step nella risposta `GET /api/pc-workflows/<id>`

Ogni step nella risposta deve includere l'output dell'ultima esecuzione.

**Schema tabella `pc_workflow_steps` — aggiungere colonna:**
```sql
ALTER TABLE pc_workflow_steps ADD COLUMN log TEXT DEFAULT '';
```

**Aggiornare route `POST /api/pc/<pc_name>/workflow/step`:**
```python
# Aggiungere al body accettato:
log = data.get("log", "")

# Salvare nel DB:
conn.execute("""
    UPDATE pc_workflow_steps
    SET stato=?, output=?, log=?, updated_at=datetime('now')
    WHERE id=?
""", (stato, output, log, step_record["id"]))
```

**Risposta step aggiornata:**
```json
{
  "step_id": 5,
  "nome": "Driver chipset",
  "tipo": "winget_install",
  "status": "running",
  "elapsed_sec": 12.4,
  "est_sec": 22.0,
  "log": "[INFO] Avvio...\n[INFO] Download...\n[OK] Completato"
}
```

### 4. Aggiungere campo `screenshot` alla risposta `GET /api/pc-workflows/<id>` (quando status=completed)

**Schema tabella — aggiungere colonna a `pc_workflows`:**
```sql
ALTER TABLE pc_workflows ADD COLUMN screenshot_b64 TEXT DEFAULT '';
```

**Aggiungere route `POST /api/pc-workflows/<id>/screenshot`:**
```python
@app.route("/api/pc-workflows/<int:pw_id>/screenshot", methods=["POST"])
@require_api_key
def post_screenshot(pw_id):
    data = request.json or {}
    b64 = data.get("screenshot_b64", "")
    with get_db_ctx() as conn:
        conn.execute("UPDATE pc_workflows SET screenshot_b64=? WHERE id=?", (b64, pw_id))
    return jsonify({"status": "ok"})
```

**Nella risposta `GET /api/pc-workflows/<id>`, quando status=completed:**
```python
# Includere screenshot_b64 solo se presente (per non appesantire le risposte normali)
if row["status"] == "completed" and row.get("screenshot_b64"):
    result["screenshot"] = row["screenshot_b64"]
```

### 5. Aggiungere campo `est_sec` agli step

Il campo `est_sec` rappresenta la stima in secondi per ogni step, calcolata come media delle ultime 5 esecuzioni completate dello stesso step/tipo.

**Calcolo da aggiungere nella query degli step:**
```python
# Per ogni step, calcolare la media elapsed degli ultimi 5 completati
est = conn.execute("""
    SELECT AVG(elapsed_sec) FROM (
        SELECT elapsed_sec FROM pc_workflow_steps
        WHERE step_id=? AND stato='done' AND elapsed_sec > 0
        ORDER BY updated_at DESC LIMIT 5
    )
""", (step_id,)).fetchone()[0]
step_data["est_sec"] = round(est, 1) if est else 0
```

---

## Modifiche agente Windows (`agent/novascm-agent.py`)

### 1. Rilevamento hardware all'avvio

Aggiungere funzione `collect_hardware()` chiamata una volta all'avvio:

```python
import subprocess, re

def collect_hardware():
    """Raccoglie info hardware via WMI (disponibile anche in WinPE con il pacchetto WMI)"""
    hw = {}
    try:
        # CPU
        out = subprocess.check_output(
            ["wmic", "cpu", "get", "name", "/value"], text=True, stderr=subprocess.DEVNULL)
        m = re.search(r"Name=(.+)", out)
        hw["cpu"] = m.group(1).strip() if m else ""

        # RAM (totale in GB)
        out = subprocess.check_output(
            ["wmic", "computersystem", "get", "TotalPhysicalMemory", "/value"],
            text=True, stderr=subprocess.DEVNULL)
        m = re.search(r"TotalPhysicalMemory=(\d+)", out)
        if m:
            gb = int(m.group(1)) // (1024**3)
            hw["ram"] = f"{gb} GB"

        # Disco (primo disco fisico)
        out = subprocess.check_output(
            ["wmic", "diskdrive", "get", "Model,Size", "/value"],
            text=True, stderr=subprocess.DEVNULL)
        model_m = re.search(r"Model=(.+)", out)
        size_m  = re.search(r"Size=(\d+)", out)
        if model_m:
            model = model_m.group(1).strip()
            size  = f" {int(size_m.group(1)) // (1024**3)}GB" if size_m else ""
            hw["disk"] = model + size

        # MAC e IP (prima scheda di rete attiva)
        out = subprocess.check_output(
            ["wmic", "nicconfig", "where", "IPEnabled=True",
             "get", "MACAddress,IPAddress", "/value"],
            text=True, stderr=subprocess.DEVNULL)
        mac_m = re.search(r"MACAddress=(.+)", out)
        ip_m  = re.search(r'IPAddress=\{"?([0-9.]+)', out)
        hw["mac"] = mac_m.group(1).strip() if mac_m else ""
        hw["ip"]  = ip_m.group(1).strip()  if ip_m  else ""
    except Exception as e:
        log(f"[WARN] collect_hardware: {e}")
    return hw

# Chiamare in main(), dopo aver ottenuto pc_name:
hw = collect_hardware()
if hw:
    try:
        requests.post(f"{SERVER}/api/pc/{pc_name}/hardware",
                      json=hw, headers=HEADERS, timeout=5)
    except Exception: pass
```

### 2. Inviare log degli step

Accumulare l'output di ogni step e inviarlo con l'aggiornamento di stato:

```python
def run_step(step, pw_id, pc_name):
    log_lines = []

    def capture(line):
        log_lines.append(line)
        log(line)  # print locale

    # ... esecuzione step con capture dell'output ...

    # Al completamento, inviare log insieme allo stato:
    requests.post(f"{SERVER}/api/pc/{pc_name}/workflow/step",
        json={
            "pw_id":   pw_id,
            "step_id": step["id"],
            "stato":   "done",
            "output":  "\n".join(log_lines[-50:]),  # ultimi 50 log
            "log":     "\n".join(log_lines[-100:]),
        },
        headers=HEADERS, timeout=10)
```

---

## Test da aggiungere (`server/tests/test_api.py`)

```python
def test_post_hardware(client):
    """POST /api/pc/<name>/hardware salva i dati"""
    r = client.post("/api/pc/WKS-TEST/hardware",
        json={"cpu": "i5-12400", "ram": "16 GB", "disk": "SSD 500GB",
              "mac": "AA:BB:CC:DD:EE:FF", "ip": "192.168.10.1"},
        headers={"X-Api-Key": "test"})
    assert r.status_code == 200

def test_hardware_in_workflow_response(client, sample_workflow):
    """GET /api/pc-workflows/<id> include hardware se presente"""
    client.post("/api/pc/WKS-TEST/hardware",
        json={"cpu": "i5-12400", "ram": "16 GB", "disk": "SSD 500GB",
              "mac": "AA:BB:CC:DD:EE:FF", "ip": "192.168.10.1"},
        headers={"X-Api-Key": "test"})
    r = client.get(f"/api/pc-workflows/{sample_workflow}",
        headers={"X-Api-Key": "test"})
    assert "hardware" in r.json
    assert r.json["hardware"]["cpu"] == "i5-12400"

def test_step_log_saved(client, sample_pw):
    """POST /api/pc/<name>/workflow/step salva il campo log"""
    # ... setup + assert log presente nella risposta ...

def test_screenshot_upload(client, sample_pw):
    """POST /api/pc-workflows/<id>/screenshot salva e restituisce screenshot"""
    fake_b64 = "aGVsbG8="  # "hello" in base64
    r = client.post(f"/api/pc-workflows/{sample_pw}/screenshot",
        json={"screenshot_b64": fake_b64},
        headers={"X-Api-Key": "test"})
    assert r.status_code == 200
    # Dopo completamento workflow, screenshot presente nella risposta
```

---

## Ordine di esecuzione consigliato

1. `init_db()` — aggiungere tabella `pc_hardware`, colonne `log` e `screenshot_b64`
2. `POST /api/pc/<name>/hardware` — nuova route
3. `POST /api/pc-workflows/<id>/screenshot` — nuova route
4. `GET /api/pc-workflows/<id>` — aggiornare con hardware, log steps, screenshot, est_sec
5. `agent/novascm-agent.py` — aggiungere `collect_hardware()` e invio log
6. Test: `pytest tests/ -v` → target **90 passed** (da 84)

---

## Nota sui file WPF

I file `DeployScreen/MainWindow.xaml` e `DeployScreen/MainWindow.xaml.cs` sono già stati aggiornati alla v3. Claude Code non deve modificarli, solo verificare che compilino correttamente con:

```powershell
cd DeployScreen
dotnet build -c Release
```

Se la build fallisce, correggere gli errori di compilazione prima di procedere con le modifiche al server.

---

## Versione target: v1.8.2

```
git commit -m "feat: hardware strip, log realtime, ETA, screenshot — v1.8.2"
```
