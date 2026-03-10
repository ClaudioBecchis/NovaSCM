# NovaSCM — Round 8 · Code Review v2.0.0-alpha.1

**Commit analizzato:** `3b79018` (2026-03-10)  
**File esaminati:** `server/api.py` (1677 righe), `NovaSCMAgent/Worker.cs`, `NovaSCMAgent/ApiClient.cs`, `NovaSCMAgent/StepExecutor.cs`, `NovaSCMAgent/AgentConfig.cs`, `server/tests/test_api.py` (677 righe), `server/version.json`

---

## Riepilogo

| Severità | N° | Titolo |
|---|---|---|
| 🔴 CRITICAL | 3 | Endpoint senza autenticazione (step write, step read, deploy-screen download) |
| 🟡 MEDIUM   | 2 | DELETE /cr non rimuove pc_workflows · IP privato hardcoded in version.json |
| 🔵 INFO     | 3 | elapsed_sec mai inviato · API key in process args · Test con endpoint inesistenti |

---

## 🔴 C-1 · `server/api.py` riga 598 — CRITICAL

**`POST /api/cr/by-name/<pc_name>/step` senza autenticazione.**

```python
@app.route("/api/cr/by-name/<pc_name>/step", methods=["POST"])
def report_step(pc_name):    # ← nessun @require_auth
```

Chiunque può scrivere step arbitrari su qualsiasi CR senza API key: modificare lo stato di step esistenti, iniettare step falsi, alterare `last_seen`. In un setup esposto su internet (tramite Cloudflare Tunnel o simili) questo permette di manipolare lo stato di deployment di qualsiasi PC.

**Fix:**
```python
@app.route("/api/cr/by-name/<pc_name>/step", methods=["POST"])
@require_auth
def report_step(pc_name):
```

> **Nota:** se il client legacy (deploy-client.html o agente WinPE) non gestisce l'header `X-Api-Key`, aggiungere la key al file `agent.json` e inviarla come header nelle chiamate `/step`.

---

## 🔴 C-2 · `server/api.py` riga 618 — CRITICAL

**`GET /api/cr/by-name/<pc_name>/steps` senza autenticazione.**

```python
@app.route("/api/cr/by-name/<pc_name>/steps", methods=["GET"])
def get_steps_by_name(pc_name):    # ← nessun @require_auth
```

Chiunque conosca (o indovini) un `pc_name` può leggere: ID della CR, stato del deploy, lista degli step completati con timestamp. Espone informazioni sul processo di deployment senza richiedere credenziali.

**Fix:**
```python
@app.route("/api/cr/by-name/<pc_name>/steps", methods=["GET"])
@require_auth
def get_steps_by_name(pc_name):
```

---

## 🔴 C-3 · `server/api.py` riga 1621 — CRITICAL

**`GET /api/download/deploy-screen` senza autenticazione.**

```python
@app.route("/api/download/deploy-screen", methods=["GET"])
def download_deploy_screen():    # ← nessun @require_auth
```

Il binario `NovaSCMDeployScreen.exe` è scaricabile da chiunque senza API key. Rispetto agli altri download (agent, NovaSCM.exe) — tutti protetti da `@require_auth` — questo è l'unico endpoint di download esposto pubblicamente. Incoerenza di design che espone un eseguibile firmato/non firmato a chiunque.

**Fix:**
```python
@app.route("/api/download/deploy-screen", methods=["GET"])
@require_auth
def download_deploy_screen():
```

> **Attenzione:** l'agente chiama questo endpoint durante il deploy (vedi `Worker.cs → LaunchDeployScreen`). Se si aggiunge `@require_auth`, l'agente deve includere `X-Api-Key` nella chiamata di download, che già fa per tutti gli altri endpoint tramite `ApiClient`.  
> Verifica che `download_deploy_screen` venga chiamato direttamente dall'agente o da un installer separato, e adegua di conseguenza.

---

## 🟡 M-1 · `server/api.py` riga 371 — MEDIUM

**`DELETE /api/cr/{cr_id}` non elimina i `pc_workflows` associati al PC.**

```python
def delete_cr(cr_id):
    conn.execute("DELETE FROM cr_steps WHERE cr_id=?", (cr_id,))
    conn.execute("DELETE FROM cr       WHERE id=?",   (cr_id,))
    # ← pc_workflows con pc_name=cr.pc_name rimangono nel DB
```

