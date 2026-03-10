# NovaSCM — Fix Screenshot al Completamento Deploy
## Report per Claude Code — Patch v1.9.1

**Data:** 2026-03-10  
**Repository:** https://github.com/ClaudioBecchis/NovaSCM  
**Scope:** Implementare `CaptureScreenshot` in `Worker.cs` e inviarlo al server al termine del deploy

---

## CONTESTO

Dal controllo su GitHub risulta che il commit `ae0aac30ca` ha implementato correttamente:
- ✅ `LaunchDeployScreen` in `Worker.cs`
- ✅ `HardwareCollector.Collect()` + `SendHardwareAsync` in `Worker.cs`
- ✅ `SendLogAsync` per ogni step
- ✅ `SendScreenshotAsync` in `ApiClient.cs` (metodo pronto ma mai chiamato)

**Manca solo:**
- ❌ Il metodo `CaptureScreenshot()` in `Worker.cs`
- ❌ La chiamata a `SendScreenshotAsync` al completamento del workflow
- ❌ Il pacchetto `System.Drawing.Common` nel `.csproj`

---

## MODIFICA 1 — `NovaSCMAgent/NovaSCMAgent.csproj`

Aggiungere il pacchetto `System.Drawing.Common` nell'`ItemGroup` esistente.

**File attuale:**
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" Version="9.0.0" />
  <PackageReference Include="Microsoft.Extensions.Hosting.Systemd"         Version="9.0.0" />
</ItemGroup>
```

**File aggiornato:**
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" Version="9.0.0" />
  <PackageReference Include="Microsoft.Extensions.Hosting.Systemd"         Version="9.0.0" />
  <PackageReference Include="System.Drawing.Common"                         Version="9.0.0"
                    Condition="'$(RuntimeIdentifier)' == 'win-x64' Or '$(OS)' == 'Windows_NT'" />
</ItemGroup>
```

> **Nota:** Il `Condition` limita la dipendenza a Windows. Su Linux `System.Drawing.Common` richiede `libgdiplus` e non è necessario — lo screenshot è una feature Windows-only.

---

## MODIFICA 2 — `NovaSCMAgent/Worker.cs`

### 2.1 — Aggiungere using in cima al file

Il file attualmente inizia con:
```csharp
using System.Text.Json.Nodes;
```

Aggiungere:
```csharp
using System.Text.Json.Nodes;
using System.Runtime.Versioning;
```

### 2.2 — Aggiungere metodo `CaptureScreenshot`

Aggiungere il metodo `CaptureScreenshot` alla classe `Worker`, **dopo** il metodo `LaunchDeployScreen` e **prima** di `RunWorkflowAsync`.

**Punto di inserimento esatto — dopo questa riga:**
```csharp
        }
    }

    private async Task RunWorkflowAsync(AgentConfig cfg, JsonObject workflow, CancellationToken ct)
```

**Inserire il metodo completo:**
```csharp
    /// <summary>
    /// Cattura screenshot del desktop principale e restituisce base64 JPEG.
    /// Solo Windows — restituisce stringa vuota su altri OS o in caso di errore.
    /// Qualità JPEG 60% per contenere la dimensione (tipicamente 100-300KB in base64).
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static string CaptureScreenshotWindows()
    {
        try
        {
            // Recupera dimensioni schermo primario via GetSystemMetrics (Win32)
            // SM_CXSCREEN=0, SM_CYSCREEN=1
            int w = GetSystemMetrics(0);
            int h = GetSystemMetrics(1);
            if (w <= 0 || h <= 0) { w = 1920; h = 1080; }

            using var bmp = new System.Drawing.Bitmap(w, h,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = System.Drawing.Graphics.FromImage(bmp);
            g.CopyFromScreen(0, 0, 0, 0, new System.Drawing.Size(w, h),
                System.Drawing.CopyPixelOperation.SourceCopy);

            using var ms = new MemoryStream();
            var encoder = System.Drawing.Imaging.ImageCodecInfo
                .GetImageEncoders()
                .FirstOrDefault(e => e.MimeType == "image/jpeg");

            if (encoder == null)
            {
                // Fallback PNG se encoder JPEG non trovato
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            }
            else
            {
                var encoderParams = new System.Drawing.Imaging.EncoderParameters(1);
                encoderParams.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                    System.Drawing.Imaging.Encoder.Quality, 60L);
                bmp.Save(ms, encoder, encoderParams);
            }

            return Convert.ToBase64String(ms.ToArray());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WARN] CaptureScreenshot: {ex.Message}");
            return "";
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private static string CaptureScreenshot()
    {
        if (!OperatingSystem.IsWindows()) return "";
        return CaptureScreenshotWindows();
    }

```

### 2.3 — Chiamare `CaptureScreenshot` + `SendScreenshotAsync` al completamento

Nel metodo `RunWorkflowAsync`, **sostituire** le ultime due righe:

```csharp
        AgentConfig.ClearState();
        _log.LogInformation("Workflow completato");
    }
```

**Con:**
```csharp
        AgentConfig.ClearState();
        _log.LogInformation("Workflow completato");

        // ── Screenshot finale ──────────────────────────────────────────────
        // Piccolo delay per dare tempo al DeployScreen di mostrare l'overlay
        await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        try
        {
            var ssB64 = CaptureScreenshot();
            if (!string.IsNullOrEmpty(ssB64))
            {
                await _api.SendScreenshotAsync(cfg.ApiUrl, pwId, ssB64, ct, cfg.ApiKey);
                _log.LogInformation("Screenshot inviato ({Kb} KB)", ssB64.Length * 3 / 4 / 1024);
            }
            else
            {
                _log.LogDebug("Screenshot non disponibile (OS non Windows o errore capture)");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning("SendScreenshot fallito: {Err}", ex.Message);
        }
    }
```

---

## VERIFICA FINALE

Dopo le modifiche, il metodo `RunWorkflowAsync` deve seguire questo flusso:

```
RunWorkflowAsync()
  ├── LaunchDeployScreen()          ✅ già presente
  ├── HardwareCollector.Collect()   ✅ già presente
  │   └── SendHardwareAsync()       ✅ già presente
  ├── foreach step:
  │   ├── ReportStepAsync("running") ✅ già presente
  │   ├── ExecuteAsync()             ✅ già presente
  │   ├── ReportStepAsync(status)    ✅ già presente
  │   └── SendLogAsync(output)       ✅ già presente
  ├── AgentConfig.ClearState()      ✅ già presente
  ├── Task.Delay(2s)                ← NUOVO
  ├── CaptureScreenshot()           ← NUOVO
  └── SendScreenshotAsync()         ← NUOVO (ApiClient già pronto)
```

---

## TEST

```powershell
# 1. Build dopo le modifiche
cd NovaSCMAgent
dotnet build -c Release

# 2. Verifica nessun errore di compilazione su Windows
# Atteso: Build succeeded, 0 Error(s)

# 3. Test manuale — lancia in demo + aspetta completamento
# Lo screenshot verrà inviato al server al termine
# Verificare nella UI NovaSCM che l'overlay di completamento mostri l'anteprima
```

---

*Report generato il 2026-03-10 — NovaSCM v1.9.1 — patch CaptureScreenshot*
