# NovaSCM — Deploy Screen Integration
## Report per Claude Code — v1.9.0

**Data:** 2026-03-10  
**Repository:** https://github.com/ClaudioBecchis/NovaSCM  
**Scope:** Integrazione schermata grafica WPF nei deploy Windows + fix bug + nuovi endpoint server

---

## CONTESTO

NovaSCM ha un'applicazione WPF (`DeployScreen/`) che mostra una schermata grafica fullscreen durante il deploy dei PC Windows. Questa schermata **esiste già su GitHub ma non viene mai lanciata automaticamente dall'agente**. L'obiettivo di questo task è:

1. **Lanciare automaticamente** `NovaSCMDeployScreen.exe` all'avvio del deploy su ogni PC Windows
2. **Correggere 3 bug critici** nel codice WPF esistente
3. **Aggiungere endpoint server** per ricevere HW info, log e screenshot dall'agente
4. **Aggiornare l'agente Windows** per raccogliere HW info via WMIC e inviare log/screenshot
5. **Integrare nel `autounattend.xml`** generato dal server per il lancio in WinPE

---

## PARTE 1 — BUG FIX WPF (`DeployScreen/MainWindow.xaml.cs`)

### BUG C-1 🔴 CRITICO — `RunDemoStep` crea `DispatcherTimer` su thread background → CRASH

**File:** `DeployScreen/MainWindow.xaml.cs`  
**Problema:** La chiamata ricorsiva usa `Task.ContinueWith` che gira su thread pool. Al suo interno viene creato un `DispatcherTimer`, che richiede il thread UI → `InvalidOperationException` a runtime.

**Codice attuale (riga ~220):**
```csharp
Task.Delay(220).ContinueWith(_ => RunDemoStep(idx + 1));
```

**Fix:**
```csharp
Task.Delay(220).ContinueWith(_ => Dispatcher.Invoke(() => RunDemoStep(idx + 1)));
```

---

### BUG M-1 🟡 MEDIO — `LogScroller.ScrollToEnd()` troppo presto → log non si autoscorre

**File:** `DeployScreen/MainWindow.xaml.cs` — metodo `AddLogLine()`  
**Problema:** `ScrollToEnd()` viene chiamato prima che `ItemsControl` abbia renderizzato la nuova riga.

**Codice attuale:**
```csharp
_logs.Add(new LogLine { Text = text, Color = color });
LogLinesCount.Text = $"{_logs.Count} ...";
LogScroller.ScrollToEnd();
```

**Fix:**
```csharp
_logs.Add(new LogLine { Text = text, Color = color });
LogLinesCount.Text = $"{_logs.Count} {(_logs.Count == 1 ? "riga" : "righe")}";
Dispatcher.InvokeAsync(() => LogScroller.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Background);
```

---

### BUG M-2 🟡 MEDIO — `ColorStepRow` no-op al primo step → cerchi restano grigi

**File:** `DeployScreen/MainWindow.xaml.cs` — metodo `RunDemoStep()`  
**Problema:** Quando viene chiamato `ColorStepRow(0, ...)` subito dopo la creazione della lista, i container WPF non sono ancora stati generati dall'`ItemContainerGenerator`.

**Codice attuale (in `RunDemoStep`):**
```csharp
Dispatcher.Invoke(() =>
{
    _steps[idx].Status  = StepStatus.Active;
    // ...
    ColorStepRow(idx, StepStatus.Active);  // ← no-op al primo step
    // ...
});
```

**Fix — aggiungere `DispatcherPriority.Loaded`:**
```csharp
Dispatcher.Invoke(() =>
{
    _steps[idx].Status  = StepStatus.Active;
    _steps[idx].Elapsed = 0;
    _activeIdx = idx;
    UpdateStatsUI();
    UpdateCurBox(_steps[idx]);
    ScrollToActive();
    // Log
    _logs.Clear();
    LogStepName.Text   = _steps[idx].Nome;
    LogLinesCount.Text = "0 righe";
    var lines = DEMO_LOGS.TryGetValue(idx + 1, out var l) ? l
        : new[] { $"[INFO] Avvio {_steps[idx].Nome}…", "[INFO] Esecuzione in corso…" };
    int li = 0;
    void AddNext()
    {
        if (li < lines.Length)
        {
            var line = lines[li++];
            AddLogLine(line);
            Task.Delay((int)(280 + new Random().NextDouble() * 400))
                .ContinueWith(_ => Dispatcher.Invoke(AddNext));
        }
    }
    Task.Delay(300).ContinueWith(_ => Dispatcher.Invoke(AddNext));
});
// FIX M-2: colora DOPO che i container sono stati generati
Dispatcher.InvokeAsync(() => ColorStepRow(idx, StepStatus.Active),
    System.Windows.Threading.DispatcherPriority.Loaded);
```

