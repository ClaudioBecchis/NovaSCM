// NovaSCM v1.6.0 — © 2026 Claudio Becchis — https://github.com/ClaudioBecchis/NovaSCM
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PolarisManager;

// ARCH-01: servizio centralizzato per le chiamate HTTP verso l'API NovaSCM.
// Usa un HttpClient statico condiviso — nessun socket exhaustion da new HttpClient() inline.
public class NovaSCMApiService(string baseUrl, string apiKey = "", ApiCache? cache = null)
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly ApiCache _cache = cache ?? new ApiCache();
    private string CrBase  => baseUrl.TrimEnd('/');                          // include /api/cr
    // BUG-9: usa Uri per estrarre l'origine senza dipendere dalla stringa "/api/cr"
    private string ApiBase
    {
        get
        {
            try { var u = new Uri(baseUrl.TrimEnd('/')); return $"{u.Scheme}://{u.Authority}"; }
            catch { return baseUrl.TrimEnd('/').Replace("/api/cr", ""); }
        }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(baseUrl);

    // ── Autenticazione ────────────────────────────────────────────────────────
    private void AddAuth(HttpRequestMessage req)
    {
        if (!string.IsNullOrEmpty(apiKey))
            req.Headers.Add("X-Api-Key", apiKey);
    }

    private HttpRequestMessage Req(HttpMethod method, string url, HttpContent? body = null)
    {
        var req = new HttpRequestMessage(method, url) { Content = body };
        AddAuth(req);
        return req;
    }

    private async Task<string> SendAsync(HttpMethod method, string url, HttpContent? body = null)
    {
        using var resp = await _http.SendAsync(Req(method, url, body));
        // BUG-10: EnsureSuccessStatusCode lancia HttpRequestException — includiamo body dell'errore
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync();
            var snippet = err.Length > 300 ? err[..300] : err;
            throw new HttpRequestException(
                $"{(int)resp.StatusCode} {resp.ReasonPhrase}: {snippet}",
                null, resp.StatusCode);
        }
        return await resp.Content.ReadAsStringAsync();
    }

    private static StringContent Json(object data)
        => new(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");

    // ── CR (Change Request) ───────────────────────────────────────────────────

    public async Task<string> GetCrListJsonAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _cache.TryGet(CrBase, out var cached)) return cached;
        var json = await SendAsync(HttpMethod.Get, CrBase);
        _cache.Set(CrBase, json, TimeSpan.FromSeconds(30));
        return json;
    }

    public async Task<string> PostCrAsync(object data)
    {
        var json = await SendAsync(HttpMethod.Post, CrBase, Json(data));
        _cache.Invalidate(CrBase);
        return json;
    }

    public async Task SetCrStatusAsync(int id, string status)
    {
        await SendAsync(HttpMethod.Put, $"{CrBase}/{id}/status", Json(new { status }));
        _cache.Invalidate(CrBase);
    }

    public async Task DeleteCrAsync(int id)
    {
        await SendAsync(HttpMethod.Delete, $"{CrBase}/{id}");
        _cache.Invalidate(CrBase);
    }

    public async Task<string> GetCrJsonAsync(int id)
        => await SendAsync(HttpMethod.Get, $"{CrBase}/{id}");

    public async Task<string> GetCrXmlAsync(string pcName)
        => await SendAsync(HttpMethod.Get, $"{CrBase}/by-name/{pcName}/autounattend.xml");

    // ── Workflow ──────────────────────────────────────────────────────────────

    public async Task<string> GetWorkflowsJsonAsync()
        => await SendAsync(HttpMethod.Get, $"{ApiBase}/api/workflows");

    public async Task<string> GetWorkflowDetailJsonAsync(int wfId)
        => await SendAsync(HttpMethod.Get, $"{ApiBase}/api/workflows/{wfId}");

    public async Task<string> PostWorkflowAsync(object data)
        => await SendAsync(HttpMethod.Post, $"{ApiBase}/api/workflows", Json(data));

    public async Task<string> PutWorkflowAsync(int wfId, object data)
        => await SendAsync(HttpMethod.Put, $"{ApiBase}/api/workflows/{wfId}", Json(data));

    public async Task DeleteWorkflowAsync(int wfId)
        => await SendAsync(HttpMethod.Delete, $"{ApiBase}/api/workflows/{wfId}");

    // ── Workflow Steps ────────────────────────────────────────────────────────

    public async Task<string> PostWorkflowStepAsync(int wfId, object data)
        => await SendAsync(HttpMethod.Post, $"{ApiBase}/api/workflows/{wfId}/steps", Json(data));

    public async Task<string> PutWorkflowStepAsync(int wfId, int stepId, object data)
        => await SendAsync(HttpMethod.Put, $"{ApiBase}/api/workflows/{wfId}/steps/{stepId}", Json(data));

    public async Task DeleteWorkflowStepAsync(int wfId, int stepId)
        => await SendAsync(HttpMethod.Delete, $"{ApiBase}/api/workflows/{wfId}/steps/{stepId}");

    // ── PC Workflows ──────────────────────────────────────────────────────────

    public async Task<string> GetPcWorkflowsJsonAsync()
        => await SendAsync(HttpMethod.Get, $"{ApiBase}/api/pc-workflows");

    public async Task<string> PostPcWorkflowAsync(object data)
        => await SendAsync(HttpMethod.Post, $"{ApiBase}/api/pc-workflows", Json(data));

    public async Task DeletePcWorkflowAsync(int pwId)
        => await SendAsync(HttpMethod.Delete, $"{ApiBase}/api/pc-workflows/{pwId}");

    // ── Version / Update ──────────────────────────────────────────────────────

    public async Task<string> GetVersionJsonAsync()
        => await SendAsync(HttpMethod.Get, $"{ApiBase}/api/version");

    public async Task<byte[]> DownloadExeAsync(string downloadUrl)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        AddAuth(req);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"{(int)resp.StatusCode} {resp.ReasonPhrase}: download fallito",
                null, resp.StatusCode);
        return await resp.Content.ReadAsByteArrayAsync();
    }

    // ── Dashboard ─────────────────────────────────────────────────────────────

    public async Task<string> GetDashboardJsonAsync(bool forceRefresh = false)
    {
        var url = $"{ApiBase}/api/cr";
        if (!forceRefresh && _cache.TryGet(url, out var cached)) return cached;
        var json = await SendAsync(HttpMethod.Get, url);
        _cache.Set(url, json, TimeSpan.FromSeconds(60));
        return json;
    }

    // ── Health ────────────────────────────────────────────────────────────────

    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            var resp = await _http.SendAsync(Req(HttpMethod.Get, $"{ApiBase}/health"));
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
