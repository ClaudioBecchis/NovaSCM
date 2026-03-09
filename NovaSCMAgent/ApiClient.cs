using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NovaSCMAgent;

public class ApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ApiClient> _log;
    private const string AgentVer = "1.0.0";

    public ApiClient(ILogger<ApiClient> log)
    {
        _log = log;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.Add("User-Agent", $"NovaSCMAgent/{AgentVer}");
    }

    // BUG-6: header per-request invece di DefaultRequestHeaders (non thread-safe)
    private HttpRequestMessage BuildRequest(HttpMethod method, string url, string apiKey, HttpContent? body = null)
    {
        var req = new HttpRequestMessage(method, url) { Content = body };
        if (!string.IsNullOrEmpty(apiKey))
            req.Headers.Add("X-Api-Key", apiKey);
        return req;
    }

    public async Task<JsonObject?> GetWorkflowAsync(string apiUrl, string pcName, CancellationToken ct, string apiKey = "")
    {
        try
        {
            var url = $"{apiUrl.TrimEnd('/')}/api/pc/{pcName}/workflow";
            using var req = BuildRequest(HttpMethod.Get, url, apiKey);
            var r   = await _http.SendAsync(req, ct);
            if (!r.IsSuccessStatusCode) return null;
            var json = await r.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<JsonObject>(json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning("GET /api/pc/{Pc}/workflow: {Err}", pcName, ex.Message);
            return null;
        }
    }

    public async Task ReportStepAsync(string apiUrl, string pcName, int stepId,
                                      string status, string output, CancellationToken ct, string apiKey = "")
    {
        try
        {
            var url  = $"{apiUrl.TrimEnd('/')}/api/pc/{pcName}/workflow/step";
            var body = JsonSerializer.Serialize(new
            {
                step_id = stepId,
                status,
                output  = output.Length > 2000 ? output[^2000..] : output,
                ts      = DateTime.Now.ToString("o")
            });
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var req = BuildRequest(HttpMethod.Post, url, apiKey, content);
            await _http.SendAsync(req, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning("POST step {StepId}: {Err}", stepId, ex.Message);
        }
    }

    public async Task CheckinAsync(string apiUrl, string pcName, CancellationToken ct, string apiKey = "")
    {
        try
        {
            var url     = $"{apiUrl.TrimEnd('/')}/api/pc/{pcName}/workflow/checkin";
            var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var req = BuildRequest(HttpMethod.Post, url, apiKey, content);
            await _http.SendAsync(req, ct);
        }
        catch { /* heartbeat fire-and-forget */ }
    }
}
