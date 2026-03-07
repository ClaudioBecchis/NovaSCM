using System.Text.Json.Nodes;

namespace NovaSCMAgent;

public class Worker : BackgroundService
{
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
        _log.LogInformation("NovaSCM Agent v1.0.0 avviato — OS: {Os}", Environment.OSVersion);

        while (!ct.IsCancellationRequested)
        {
            var cfg = AgentConfig.Load();
            _log.LogDebug("Polling — PC={Pc} API={Url}", cfg.PcName, cfg.ApiUrl);

            try
            {
                var wf = await _api.GetWorkflowAsync(cfg.ApiUrl, cfg.PcName, ct);

                if (wf != null && wf["error"] is null)
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

    private async Task RunWorkflowAsync(AgentConfig cfg, JsonObject workflow, CancellationToken ct)
    {
        var pwId  = workflow["id"]?.GetValue<int>() ?? 0;
        var steps = workflow["steps"]?.AsArray() ?? [];

        // Resume dopo reboot: salta step già completati
        var state       = AgentConfig.LoadState();
        var resumeFrom  = state?.ResumeStep ?? 0;
        if (resumeFrom > 0)
            _log.LogInformation("Resume dopo reboot — da step_id={StepId}", resumeFrom);

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

            _log.LogInformation("[{N}/{Tot}] {Nome}", ordine, steps.Count, nome);

            // Segnala running
            await _api.ReportStepAsync(cfg.ApiUrl, cfg.PcName, stepId, "running", "", ct);

            // Esegui
            var result = await _exec.ExecuteAsync(step, ct);

            if (result.Ok is null)
            {
                // Skipped
                _log.LogInformation("  → SKIPPED");
                await _api.ReportStepAsync(cfg.ApiUrl, cfg.PcName, stepId, "skipped", result.Output, ct);
                continue;
            }

            var status = result.Ok.Value ? "done" : "error";
            _log.LogInformation("  → {Status}: {Out}", status.ToUpperInvariant(),
                result.Output.Length > 200 ? result.Output[..200] + "..." : result.Output);

            await _api.ReportStepAsync(cfg.ApiUrl, cfg.PcName, stepId, status, result.Output, ct);

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
