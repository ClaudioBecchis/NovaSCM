using System.Text.Json.Nodes;

namespace NovaSCMAgent;

public class Worker : BackgroundService
{
    // BUG-06: valuta condizione semplice prima di eseguire lo step
    private static bool EvaluateCondition(string? condizione)
    {
        if (string.IsNullOrWhiteSpace(condizione)) return true;
        var cond = condizione.Trim().ToLowerInvariant();
        if (cond == "windows") return OperatingSystem.IsWindows();
        if (cond == "linux")   return OperatingSystem.IsLinux();
        if (cond.StartsWith("os="))
            return (OperatingSystem.IsWindows() ? "windows" : "linux") == cond[3..].Trim();
        if (cond.StartsWith("hostname="))
            return Environment.MachineName.Equals(cond[9..].Trim(), StringComparison.OrdinalIgnoreCase);
        return true;  // condizione sconosciuta → esegui comunque
    }

    private readonly ILogger<Worker> _log;
    private readonly ApiClient       _api;
    private readonly StepExecutor    _exec;

    public Worker(ILogger<Worker> log, ApiClient api, StepExecutor exec)
    {
        _log  = log;
        _api  = api;
        _exec = exec;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        _log.LogInformation("NovaSCM Agent v{Version} avviato — OS: {Os}", version, Environment.OSVersion);

        while (!ct.IsCancellationRequested)
        {
            var cfg = AgentConfig.Load();
            _log.LogDebug("Polling — PC={Pc} API={Url}", cfg.PcName, cfg.ApiUrl);

            try
            {
                var wf = await _api.GetWorkflowAsync(cfg.ApiUrl, cfg.PcName, ct, cfg.ApiKey);

                if (wf != null && wf.ContainsKey("workflow_nome") && wf["workflow_nome"] != null)
                {
                    var nome = wf["workflow_nome"]?.GetValue<string>() ?? "?";
                    _log.LogInformation("Workflow trovato: '{Nome}'", nome);
                    await RunWorkflowAsync(cfg, wf, ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "Errore nel loop principale");
            }

            await Task.Delay(TimeSpan.FromSeconds(cfg.PollSec), ct);
        }
    }

    private static void LaunchDeployScreen(AgentConfig cfg, int pwId, string wfNome)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var exeDir   = AppContext.BaseDirectory;
            var exePaths = new[]
            {
                Path.Combine(exeDir, "NovaSCMDeployScreen.exe"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "NovaSCM", "NovaSCMDeployScreen.exe"),
            };
            var exePath = exePaths.FirstOrDefault(File.Exists);
            if (exePath == null) return;

            var args = $"hostname={cfg.PcName} domain={cfg.Domain} pw_id={pwId} " +
                       $"server={cfg.ApiUrl} key={cfg.ApiKey} wf={Uri.EscapeDataString(wfNome)}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = exePath,
                Arguments       = args,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            // Non critico: il deploy continua anche senza schermata
            Console.Error.WriteLine($"[WARN] LaunchDeployScreen: {ex.Message}");
        }
    }

    private async Task RunWorkflowAsync(AgentConfig cfg, JsonObject workflow, CancellationToken ct)
    {
        var pwId   = workflow["id"]?.GetValue<int>()          ?? 0;
        var wfNome = workflow["workflow_nome"]?.GetValue<string>() ?? "Deploy";
        var steps  = workflow["steps"]?.AsArray() ?? [];

        // Resume dopo reboot: salta step già completati
        var state      = AgentConfig.LoadState();
        // BUG-8: verifica che lo stato salvato appartenga a questo workflow
        if (state != null && state.PwId != pwId)
        {
            _log.LogWarning("Stato resume pw_id={Saved} != workflow corrente pw_id={Current} — reset", state.PwId, pwId);
            AgentConfig.ClearState();
            state = null;
        }
        var resumeFrom = state?.ResumeStep ?? 0;
        if (resumeFrom > 0)
            _log.LogInformation("Resume dopo reboot — da step_id={StepId}", resumeFrom);

        // Lancia DeployScreen sul PC in deploy (solo primo avvio, non su resume)
        if (resumeFrom == 0)
            LaunchDeployScreen(cfg, pwId, wfNome);

        // Invia hardware info se non già inviato
        if (!(state?.HwSent ?? false))
        {
            try
            {
                var hw = HardwareCollector.Collect();
                await _api.SendHardwareAsync(cfg.ApiUrl, pwId, hw, ct, cfg.ApiKey);
                AgentConfig.MarkHwSent(pwId, resumeFrom);
                _log.LogInformation("Hardware inviato: CPU={Cpu} RAM={Ram}", hw.Cpu, hw.Ram);
            }
            catch (Exception ex) { _log.LogWarning("HW collect: {Err}", ex.Message); }
        }

        foreach (var node in steps)
        {
            if (ct.IsCancellationRequested) break;
            if (node is not JsonObject step) continue;

            var stepId  = step["step_id"]?.GetValue<int>() ?? 0;
            var ordine  = step["ordine"]?.GetValue<int>()  ?? 0;
            var nome    = step["nome"]?.GetValue<string>()  ?? "?";
            var suErr   = step["su_errore"]?.GetValue<string>() ?? "stop";

            // Salta step già eseguiti (resume)
            if (resumeFrom > 0 && stepId <= resumeFrom)
            {
                _log.LogInformation("[{N}] {Nome} — già completato, salto", ordine, nome);
                continue;
            }

            // BUG-06: valuta condizione prima di eseguire
            var condizione = step["condizione"]?.GetValue<string>() ?? "";
            if (!EvaluateCondition(condizione))
            {
                _log.LogInformation("[{N}] {Nome} — SKIP condizione: {Cond}", ordine, nome, condizione);
                await _api.ReportStepAsync(cfg.ApiUrl, cfg.PcName, stepId, "skipped",
                    $"Condizione non soddisfatta: {condizione}", ct, cfg.ApiKey);
                continue;
            }

            _log.LogInformation("[{N}/{Tot}] {Nome}", ordine, steps.Count, nome);

            // Segnala running
            await _api.ReportStepAsync(cfg.ApiUrl, cfg.PcName, stepId, "running", "", ct, cfg.ApiKey);

            // Esegui
            var result = await _exec.ExecuteAsync(step, ct);

            if (result.Ok is null)
            {
                // Skipped
                _log.LogInformation("  → SKIPPED");
                await _api.ReportStepAsync(cfg.ApiUrl, cfg.PcName, stepId, "skipped", result.Output, ct, cfg.ApiKey);
                continue;
            }

            var status = result.Ok.Value ? "done" : "error";
            _log.LogInformation("  → {Status}: {Out}", status.ToUpperInvariant(),
                result.Output.Length > 200 ? result.Output[..200] + "..." : result.Output);

            await _api.ReportStepAsync(cfg.ApiUrl, cfg.PcName, stepId, status, result.Output, ct, cfg.ApiKey);
            if (!string.IsNullOrWhiteSpace(result.Output))
                await _api.SendLogAsync(cfg.ApiUrl, pwId, result.Output, ct, cfg.ApiKey);

            if (!result.Ok.Value && suErr == "stop")
            {
                _log.LogError("Step fallito con su_errore=stop — workflow interrotto");
                AgentConfig.ClearState();
                return;
            }

            // Reboot: salva stato e lascia che il sistema si riavvii
            if (step["tipo"]?.GetValue<string>() == "reboot" && result.Ok.Value)
            {
                AgentConfig.SaveState(new AgentConfig.AgentState(pwId, stepId));
                _log.LogInformation("Stato salvato per resume dopo reboot");
                return;
            }
        }

        AgentConfig.ClearState();
        _log.LogInformation("Workflow completato");
    }
}
