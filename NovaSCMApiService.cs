using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PolarisManager;

// ARCH-01: servizio centralizzato per le chiamate HTTP verso l'API NovaSCM
// Raccoglie tutti i metodi HTTP in un unico posto, semplificando la gestione
// di headers, timeout, serializzazione e cache.
public class NovaSCMApiService(string baseUrl, string apiKey = "", ApiCache? cache = null)
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly ApiCache _cache = cache ?? new ApiCache();
    private string Base => baseUrl.TrimEnd('/');

    public bool IsConfigured => !string.IsNullOrWhiteSpace(baseUrl);

    private HttpRequestMessage BuildGet(string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(apiKey))
            req.Headers.Add("X-Api-Key", apiKey);
        return req;
    }

    private HttpRequestMessage BuildPut(string url, HttpContent content)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };
        if (!string.IsNullOrEmpty(apiKey))
            req.Headers.Add("X-Api-Key", apiKey);
        return req;
    }

    private HttpRequestMessage BuildDelete(string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, url);
        if (!string.IsNullOrEmpty(apiKey))
            req.Headers.Add("X-Api-Key", apiKey);
        return req;
    }

    // ── CR (Change Request) ───────────────────────────────────────────────────
    public async Task<string> GetCrListJsonAsync(bool forceRefresh = false)
    {
        var url = $"{Base}";
        if (!forceRefresh && _cache.TryGet(url, out var cached)) return cached;
        var resp = await _http.SendAsync(BuildGet(url));
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        _cache.Set(url, json, TimeSpan.FromSeconds(30));
        return json;
    }

    public async Task SetCrStatusAsync(int id, string status)
    {
        var body = JsonSerializer.Serialize(new { status });
        await _http.SendAsync(BuildPut($"{Base}/{id}/status",
            new StringContent(body, Encoding.UTF8, "application/json")));
        _cache.Invalidate(Base);
    }

    public async Task DeleteCrAsync(int id)
    {
        await _http.SendAsync(BuildDelete($"{Base}/{id}"));
        _cache.Invalidate(Base);
    }

    public async Task<string> GetCrXmlAsync(string pcName)
    {
        var resp = await _http.SendAsync(BuildGet($"{Base}/by-name/{pcName}/autounattend.xml"));
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync();
    }

    // ── Workflow ──────────────────────────────────────────────────────────────
    private string WfBase => baseUrl.TrimEnd('/').Replace("/api/cr", "");

    public async Task<string> GetWorkflowsJsonAsync()
    {
        var resp = await _http.SendAsync(BuildGet($"{WfBase}/api/workflows"));
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync();
    }

    public async Task<string> GetWorkflowDetailJsonAsync(int wfId)
    {
        var resp = await _http.SendAsync(BuildGet($"{WfBase}/api/workflows/{wfId}"));
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync();
    }

    public async Task<string> GetPcWorkflowsJsonAsync()
    {
        var resp = await _http.SendAsync(BuildGet($"{WfBase}/api/pc-workflows"));
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync();
    }

    // ── Agent / Check-in ─────────────────────────────────────────────────────
    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            var resp = await _http.SendAsync(BuildGet($"{WfBase}/health"));
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