`pc_workflows` non ha una FK diretta su `cr.id` (è legato per `pc_name`, non per `cr_id`). Cancellando una CR, restano `pc_workflows` orfani con workflow ancora assegnati a quel `pc_name`. Al successivo boot PXE dello stesso MAC, l'agente troverà workflow "pending" residui e li eseguirà nuovamente.

**Fix — aggiungere la DELETE esplicita in `delete_cr`:**
```python
def delete_cr(cr_id):
    with get_db_ctx() as conn:
        row = conn.execute("SELECT pc_name FROM cr WHERE id=?", (cr_id,)).fetchone()
        if not row:
            return jsonify({"error": "Non trovato"}), 404
        pc_name = row["pc_name"]
        conn.execute("DELETE FROM cr_steps    WHERE cr_id=?",   (cr_id,))
        conn.execute("DELETE FROM pc_workflows WHERE pc_name=?", (pc_name,))
        conn.execute("DELETE FROM cr           WHERE id=?",      (cr_id,))
        conn.commit()
    return jsonify({"ok": True})
```

---

## 🟡 M-2 · `server/version.json` — MEDIUM

**IP privato hardcoded nel file `version.json` committato nel repo pubblico.**

```json
{
  "version": "2.0.0-alpha.1",
  "url": "http://192.168.20.110:9091/api/download/NovaSCM.exe",
  ...
}
```

L'IP `192.168.20.110` è l'indirizzo interno del server NovaSCM nel homelab (VLAN 20 Servers). Committato in chiaro in un repository pubblico GitHub. Chiunque legga il repo conosce la topologia di rete interna.

**Fix — usare placeholder o URL relativo:**
```json
{
  "version": "2.0.0-alpha.1",
  "url": "",
  "notes": "v2.0.0-alpha.1: PXE Boot + TFTP Server + DeployScreen integration."
}
```

Impostare `url` vuoto e configurarlo a runtime tramite la variabile d'ambiente `NOVASCM_PUBLIC_URL` già presente in `api.py` (funzione `_get_public_url()`). Oppure generare `version.json` dinamicamente dall'endpoint `/api/version` che già usa `_get_public_url()`.

---

## 🔵 I-1 · `NovaSCMAgent/ApiClient.cs` — INFO

**`elapsed_sec` mai popolato dall'agente → sempre 0 nell'UI.**

Lo schema DB ha la colonna `pc_workflow_steps.elapsed_sec` (migration riga 269), la query di `get_pc_workflow` la legge e la restituisce (riga 879), ma `ApiClient.ReportStepAsync` non la invia mai:

```csharp
var body = JsonSerializer.Serialize(new {
    step_id = stepId,
    status,
    output  = ...,
    ts      = DateTime.Now.ToString("o")
    // ← elapsed_sec mancante
});
```

Il server, nel `report_wf_step`, non gestisce `elapsed_sec` in ingresso (INSERT/UPDATE su `pc_workflow_steps` non include la colonna).

**Fix lato agente:** misurare la durata e inviarla; **fix lato server:** riceverla e salvarla:

```csharp
// Worker.cs — misura durata step
var sw = Stopwatch.StartNew();
var result = await _exec.ExecuteAsync(step, ct);
sw.Stop();
await _api.ReportStepAsync(..., elapsed: sw.Elapsed.TotalSeconds, ...);
```

```python
# api.py — report_wf_step: aggiungere elapsed_sec alla UPSERT
elapsed = data.get("elapsed_sec", 0)
conn.execute("""
    INSERT INTO pc_workflow_steps (pc_workflow_id, step_id, status, output, timestamp, elapsed_sec)
    VALUES (?,?,?,?,?,?)
    ON CONFLICT(pc_workflow_id, step_id)
    DO UPDATE SET status=excluded.status, output=excluded.output,
                  timestamp=excluded.timestamp, elapsed_sec=excluded.elapsed_sec
""", (pw_id, step_id, status, output, ts, elapsed))
```

---

## 🔵 I-2 · `NovaSCMAgent/Worker.cs` riga 74 — INFO

**API key esposta in chiaro negli argomenti di processo di `NovaSCMDeployScreen.exe`.**

