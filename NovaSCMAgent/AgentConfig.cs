using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovaSCMAgent;

public class AgentConfig
{
    private static readonly bool IsWindows = OperatingSystem.IsWindows();

    private static string ConfigPath => IsWindows
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NovaSCM", "agent.json")
        : "/etc/novascm/agent.json";

    public static string StatePath => IsWindows
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NovaSCM", "state.json")
        : "/var/lib/novascm/state.json";

    public static string LogDir => IsWindows
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NovaSCM", "logs")
        : "/var/log/novascm";

    // BUG: i messaggi di errore erano scritti solo su Console.Error, ma l'agent
    // gira come servizio Windows/systemd senza console attaccata — finivano nel
    // vuoto, vanificando il valore diagnostico dei try/catch. Ora anche su un
    // file di log persistente (bootstrap.log) accanto agli altri log.
    private static void LogLine(string msg)
    {
        Console.Error.WriteLine(msg);
        try
        {
            Directory.CreateDirectory(LogDir);
            File.AppendAllText(Path.Combine(LogDir, "bootstrap.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}");
        }
        catch { /* logging best-effort */ }
    }

    // BUG CRITICO: PropertyNameCaseInsensitive confronta le stringhe ignorando
    // solo il case, NON converte snake_case↔PascalCase — "api_url" non fa
    // match con "ApiUrl". OGNI agent.json generato dagli script server-side
    // (server/api.py: download_agent_installer_ps1/.sh, il flusso wimboot)
    // scrive chiavi snake_case (api_url, api_key, pc_name, poll_sec) perché
    // è la convenzione dell'agent Python — ma venivano SEMPRE ignorate qui,
    // lasciando ApiUrl/ApiKey sui valori di default. L'agent C# installato
    // come servizio Windows non riusciva mai a configurarsi da solo,
    // indipendentemente dal percorso di deploy (DISM, wimboot, o download
    // manuale dell'installer). Verificato empiricamente prima del fix.
    [JsonPropertyName("api_url")]
    public string ApiUrl  { get; set; } = "http://YOUR-NOVASCM-SERVER:9091";
    [JsonPropertyName("api_key")]
    public string ApiKey  { get; set; } = "";
    [JsonPropertyName("pc_name")]
    public string PcName  { get; set; } = Environment.MachineName.ToUpperInvariant();
    [JsonPropertyName("domain")]
    public string Domain  { get; set; } = "WORKGROUP";
    [JsonPropertyName("poll_sec")]
    public int    PollSec { get; set; } = 60;

    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static AgentConfig Load()
    {
        // Crea directory se mancanti
        foreach (var dir in new[] {
            Path.GetDirectoryName(ConfigPath)!,
            Path.GetDirectoryName(StatePath)!,
            LogDir
        }) Directory.CreateDirectory(dir);

        if (!File.Exists(ConfigPath))
        {
            var def = new AgentConfig();
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(def, _opts));
            return def;
        }

        try
        {
            var cfg = JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(ConfigPath), _opts)
                      ?? new AgentConfig();
            if (string.IsNullOrWhiteSpace(cfg.PcName))
                cfg.PcName = Environment.MachineName.ToUpperInvariant();
            if (cfg.ApiUrl.Contains("YOUR-NOVASCM-SERVER"))
                LogLine($"[ATTENZIONE] agent.json non configurato! Modifica ApiUrl in: {ConfigPath}");
            return cfg;
        }
        catch (Exception ex)
        {
            LogLine($"[NovaSCM] Errore lettura config {ConfigPath}: {ex.Message}");
            return new AgentConfig();
        }
    }

    // ── Stato persistente (per resume dopo reboot) ────────────────────────────
    // Phase: "rebooting" = reboot schedulato, "resumed" = reboot completato e step reboot confermato
    // BUG: ResumeStep (soglia numerica su step_id, PK globale non garantita
    // coerente con l'ordine di esecuzione) è mantenuto solo per log/diagnostica
    // — la logica di skip-on-resume usa CompletedStepIds (l'insieme reale
    // degli step processati), corretto anche se il workflow viene riordinato
    // o modificato tra il checkpoint e il resume.
    public record AgentState(int PwId, int ResumeStep, bool HwSent = false, string Phase = "rebooting", int[]? CompletedStepIds = null);

    public static AgentState? LoadState()
    {
        if (!File.Exists(StatePath)) return null;
        try
        {
            return JsonSerializer.Deserialize<AgentState>(File.ReadAllText(StatePath), _opts);
        }
        catch (Exception ex)
        {
            LogLine($"[NovaSCM] State file corrotto: {ex.Message} — reset");
            try { File.Delete(StatePath); } catch { }
            return null;
        }
    }

    public static void SaveState(AgentState state)
    {
        var json = JsonSerializer.Serialize(state, _opts);
        // Atomic write: scrivi su file temp, poi rinomina (previene corruzione su crash)
        var tmpPath = StatePath + ".tmp";
        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, StatePath, overwrite: true);
    }

    public static void MarkHwSent(int pwId, int resumeStep, int[]? completedStepIds = null)
        => SaveState(new AgentState(pwId, resumeStep, HwSent: true, Phase: "resumed", CompletedStepIds: completedStepIds));

    public static void MarkResumed(AgentState state)
        => SaveState(state with { Phase = "resumed" });

    public static void ClearState()
    {
        try { if (File.Exists(StatePath)) File.Delete(StatePath); } catch { }
    }
}
