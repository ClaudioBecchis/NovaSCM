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

    private void SetApiKey(string apiKey)
    {
        _http.DefaultRequestHeaders.Remove("X-Api-Key");
        if (!string.IsNullOrEmpty(apiKey))
            _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    }

    public async Task<JsonObject?> GetWorkflowAsync(string apiUrl, string pcName, CancellationToken ct, string apiKey = "")
    {
        SetApiKey(apiKey);
        try
        {
            var url = $"{apiUrl.TrimEnd('/')}/api/pc/{pcName}/workflow";
            var r   = await _http.GetAsync(url, ct);
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
        SetApiKey(apiKey);
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
            await _http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning("POST step {StepId}: {Err}", stepId, ex.Message);
        }
    }

    public async Task CheckinAsync(string apiUrl, string pcName, CancellationToken ct, string apiKey = "")
    {
        SetApiKey(apiKey);
        try
        {
            var url  = $"{apiUrl.TrimEnd('/')}/api/pc/{pcName}/workflow/checkin";
            var body = new StringContent("{}", Encoding.UTF8, "application/json");
            await _http.PostAsync(url, body, ct);
        }
        catch { /* heartbeat fire-and-forget */ }
    }
}