---

## PARTE 2 — NUOVI ENDPOINT SERVER (`server/api.py`)

Aggiungere 3 nuovi endpoint alla fine di `api.py`, prima della sezione `if __name__ == "__main__"`.

### Endpoint 2.1 — `POST /api/pc-workflows/<pw_id>/hardware`

Riceve le informazioni hardware del PC client durante il deploy.

```python
@app.route("/api/pc-workflows/<int:pw_id>/hardware", methods=["POST"])
@require_auth
def post_pw_hardware(pw_id):
    """Riceve HW info dal client: cpu, ram, disk, mac, ip."""
    data = request.get_json(silent=True) or {}
    required = ("cpu", "ram", "disk", "mac", "ip")
    missing = [f for f in required if not data.get(f)]
    if missing:
        return jsonify({"error": f"Campi mancanti: {', '.join(missing)}"}), 400
    now = datetime.datetime.utcnow().isoformat()
    with get_db() as conn:
        row = conn.execute("SELECT id FROM pc_workflows WHERE id=?", (pw_id,)).fetchone()
        if not row:
            return jsonify({"error": f"pc_workflow {pw_id} non trovato"}), 404
        # Aggiungi colonne se non esistono (migrazione lazy)
        for col in ("hw_cpu", "hw_ram", "hw_disk", "hw_mac", "hw_ip", "hw_received_at"):
            try:
                conn.execute(f"ALTER TABLE pc_workflows ADD COLUMN {col} TEXT")
            except Exception:
                pass
        conn.execute(
            """UPDATE pc_workflows SET
               hw_cpu=?, hw_ram=?, hw_disk=?, hw_mac=?, hw_ip=?, hw_received_at=?
               WHERE id=?""",
            (data["cpu"], data["ram"], data["disk"], data["mac"], data["ip"], now, pw_id)
        )
    log.info("HW ricevuto per pw_id=%d — CPU=%s", pw_id, data["cpu"])
    return jsonify({"ok": True, "pw_id": pw_id}), 200
```

### Endpoint 2.2 — `POST /api/pc-workflows/<pw_id>/log`

Riceve righe di log del deploy in tempo reale.

```python
@app.route("/api/pc-workflows/<int:pw_id>/log", methods=["POST"])
@require_auth
def post_pw_log(pw_id):
    """Riceve righe di log dal client durante il deploy."""
    data = request.get_json(silent=True) or {}
    lines = data.get("lines", "")
    if not lines:
        return jsonify({"error": "Campo 'lines' mancante o vuoto"}), 400
    if isinstance(lines, list):
        lines = "\n".join(lines)
    now = datetime.datetime.utcnow().isoformat()
    with get_db() as conn:
        row = conn.execute("SELECT id FROM pc_workflows WHERE id=?", (pw_id,)).fetchone()
        if not row:
            return jsonify({"error": f"pc_workflow {pw_id} non trovato"}), 404
        # Migrazione lazy colonne log
        for col in ("last_log", "last_log_at"):
            try:
                conn.execute(f"ALTER TABLE pc_workflows ADD COLUMN {col} TEXT")
            except Exception:
                pass
        # Tronca a 8000 caratteri (ultimi log)
        truncated = lines[-8000:] if len(lines) > 8000 else lines
        conn.execute(
            "UPDATE pc_workflows SET last_log=?, last_log_at=? WHERE id=?",
            (truncated, now, pw_id)
        )
    return jsonify({"ok": True}), 200
```

### Endpoint 2.3 — `POST /api/pc-workflows/<pw_id>/screenshot`

Riceve screenshot finale in base64 al completamento del deploy.

