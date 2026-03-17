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

    public string ApiUrl  { get; set; } = "http://YOUR-NOVASCM-SERVER:9091";
    public string ApiKey  { get; set; } = "";
    public string PcName  { get; set; } = Environment.MachineName.ToUpperInvariant();
    public string Domain  { get; set; } = "WORKGROUP";
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
                Console.Error.WriteLine($"[ATTENZIONE] agent.json non configurato! Modifica ApiUrl in: {ConfigPath}");
            return cfg;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[NovaSCM] Errore lettura config {ConfigPath}: {ex.Message}");
            return new AgentConfig();
        }
    }

    // ── Stato persistente (per resume dopo reboot) ────────────────────────────
    public record AgentState(int PwId, int ResumeStep, bool HwSent = false);

    public static AgentState? LoadState()
    {
        if (!File.Exists(StatePath)) return null;
        try { return JsonSerializer.Deserialize<AgentState>(File.ReadAllText(StatePath), _opts); }
        catch { return null; }
    }

    public static void SaveState(AgentState state)
        => File.WriteAllText(StatePath, JsonSerializer.Serialize(state, _opts));

    public static void MarkHwSent(int pwId, int resumeStep)
        => SaveState(new AgentState(pwId, resumeStep, HwSent: true));

    public static void ClearState()
    {
        if (File.Exists(StatePath)) File.Delete(StatePath);
    }
}