```csharp
var args = $"hostname={cfg.PcName} domain={cfg.Domain} pw_id={pwId} " +
           $"server={cfg.ApiUrl} key={cfg.ApiKey} wf={Uri.EscapeDataString(wfNome)}";
System.Diagnostics.Process.Start(new ProcessStartInfo {
    FileName  = exePath,
    Arguments = args,     // ← key visibile in Task Manager / ps aux
    ...
});
```

Su Windows, qualsiasi processo con accesso al Task Manager può leggere la command line di `NovaSCMDeployScreen.exe` e ottenere la API key. Il PC in deploy è in WinPE/OOBE quindi l'esposizione è limitata, ma resta una cattiva pratica.

**Fix — passare la key tramite variabile d'ambiente:**
```csharp
var psi = new ProcessStartInfo {
    FileName        = exePath,
    Arguments       = $"hostname={cfg.PcName} domain={cfg.Domain} pw_id={pwId} server={cfg.ApiUrl} wf={Uri.EscapeDataString(wfNome)}",
    UseShellExecute = false,
};
psi.EnvironmentVariables["NOVASCM_API_KEY"] = cfg.ApiKey;
Process.Start(psi);
```

E in `NovaSCMDeployScreen` leggere `Environment.GetEnvironmentVariable("NOVASCM_API_KEY")` invece del parametro `key=`.

---

## 🔵 I-3 · `server/tests/test_api.py` riga 636 — INFO

**`test_delete_cr_also_removes_cr_steps` usa endpoint inesistenti — test non verifica nulla di utile.**

```python
def test_delete_cr_also_removes_cr_steps(self, client):
    ...
    client.post("/api/checkin",   # ← endpoint NON ESISTE (404 silenzioso)
        json={"hostname": "STEP-PC"}, headers=AUTH)
    client.post("/api/step",      # ← endpoint NON ESISTE (404 silenzioso)
        json={"hostname": "STEP-PC", "step": "postinstall_start", ...}, headers=AUTH)
    client.delete(f"/api/cr/{cr_id}", headers=AUTH)
    r = client.get(f"/api/cr/{cr_id}", headers=AUTH)
    assert r.status_code == 404   # ← passa, ma non ha mai creato cr_steps
```

Il test non crea mai `cr_steps` reali (le POST vanno a endpoint sbagliati), quindi non verifica il cascade delete. Passa per il motivo sbagliato.

**Fix — usare gli endpoint corretti:**
```python
def test_delete_cr_also_removes_cr_steps(self, client):
    cr = client.post("/api/cr",
                     json={"pc_name": "STEP-PC", "domain": "test.local"},
                     headers=AUTH).get_json()
    cr_id = cr["id"]
    # Endpoint corretto per cr_steps
    client.post("/api/cr/by-name/STEP-PC/step",
                json={"step": "postinstall_start", "status": "done"},
                headers=AUTH)
    # Verifica che lo step esista prima di cancellare
    steps_before = client.get(f"/api/cr/{cr_id}/steps", headers=AUTH).get_json()
    assert steps_before["total"] == 1
    # Delete CR
    client.delete(f"/api/cr/{cr_id}", headers=AUTH)
    # CR non deve più esistere
    r = client.get(f"/api/cr/{cr_id}", headers=AUTH)
    assert r.status_code == 404
    # Opzionale: verifica diretta su DB che cr_steps sia vuoto
```

---

## Recap fix prioritari

| # | File | Riga | Fix |
|---|---|---|---|
| C-1 🔴 | `server/api.py` | 598 | Aggiungere `@require_auth` a `report_step` |
| C-2 🔴 | `server/api.py` | 618 | Aggiungere `@require_auth` a `get_steps_by_name` |
| C-3 🔴 | `server/api.py` | 1621 | Aggiungere `@require_auth` a `download_deploy_screen` |
| M-1 🟡 | `server/api.py` | 371 | `delete_cr` elimina anche `pc_workflows` per `pc_name` |
| M-2 🟡 | `server/version.json` | — | Rimuovere IP privato, usare `url: ""` |
| I-1 🔵 | `ApiClient.cs` + `api.py` | — | Inviare e salvare `elapsed_sec` |
| I-2 🔵 | `Worker.cs` | 74 | Passare API key via env var, non via argomento CLI |
| I-3 🔵 | `test_api.py` | 636 | Correggere test `test_delete_cr_also_removes_cr_steps` |