```python
@app.route("/api/pc-workflows/<int:pw_id>/screenshot", methods=["POST"])
@require_auth
def post_pw_screenshot(pw_id):
    """Riceve screenshot finale in base64 (PNG/JPEG, max 2MB)."""
    data = request.get_json(silent=True) or {}
    b64 = data.get("screenshot_b64", "")
    if not b64:
        return jsonify({"error": "Campo 'screenshot_b64' mancante"}), 400
    # Valida dimensione (base64 di 2MB ≈ 2.7M caratteri)
    if len(b64) > 2_800_000:
        return jsonify({"error": "Screenshot troppo grande (max ~2MB)"}), 413
    now = datetime.datetime.utcnow().isoformat()
    with get_db() as conn:
        row = conn.execute("SELECT id FROM pc_workflows WHERE id=?", (pw_id,)).fetchone()
        if not row:
            return jsonify({"error": f"pc_workflow {pw_id} non trovato"}), 404
        for col in ("screenshot_b64", "screenshot_at"):
            try:
                conn.execute(f"ALTER TABLE pc_workflows ADD COLUMN {col} TEXT")
            except Exception:
                pass
        conn.execute(
            "UPDATE pc_workflows SET screenshot_b64=?, screenshot_at=? WHERE id=?",
            (b64, now, pw_id)
        )
    log.info("Screenshot ricevuto per pw_id=%d (%d chars)", pw_id, len(b64))
    return jsonify({"ok": True, "pw_id": pw_id}), 200
```

### Modifica 2.4 — `GET /api/pc-workflows/<pw_id>` (esistente)

Aggiungere i nuovi campi alla risposta JSON dell'endpoint esistente `get_pc_workflow`.

**Nel metodo `get_pc_workflow` (riga ~831), nel dizionario di risposta, aggiungere:**
```python
# Dopo il campo "steps": [...], aggiungere:
"hardware": {
    "cpu":  row["hw_cpu"]  or "",
    "ram":  row["hw_ram"]  or "",
    "disk": row["hw_disk"] or "",
    "mac":  row["hw_mac"]  or "",
    "ip":   row["hw_ip"]   or "",
} if row["hw_cpu"] else None,
"screenshot": row["screenshot_b64"] or None,
"last_log":   row["last_log"] or "",
```

> **Nota:** Poiché le colonne vengono aggiunte con migrazione lazy, usare `row.get("hw_cpu")` o gestire `KeyError` con `.get()`.

---

## PARTE 3 — AGGIORNAMENTO AGENTE WINDOWS (`NovaSCMAgent/`)

### 3.1 — Nuovo file `HardwareCollector.cs`

Creare il file `NovaSCMAgent/HardwareCollector.cs`:

```csharp
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NovaSCMAgent;

/// <summary>
/// Raccoglie informazioni hardware via WMI (Windows Management Instrumentation).
/// Funziona in WinPE se WinPE-WMI e WinPE-NetFx sono presenti.
/// </summary>
public static class HardwareCollector
{
    public record HwInfo(
        string Cpu, string Ram, string Disk,
        string Mac, string Ip);

    public static HwInfo Collect()
    {
        return new HwInfo(
            Cpu:  GetCpu(),
            Ram:  GetRam(),
            Disk: GetDisk(),
            Mac:  GetMac(),
            Ip:   GetIp()
        );
    }

    private static string GetCpu()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_Processor");
            foreach (var obj in searcher.Get())
                return CleanString(obj["Name"]?.ToString());
        }
        catch { }
        return "N/A";
    }

    private static string GetRam()
    {
        try
        {
            ulong totalBytes = 0;
            using var searcher = new ManagementObjectSearcher(
                "SELECT Capacity FROM Win32_PhysicalMemory");
            foreach (var obj in searcher.Get())
                totalBytes += Convert.ToUInt64(obj["Capacity"] ?? 0);
            if (totalBytes == 0) return "N/A";
            double gb = totalBytes / 1024.0 / 1024.0 / 1024.0;
            return $"{(int)Math.Round(gb)} GB";
        }
        catch { }
        return "N/A";
    }

    private static string GetDisk()
    {
        try
        {
            // Primo disco fisso (esclude USB/floppy)
            using var searcher = new ManagementObjectSearcher(
                "SELECT Model, Size FROM Win32_DiskDrive WHERE MediaType='Fixed hard disk media' OR MediaType='Unknown'");
            foreach (var obj in searcher.Get())
            {
                var model = CleanString(obj["Model"]?.ToString());
                var sizeBytes = Convert.ToUInt64(obj["Size"] ?? 0);
                if (sizeBytes == 0) return model;
                double gb = sizeBytes / 1024.0 / 1024.0 / 1024.0;
                return $"{model} {(int)Math.Round(gb)}GB";
            }
        }
        catch { }
        return "N/A";
    }

    private static string GetMac()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet
                    && nic.OperationalStatus == OperationalStatus.Up)
                {
                    var bytes = nic.GetPhysicalAddress().GetAddressBytes();
                    if (bytes.Length == 6)
                        return string.Join(":", bytes.Select(b => b.ToString("X2")));
                }
            }
        }
        catch { }
        return "N/A";
    }

    private static string GetIp()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet
                    && nic.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                            return addr.Address.ToString();
                    }
                }
            }
        }
        catch { }
        return "N/A";
    }

    private static string CleanString(string? s)
        => string.IsNullOrWhiteSpace(s) ? "N/A"
           : System.Text.RegularExpressions.Regex.Replace(s.Trim(), @"\s+", " ");
}
```

