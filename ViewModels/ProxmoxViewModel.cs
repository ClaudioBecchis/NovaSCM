using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Input;
using NovaSCM.Commands;

namespace NovaSCM.ViewModels;

/// <summary>
/// ViewModel per il tab Proxmox — gestione VM/CT, console, monitoraggio.
/// </summary>
public class ProxmoxViewModel : ViewModelBase
{
    private HttpClient? _http;
    private string _cookie = "";
    private string _csrfToken = "";

    // ── Connection ──
    private string _host = "";
    public string Host { get => _host; set => SetProperty(ref _host, value); }

    private string _user = "root@pam";
    public string User { get => _user; set => SetProperty(ref _user, value); }

    private string _password = "";
    public string Password { get => _password; set => SetProperty(ref _password, value); }

    private bool _isConnected;
    public bool IsConnected { get => _isConnected; set => SetProperty(ref _isConnected, value); }

    // ── Guests ──
    public ObservableCollection<PveGuest> Guests { get; } = new();
    public ObservableCollection<string> Nodes { get; } = new();

    private PveGuest? _selectedGuest;
    public PveGuest? SelectedGuest { get => _selectedGuest; set => SetProperty(ref _selectedGuest, value); }

    private string _selectedNode = "";
    public string SelectedNode { get => _selectedNode; set => SetProperty(ref _selectedNode, value); }

    private string _filterText = "";
    public string FilterText { get => _filterText; set => SetProperty(ref _filterText, value); }

    // ── Status ──
    private string _status = "";
    public string Status { get => _status; set => SetProperty(ref _status, value); }

    // ── Comandi ──
    public ICommand ConnectCommand => new AsyncRelayCommand(ConnectAsync);
    public ICommand RefreshCommand => new AsyncRelayCommand(RefreshAsync);
    public ICommand StartCommand => new AsyncRelayCommand(() => GuestActionAsync("start"));
    public ICommand StopCommand => new AsyncRelayCommand(() => GuestActionAsync("stop"));
    public ICommand RebootCommand => new AsyncRelayCommand(() => GuestActionAsync("reboot"));

    private void EnsureHttp()
    {
        if (_http != null) return;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            // BUG: UseCookies di default è true, ma RefreshAsync/GuestActionAsync
            // impostano l'header "Cookie" a mano su ogni HttpRequestMessage
            // (PVEAuthCookie) — con UseCookies=true, .NET lancia
            // InvalidOperationException al momento dell'invio perché l'header
            // Cookie va gestito solo tramite CookieContainer. Rompeva
            // sistematicamente ogni azione dopo la connessione (Refresh/Start/
            // Stop/Reboot), ingoiata dal try/catch e mostrata solo come
            // "Errore: ..." generico.
            UseCookies = false,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(Host)) { Status = "Host mancante"; return; }
        EnsureHttp();
        Status = "Connessione...";
        try
        {
            var url = $"https://{Host}:8006/api2/json/access/ticket";
            var resp = await _http!.PostAsync(url, new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", User),
                new KeyValuePair<string, string>("password", Password),
            }));
            if (!resp.IsSuccessStatusCode) { Status = $"Auth fallita: {(int)resp.StatusCode}"; return; }
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
            var data = json.GetProperty("data");
            _cookie = data.GetProperty("ticket").GetString() ?? "";
            _csrfToken = data.GetProperty("CSRFPreventionToken").GetString() ?? "";
            _http.DefaultRequestHeaders.Remove("CSRFPreventionToken");
            _http.DefaultRequestHeaders.Add("CSRFPreventionToken", _csrfToken);
            IsConnected = true;
            Status = "Connesso";
            await RefreshAsync();
        }
        catch (Exception ex) { Status = $"Errore: {ex.Message}"; }
    }

    public async Task RefreshAsync()
    {
        if (!IsConnected || _http == null) return;
        try
        {
            var url = $"https://{Host}:8006/api2/json/cluster/resources?type=vm";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Cookie", $"PVEAuthCookie={_cookie}");
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return;
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
            var data = json.GetProperty("data");

            Guests.Clear();
            Nodes.Clear();
            var nodeSet = new HashSet<string>();
            foreach (var item in data.EnumerateArray())
            {
                var node = item.GetProperty("node").GetString() ?? "";
                if (nodeSet.Add(node)) Nodes.Add(node);
                Guests.Add(new PveGuest
                {
                    Vmid = item.GetProperty("vmid").GetInt32(),
                    Name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Node = node,
                    Type = item.GetProperty("type").GetString() ?? "",
                    Status = item.GetProperty("status").GetString() ?? "",
                    Cpu = item.TryGetProperty("cpu", out var c) ? c.GetDouble() : 0,
                    Mem = item.TryGetProperty("mem", out var m) ? m.GetInt64() : 0,
                    MaxMem = item.TryGetProperty("maxmem", out var mm) ? mm.GetInt64() : 0,
                });
            }
            Status = $"{Guests.Count} guest trovati";
        }
        catch (Exception ex) { Status = $"Errore: {ex.Message}"; }
    }

    public async Task GuestActionAsync(string action)
    {
        if (!IsConnected || _http == null || SelectedGuest == null) return;
        var g = SelectedGuest;
        var url = $"https://{Host}:8006/api2/json/nodes/{g.Node}/{g.Type}/{g.Vmid}/status/{action}";
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("Cookie", $"PVEAuthCookie={_cookie}");
        try
        {
            await _http.SendAsync(req);
            Status = $"{action} inviato a {g.Name}";
            await Task.Delay(2000);
            await RefreshAsync();
        }
        catch (Exception ex) { Status = $"Errore: {ex.Message}"; }
    }
}

public class PveGuest : ViewModelBase
{
    private int _vmid; public int Vmid { get => _vmid; set => SetProperty(ref _vmid, value); }
    private string _name = ""; public string Name { get => _name; set => SetProperty(ref _name, value); }
    private string _node = ""; public string Node { get => _node; set => SetProperty(ref _node, value); }
    private string _type = ""; public string Type { get => _type; set => SetProperty(ref _type, value); }
    private string _status = ""; public string Status { get => _status; set => SetProperty(ref _status, value); }
    private double _cpu; public double Cpu { get => _cpu; set => SetProperty(ref _cpu, value); }
    private long _mem; public long Mem { get => _mem; set => SetProperty(ref _mem, value); }
    private long _maxMem; public long MaxMem { get => _maxMem; set => SetProperty(ref _maxMem, value); }
}
