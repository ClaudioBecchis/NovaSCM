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

    public string ApiUrl  { get; set; } = "http://192.168.20.110:9091";
    public string PcName  { get; set; } = Environment.MachineName.ToUpperInvariant();
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
            return cfg;
        }
        catch
        {
            return new AgentConfig();
        }
    }

    // ── Stato persistente (per resume dopo reboot) ────────────────────────────
    public record AgentState(int PwId, int ResumeStep);

    public static AgentState? LoadState()
    {
        if (!File.Exists(StatePath)) return null;
        try { return JsonSerializer.Deserialize<AgentState>(File.ReadAllText(StatePath), _opts); }
        catch { return null; }
    }

    public static void SaveState(AgentState state)
        => File.WriteAllText(StatePath, JsonSerializer.Serialize(state, _opts));

    public static void ClearState()
    {
        if (File.Exists(StatePath)) File.Delete(StatePath);
    }
}