### 3.2 — Aggiornare `.csproj` per WMI

Nel file `NovaSCMAgent/NovaSCMAgent.csproj`, aggiungere il riferimento a System.Management:

```xml
<ItemGroup>
  <PackageReference Include="System.Management" Version="8.0.0" />
</ItemGroup>
```

### 3.3 — Aggiornare `ApiClient.cs` — nuovi metodi

Aggiungere alla classe `ApiClient` i tre nuovi metodi per inviare HW, log e screenshot:

```csharp
public async Task SendHardwareAsync(string apiUrl, int pwId,
    HardwareCollector.HwInfo hw, CancellationToken ct, string apiKey = "")
{
    try
    {
        var url  = $"{apiUrl.TrimEnd('/')}/api/pc-workflows/{pwId}/hardware";
        var body = JsonSerializer.Serialize(new
        {
            cpu  = hw.Cpu,
            ram  = hw.Ram,
            disk = hw.Disk,
            mac  = hw.Mac,
            ip   = hw.Ip
        });
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var req = BuildRequest(HttpMethod.Post, url, apiKey, content);
        await _http.SendAsync(req, ct);
        _log.LogInformation("HW info inviata per pw_id={PwId}", pwId);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        _log.LogWarning("SendHardware pw_id={PwId}: {Err}", pwId, ex.Message);
    }
}

public async Task SendLogAsync(string apiUrl, int pwId,
    string lines, CancellationToken ct, string apiKey = "")
{
    try
    {
        var url  = $"{apiUrl.TrimEnd('/')}/api/pc-workflows/{pwId}/log";
        var body = JsonSerializer.Serialize(new { lines });
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var req = BuildRequest(HttpMethod.Post, url, apiKey, content);
        await _http.SendAsync(req, ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        _log.LogWarning("SendLog pw_id={PwId}: {Err}", pwId, ex.Message);
    }
}

public async Task SendScreenshotAsync(string apiUrl, int pwId,
    string screenshotB64, CancellationToken ct, string apiKey = "")
{
    try
    {
        var url  = $"{apiUrl.TrimEnd('/')}/api/pc-workflows/{pwId}/screenshot";
        var body = JsonSerializer.Serialize(new { screenshot_b64 = screenshotB64 });
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var req = BuildRequest(HttpMethod.Post, url, apiKey, content);
        await _http.SendAsync(req, ct);
        _log.LogInformation("Screenshot inviato per pw_id={PwId} ({Len} chars)", pwId, screenshotB64.Length);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        _log.LogWarning("SendScreenshot pw_id={PwId}: {Err}", pwId, ex.Message);
    }
}
```

### 3.4 — Aggiornare `Worker.cs` — lancio DeployScreen + invio HW/log

Modificare il metodo `RunWorkflowAsync` in `Worker.cs` per:
- Avviare `NovaSCMDeployScreen.exe` all'inizio del deploy
- Raccogliere e inviare HW info
- Inviare output degli step come log

**Aggiungere in cima alla classe `Worker`:**
```csharp
private System.Diagnostics.Process? _deployScreen;
```

**All'inizio del metodo `RunWorkflowAsync`, dopo la riga `var resumeFrom = state?.ResumeStep ?? 0;`:**

