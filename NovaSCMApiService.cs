using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PolarisManager;

// ARCH-01: servizio centralizzato per le chiamate HTTP verso l'API NovaSCM
// Raccoglie tutti i metodi HTTP in un unico posto, semplificando la gestione
// di headers, timeout, serializzazione e cache.
public class NovaSCMApiService(string baseUrl, ApiCache? cache = null)
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly ApiCache _cache = cache ?? new ApiCache();
    private string Base => baseUrl.TrimEnd('/');

    public bool IsConfigured => !string.IsNullOrWhiteSpace(baseUrl);

    // ── CR (Change Request) ───────────────────────────────────────────────────
    public async Task<string> GetCrListJsonAsync(bool forceRefresh = false)
    {
        var url = $"{Base}";
        if (!forceRefresh && _cache.TryGet(url, out var cached)) return cached;
        var json = await _http.GetStringAsync(url);
        _cache.Set(url, json, TimeSpan.FromSeconds(30));
        return json;
    }

    public async Task SetCrStatusAsync(int id, string status)
    {
        var body = JsonSerializer.Serialize(new { status });
        await _http.PutAsync($"{Base}/{id}/status",
            new StringContent(body, Encoding.UTF8, "application/json"));
        _cache.Invalidate(Base);
    }

    public async Task DeleteCrAsync(int id)
    {
        await _http.DeleteAsync($"{Base}/{id}");
        _cache.Invalidate(Base);
    }

    public async Task<string> GetCrXmlAsync(string pcName)
        => await _http.GetStringAsync($"{Base}/by-name/{pcName}/autounattend.xml");

    // ── Workflow ──────────────────────────────────────────────────────────────
    private string WfBase => baseUrl.TrimEnd('/').Replace("/api/cr", "");

    public async Task<string> GetWorkflowsJsonAsync()
        => await _http.GetStringAsync($"{WfBase}/api/workflows");

    public async Task<string> GetWorkflowDetailJsonAsync(int wfId)
        => await _http.GetStringAsync($"{WfBase}/api/workflows/{wfId}");

    public async Task<string> GetPcWorkflowsJsonAsync()
        => await _http.GetStringAsync($"{WfBase}/api/pc-workflows");

    // ── Agent / Check-in ─────────────────────────────────────────────────────
    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            var resp = await _http.GetAsync($"{WfBase}/health");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