```csharp
// ── Lancia DeployScreen se Windows e non ancora avviato ──
if (OperatingSystem.IsWindows() && _deployScreen == null)
{
    await Task.Run(() => LaunchDeployScreen(cfg, pwId, workflow), ct);
}

// ── Invia HW info (una sola volta per workflow) ──
if (OperatingSystem.IsWindows() && (state == null || state.HwSent == false))
{
    try
    {
        var hw = HardwareCollector.Collect();
        await _api.SendHardwareAsync(cfg.ApiUrl, pwId, hw, ct, cfg.ApiKey);
        AgentConfig.MarkHwSent(pwId);
        _log.LogInformation("HW info raccolte e inviate: CPU={Cpu}", hw.Cpu);
    }
    catch (Exception ex)
    {
        _log.LogWarning("HW collection fallita: {Err}", ex.Message);
    }
}
```

**Aggiungere il metodo `LaunchDeployScreen` alla classe `Worker`:**

```csharp
private void LaunchDeployScreen(AgentConfig cfg, int pwId, JsonObject workflow)
{
    try
    {
        // Cerca NovaSCMDeployScreen.exe nella stessa cartella dell'agente
        var agentDir = Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        var exePath = Path.Combine(agentDir, "NovaSCMDeployScreen.exe");

        if (!File.Exists(exePath))
        {
            _log.LogWarning("NovaSCMDeployScreen.exe non trovato in {Dir}", agentDir);
            return;
        }

        var wfName   = workflow["workflow_nome"]?.GetValue<string>() ?? "Deploy";
        var hostname = cfg.PcName;
        var domain   = cfg.Domain ?? "locale";

        var args = string.Join(" ", new[]
        {
            $"hostname={hostname}",
            $"domain={domain}",
            $"wf=\"{wfName}\"",
            $"server={cfg.ApiUrl}",
            $"key={cfg.ApiKey}",
            $"pw_id={pwId}"
        });

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName        = exePath,
            Arguments       = args,
            UseShellExecute = true,
            CreateNoWindow  = false,
            WindowStyle     = System.Diagnostics.ProcessWindowStyle.Maximized
        };

        _deployScreen = System.Diagnostics.Process.Start(psi);
        _log.LogInformation("DeployScreen avviato (PID={Pid})", _deployScreen?.Id);
    }
    catch (Exception ex)
    {
        _log.LogWarning("Impossibile avviare DeployScreen: {Err}", ex.Message);
    }
}
```

**Aggiornare il report dell'output degli step** in `RunWorkflowAsync`, dopo `ReportStepAsync`, per inviare il log:

```csharp
// Dopo await _api.ReportStepAsync(cfg.ApiUrl, cfg.PcName, stepId, "running", ..., ct, cfg.ApiKey);
// Aggiungere invio log:
if (!string.IsNullOrWhiteSpace(output) && pwId > 0)
    _ = _api.SendLogAsync(cfg.ApiUrl, pwId, output, ct, cfg.ApiKey);
```

**Al completamento del workflow** (quando `status == "done"`), aggiungere invio screenshot:

```csharp
// Dopo aver marcato il workflow come completed, aggiungere:
if (OperatingSystem.IsWindows())
{
    try
    {
        var ssB64 = CaptureScreenshot();
        if (!string.IsNullOrEmpty(ssB64))
            await _api.SendScreenshotAsync(cfg.ApiUrl, pwId, ssB64, ct, cfg.ApiKey);
    }
    catch (Exception ex)
    {
        _log.LogWarning("Screenshot capture fallita: {Err}", ex.Message);
    }
}
```

**Aggiungere il metodo `CaptureScreenshot` alla classe `Worker`:**

```csharp
/// <summary>
/// Cattura screenshot dello schermo principale e restituisce base64 JPEG.
/// Richiede .NET 6+ su Windows. Compresso a qualità 60 per contenere dimensioni.
/// </summary>
private static string CaptureScreenshot()
{
    try
    {
        // Usa System.Drawing.Common (aggiungere pacchetto se non presente)
        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
                     ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);

        using var bmp = new System.Drawing.Bitmap(bounds.Width, bounds.Height,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);

        using var ms = new MemoryStream();
        // Codifica JPEG qualità 60
        var encoder = System.Drawing.Imaging.ImageCodecInfo
            .GetImageEncoders()
            .First(e => e.MimeType == "image/jpeg");
        var encoderParams = new System.Drawing.Imaging.EncoderParameters(1);
        encoderParams.Param[0] = new System.Drawing.Imaging.EncoderParameter(
            System.Drawing.Imaging.Encoder.Quality, 60L);
        bmp.Save(ms, encoder, encoderParams);
        return Convert.ToBase64String(ms.ToArray());
    }
    catch
    {
        return "";
    }
}
```

### 3.5 — Aggiornare `AgentConfig.cs` — stato HwSent

Aggiungere al modello `AgentState` il campo `HwSent` e il metodo `MarkHwSent`:

```csharp
// Nel record/class AgentState, aggiungere:
public bool HwSent { get; set; } = false;

// Nel metodo statico MarkHwSent (da aggiungere alla classe AgentConfig):
public static void MarkHwSent(int pwId)
{
    var state = LoadState() ?? new AgentState();
    state.HwSent = true;
    SaveState(state);
}
```

### 3.6 — Aggiornare `.csproj` — dipendenze screenshot

Nel file `NovaSCMAgent/NovaSCMAgent.csproj`, aggiungere:

```xml
<ItemGroup>
  <PackageReference Include="System.Management" Version="8.0.0" />
  <PackageReference Include="System.Drawing.Common" Version="8.0.0" />
</ItemGroup>
```

---

## PARTE 4 — `AgentConfig.cs` — aggiunta campo `Domain`

Il DeployScreen richiede il campo `domain` nella configurazione agente. Aggiungere:

**Nel file `NovaSCMAgent/AgentConfig.cs`:**
```csharp
// Aggiungere proprietà Domain alla classe AgentConfig:
public string Domain { get; set; } = "locale";
```

**Nel parser CLI (se presente) o nel JSON di configurazione `agent.json`:**
```json
{
  "api_url":  "http://192.168.20.110:9091",
  "api_key":  "",
  "pc_name":  "WKS-ESEMPIO",
  "poll_sec": 60,
  "domain":   "polariscore.local"
}
```

---

## PARTE 5 — `agent/install-windows.ps1` — copia DeployScreen

Aggiornare lo script di installazione per scaricare e posizionare `NovaSCMDeployScreen.exe` accanto all'agente.

**Aggiungere dopo il blocco "Scarica NovaSCMAgent.exe" (dopo il passaggio 2):**

```powershell
# ── 2b. Scarica NovaSCMDeployScreen.exe ──────────────────────────────────────
$DeployScreenExe = "$AgentDir\NovaSCMDeployScreen.exe"
Log "Scarico NovaSCMDeployScreen.exe..."
try {
    Invoke-WebRequest -Uri "$ApiUrl/api/download/deploy-screen" `
        -OutFile $DeployScreenExe -UseBasicParsing
    Log "DeployScreen scaricato: $DeployScreenExe"
} catch {
    Log "ATTENZIONE: NovaSCMDeployScreen.exe non disponibile — deploy senza schermata grafica"
}
```

**Aggiungere anche il parametro `Domain` nella configurazione:**

```powershell
param(
    [string]$ApiUrl  = "http://YOUR-SERVER-IP:9091",
    [string]$ApiKey  = "",
    [string]$PcName  = $env:COMPUTERNAME,
    [string]$Domain  = "locale",        # ← NUOVO
    [int]$PollSec    = 60
)

# Nel blocco di scrittura config (Sezione 4):
@{
    api_url  = $ApiUrl
    api_key  = $ApiKey
    pc_name  = $PcName.ToUpper()
    poll_sec = [int]$PollSec
    domain   = $Domain              # ← NUOVO
} | ConvertTo-Json | Set-Content -Path $ConfigFile -Encoding UTF8
```

---

## PARTE 6 — SERVER: endpoint download `deploy-screen`

Aggiungere endpoint per scaricare `NovaSCMDeployScreen.exe` dal server (come già esiste per `NovaSCMAgent.exe`).

**In `server/api.py`**, trovare l'endpoint `/api/download/agent` ed aggiungere subito dopo:

```python
@app.route("/api/download/deploy-screen", methods=["GET"])
@require_auth
def download_deploy_screen():
    """Scarica NovaSCMDeployScreen.exe — client grafico per il deploy Windows."""
    dist_dir = os.path.join(os.path.dirname(__file__), "dist")
    exe_path = os.path.join(dist_dir, "NovaSCMDeployScreen.exe")
    if not os.path.isfile(exe_path):
        return jsonify({"error": "NovaSCMDeployScreen.exe non trovato in dist/"}), 404
    return send_from_directory(dist_dir, "NovaSCMDeployScreen.exe",
                               as_attachment=True,
                               mimetype="application/octet-stream")
```

> **Nota:** Il file compilato `NovaSCMDeployScreen.exe` (build `dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true`) va copiato in `server/dist/NovaSCMDeployScreen.exe`. Aggiungere `server/dist/` al `.gitignore` se non già presente.

---

## PARTE 7 — AUTOUNATTEND.XML — lancio in WinPE

Il server genera `autounattend.xml` via `GET /api/cr/by-name/<pc_name>/autounattend.xml`. Aggiornare il template per includere il lancio dell'agente (che a sua volta lancerà il DeployScreen).

**In `api.py`, nella funzione `get_autounattend`, nel blocco `<RunSynchronousCommands>`:**

Assicurarsi che sia presente il comando di avvio agente. Se non già presente, aggiungere:

```xml
<RunSynchronousCommand wcm:action="add">
    <Order>1</Order>
    <Path>cmd /c start /b C:\ProgramData\NovaSCM\NovaSCMAgent.exe</Path>
    <Description>Avvia NovaSCM Agent (lancia automaticamente DeployScreen)</Description>
    <WillReboot>Never</WillReboot>
</RunSynchronousCommand>
```

---

## RIEPILOGO FILE DA MODIFICARE

| File | Tipo modifica |
|------|--------------|
| `DeployScreen/MainWindow.xaml.cs` | Fix 3 bug (C-1, M-1, M-2) |
| `NovaSCMAgent/HardwareCollector.cs` | **NUOVO FILE** |
| `NovaSCMAgent/ApiClient.cs` | Aggiungere 3 metodi (HW, log, screenshot) |
| `NovaSCMAgent/Worker.cs` | Lancio DeployScreen + invio HW/log/screenshot |
| `NovaSCMAgent/AgentConfig.cs` | Aggiungere `Domain`, `HwSent` |
| `NovaSCMAgent/NovaSCMAgent.csproj` | Aggiungere `System.Management`, `System.Drawing.Common` |
| `agent/install-windows.ps1` | Download DeployScreen + param Domain |
| `server/api.py` | 3 nuovi endpoint HW/log/screenshot + download deploy-screen |

---

## ORDINE DI ESECUZIONE CONSIGLIATO

1. Fix bug WPF (`MainWindow.xaml.cs`) — da fare prima, test in demo mode
2. Nuovi endpoint server (`api.py`) — aggiungerli e testare con `curl`
3. `HardwareCollector.cs` + aggiornamento `.csproj`
4. Aggiornamento `ApiClient.cs` + `Worker.cs` + `AgentConfig.cs`
5. Aggiornamento `install-windows.ps1`
6. Endpoint download `deploy-screen` + build/copia exe in `server/dist/`
7. Test end-to-end in demo mode: `NovaSCMDeployScreen.exe demo=1`
8. Test end-to-end con agente reale

---

## TEST MANUAL — verifica endpoint server

```bash
# Test HW endpoint
curl -X POST http://192.168.20.110:9091/api/pc-workflows/1/hardware \
  -H "X-Api-Key: TUA_CHIAVE" \
  -H "Content-Type: application/json" \
  -d '{"cpu":"Intel i5-12400","ram":"16 GB","disk":"Samsung SSD 500GB","mac":"AA:BB:CC:DD:EE:FF","ip":"192.168.10.42"}'

# Test Log endpoint
curl -X POST http://192.168.20.110:9091/api/pc-workflows/1/log \
  -H "X-Api-Key: TUA_CHIAVE" \
  -H "Content-Type: application/json" \
  -d '{"lines":"[INFO] Avvio partizionamento\n[OK] Completato"}'

# Verifica risposta GET con nuovi campi
curl http://192.168.20.110:9091/api/pc-workflows/1 \
  -H "X-Api-Key: TUA_CHIAVE" | python3 -m json.tool
```

---

*Report generato il 2026-03-10 — NovaSCM v1.9.0*
