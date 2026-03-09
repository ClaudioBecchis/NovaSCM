using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace PolarisManager;

// ── Modelli ───────────────────────────────────────────────────────────────────
public class DeviceRow : INotifyPropertyChanged
{
    private string _status         = "🔍 Scansione...";
    private string _mac            = "—";
    private string _vendor         = "—";
    private string _name           = "—";
    private string _icon           = "❓";
    private string _deviceType     = "—";
    private string _connectionType = "❓";

    public string Ip         { get; set; } = "";
    public string CertStatus { get; set; } = "⬜ No";
    public bool   WasOnline  { get; set; } = false;

    public string Mac            { get => _mac;            set { _mac            = value; OnPC(); } }
    public string Vendor         { get => _vendor;          set { _vendor         = value; OnPC(); } }
    public string Name           { get => _name;            set { _name           = value; OnPC(); } }
    public string Status         { get => _status;          set { _status         = value; OnPC(); } }
    public string Icon           { get => _icon;            set { _icon           = value; OnPC(); } }
    public string DeviceType     { get => _deviceType;      set { _deviceType     = value; OnPC(); } }
    public string ConnectionType { get => _connectionType;  set { _connectionType = value; OnPC(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPC([System.Runtime.CompilerServices.CallerMemberName] string? p = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

    // Aggiorna tipo e icona in base a porte aperte + vendor
    public void DetectType(IEnumerable<int> openPorts)
    {
        var ports  = new HashSet<int>(openPorts);
        var vendor = Vendor.ToLowerInvariant();
        var name   = Name.ToLowerInvariant();

        if (ports.Contains(8006))                          { Icon = "🔷"; DeviceType = "Proxmox"; }
        else if (ports.Contains(8123))                     { Icon = "🏠"; DeviceType = "Home Assistant"; }
        else if (ports.Contains(3389))                     { Icon = "🖥️"; DeviceType = "Windows PC"; }
        else if (ports.Contains(22) && !ports.Contains(3389) &&
                 (ports.Contains(80) || ports.Contains(443) || ports.Contains(9000) || ports.Contains(9090)))
                                                           { Icon = "🐧"; DeviceType = "Linux Server"; }
        else if (ports.Contains(22))                       { Icon = "🐧"; DeviceType = "Linux"; }
        else if (vendor.Contains("ubiquiti") || vendor.Contains("tp-link") ||
                 name.Contains("router") || name.Contains("gateway") || name.Contains("ucg"))
                                                           { Icon = "🌐"; DeviceType = "Router / AP"; }
        else if (vendor.Contains("apple"))                 { Icon = "🍎"; DeviceType = "Apple"; }
        else if (vendor.Contains("oneplus") || vendor.Contains("samsung") ||
                 vendor.Contains("xiaomi") || vendor.Contains("oppo"))
                                                           { Icon = "📱"; DeviceType = "Mobile"; }
        else if (vendor.Contains("raspberry"))             { Icon = "🍓"; DeviceType = "Raspberry Pi"; }
        else if (ports.Contains(9100) || ports.Contains(515) || ports.Contains(631))
                                                           { Icon = "🖨️"; DeviceType = "Stampante"; }
        else if (ports.Contains(1883))                     { Icon = "🔌"; DeviceType = "IoT / MQTT"; }
        else if (ports.Count == 0)                         { Icon = "📡"; DeviceType = "IoT / Smart"; }
        else                                               { Icon = "❔"; DeviceType = "Sconosciuto"; }
    }
}

record OuiRecord(
    [property: JsonPropertyName("macPrefix")]  string MacPrefix,
    [property: JsonPropertyName("vendorName")] string VendorName,
    [property: JsonPropertyName("private")]    bool   Private);

public record CertRow(string Icon, string Name, string Mac, string Created, string Expires, string Status);
public record AppQueueRow(string Pc, string Ip, string Mac, string Apps, string Status);
public record AppCatRow(string Category, string Items);
public record OpsiRow(string Name, string Version, string Status, string Updated);
public record PcRow(string Icon, string Name, string Ip, string Os, string Cpu, string Ram, string Status, string Agent);

// ── Workflow models ────────────────────────────────────────────────────────────
public class WfRow : INotifyPropertyChanged
{
    private string _nome = "";
    private int    _stepCount;
    public int    Id          { get; set; }
    public string Nome        { get => _nome;       set { _nome       = value; OnPC(); } }
    public string Descrizione { get; set; } = "";
    public int    Versione    { get; set; }
    public int    StepCount   { get => _stepCount;  set { _stepCount  = value; OnPC(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPC([System.Runtime.CompilerServices.CallerMemberName] string? p = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

public class WfStepRow
{
    public int    Id         { get; set; }
    public int    WorkflowId { get; set; }
    public int    Ordine     { get; set; }
    public string Nome       { get; set; } = "";
    public string Tipo       { get; set; } = "";
    public string Parametri  { get; set; } = "{}";
    public string Platform   { get; set; } = "all";
    public string SuErrore   { get; set; } = "stop";
}

public class WfAssignRow : INotifyPropertyChanged
{
    private string _status   = "";
    private int    _progress;
    public int    Id           { get; set; }
    public string PcName       { get; set; } = "";
    public string WorkflowNome { get; set; } = "";
    public int    WorkflowId   { get; set; }
    public string Status       { get => _status;   set { _status   = value; OnPC(); OnPC(nameof(ProgressColor)); } }
    public int    Progress     { get => _progress; set { _progress = value; OnPC(); OnPC(nameof(ProgressText)); } }
    public string ProgressText => $"{Progress}%";
    public System.Windows.Media.SolidColorBrush ProgressColor => Status switch
    {
        "running"   => System.Windows.Media.Brushes.DodgerBlue,
        "completed" => System.Windows.Media.Brushes.LimeGreen,
        "failed"    => System.Windows.Media.Brushes.Tomato,
        _           => System.Windows.Media.Brushes.SlateGray,
    };
    public string AssignedAt   { get; set; } = "";
    public string LastSeen     { get; set; } = "";
    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPC([System.Runtime.CompilerServices.CallerMemberName] string? p = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

// UI-06: step timeline view model
public class WfTimelineItem
{
    public int    Ordine              { get; set; }
    public string ShortNome           { get; set; } = "";
    public string BubbleColor         { get; set; } = "#3B82F6";
    public System.Windows.Visibility ConnectorVisibility { get; set; }
        = System.Windows.Visibility.Collapsed;
}

// ── Deploy config ─────────────────────────────────────────────────────────────
class DeployConfig
{
    public string WinEdition      { get; set; } = "Windows 11 Pro";
    public string WinEditionId    { get; set; } = "Professional";   // Home / Professional / Enterprise
    public string PcNameTemplate  { get; set; } = "PC-{MAC6}";      // {MAC6} = ultimi 6 hex del MAC
    public string Locale          { get; set; } = "it-IT";
    public string TimeZone        { get; set; } = "W. Europe Standard Time";
    public string AdminPassword   { get; set; } = "";
    public string UserName        { get; set; } = "";
    public string UserPassword    { get; set; } = "";
    public List<string> WingetPackages { get; set; } = [];
    public string ProductKey      { get; set; } = "";  // opzionale — vuoto = KMS/valutazione
    public bool   IncludeAgent    { get; set; } = true;
    public string ServerUrl       { get; set; } = "";
    public string NovaSCMCrApiUrl { get; set; } = "";   // per check-in post-install
    public string PxeServerIp        { get; set; } = "";
    public string PxeServerPath      { get; set; } = "/srv/netboot/novascm/";
    public bool   UseMicrosoftAccount { get; set; } = false;
    // Dominio: "Workgroup" | "AD" | "AzureAD"
    public string DomainJoin     { get; set; } = "Workgroup";
    public string DomainName         { get; set; } = "";
    public string DomainUser         { get; set; } = "";
    public string DomainPassword     { get; set; } = "";
    public string DomainControllerIp { get; set; } = "";
    public string AzureTenantId  { get; set; } = "";
}

// ── Config ────────────────────────────────────────────────────────────────────
class AppConfig
{
    public string CertportalUrl { get; set; } = "";
    public string UnifiUrl      { get; set; } = "";
    public string UnifiUser     { get; set; } = "admin";
    // Campi plain mantenuti per retrocompatibilità (migrazione automatica al primo salvataggio)
    public string UnifiPass     { get; set; } = "";
    public string Ssid          { get; set; } = "NovaSCM-Secure";
    public string RadiusIp      { get; set; } = "";
    public string CertDays      { get; set; } = "3650";
    public string OrgName       { get; set; } = "";
    public string Domain        { get; set; } = "";
    public string ScanNetwork   { get; set; } = "192.168.1.0";
    public string ScanSubnet    { get; set; } = "24";
    public string AdminUser     { get; set; } = "";
    public string AdminPass     { get; set; } = "";
    // Subnet multiple — una per riga in formato "192.168.1.0/24"
    public string ScanNetworks  { get; set; } = "192.168.1.0/24";
    public string NovaSCMApiUrl { get; set; } = "";
    public string NovaSCMApiKey { get; set; } = "";
    // BUG-04: campi cifrati con DPAPI (sostituiscono i campi plain sopra)
    public string UnifiPassE     { get; set; } = "";
    public string AdminPassE     { get; set; } = "";
    public string NovaSCMApiKeyE { get; set; } = "";
}

public class CrRow
{
    public int    Id           { get; set; }
    public string PcName       { get; set; } = "";
    public string Domain       { get; set; } = "";
    public string Ou           { get; set; } = "";
    public string AssignedUser { get; set; } = "";
    public string Status       { get; set; } = "";
    public string CreatedAt    { get; set; } = "";
    public string Notes        { get; set; } = "";
    public string LastSeen     { get; set; } = "";
}

// PERF-03: cache HTTP con TTL per evitare chiamate API ripetute
public class ApiCache
{
    private readonly record struct Entry(string Json, DateTime Expires);
    private readonly Dictionary<string, Entry> _cache = [];
    private readonly int _maxEntries;

    public ApiCache(int maxEntries = 64) => _maxEntries = maxEntries;

    public bool TryGet(string url, out string json)
    {
        if (_cache.TryGetValue(url, out var e) && DateTime.UtcNow < e.Expires)
        { json = e.Json; return true; }
        json = "";
        return false;
    }

    public void Set(string url, string json, TimeSpan ttl)
    {
        if (_cache.Count >= _maxEntries)
        {
            var oldest = _cache.MinBy(kv => kv.Value.Expires).Key;
            _cache.Remove(oldest);
        }
        _cache[url] = new Entry(json, DateTime.UtcNow + ttl);
    }

    public void Invalidate(string url) => _cache.Remove(url);
    public void InvalidateAll() => _cache.Clear();
}

public partial class MainWindow : Window
{
    private static readonly string ConfigPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "PolarisManager", "config.json");

    private AppConfig _config = new();
    private CancellationTokenSource? _scanCts;
    private readonly ObservableCollection<DeviceRow>   _netRows       = [];
    private CancellationTokenSource? _monitorCts;
    private bool _monitoring = false;

    // Workflow tab
    private readonly ObservableCollection<WfRow>       _wfRows        = [];
    private readonly ObservableCollection<WfStepRow>   _wfStepRows    = [];
    private readonly ObservableCollection<WfAssignRow> _wfAssignRows  = [];
    private int _selectedWfId = -1;
    private string WfApiBase => (_config.NovaSCMApiUrl ?? "").TrimEnd('/');

    // FEAT-01: Dashboard timer
    private readonly System.Windows.Threading.DispatcherTimer _dashTimer = new()
        { Interval = TimeSpan.FromSeconds(30) };

    // DX-01: config hot reload
    private FileSystemWatcher? _configWatcher;

    // PERF-03: API cache
    private readonly ApiCache _apiCache = new();

    // ARCH-01: servizio API centralizzato (ricreato in LoadConfig quando cambia l'URL)
    private NovaSCMApiService? _apiSvc;

    // BUG-04: DPAPI helpers (Windows-only, CurrentUser scope)
    private static string DpapiEncrypt(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var encrypted = System.Security.Cryptography.ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes(plain), null,
            System.Security.Cryptography.DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private static string DpapiDecrypt(string cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return "";
        try
        {
            var plain = System.Security.Cryptography.ProtectedData.Unprotect(
                Convert.FromBase64String(cipher), null,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(plain);
        }
        catch { return ""; }
    }

    // NEW-E: null-guard per handler che richiedono l'API configurata
    private bool EnsureApiConfigured(string? statusControl = null)
    {
        if (_apiSvc != null) return true;
        var msg = "⚙️  Configura l'URL API NovaSCM nelle Impostazioni";
        SetStatus(msg);
        return false;
    }

    // FEAT-03: Search overlay record
    private record SearchResult(string Label, string Sub, int TabIndex, string Workspace = "asset");

    public MainWindow()
    {
        InitializeComponent();
        Database.Initialize();
        // Forza render del primo tab al caricamento
        Dispatcher.BeginInvoke(() =>
        {
            MainTabs.SelectedIndex = 0;
            UpdateNavState(0);
            SwitchWorkspace("asset");
        }, System.Windows.Threading.DispatcherPriority.Render);
        bool firstRun = !File.Exists(ConfigPath);
        LoadConfig();
        NetGrid.ItemsSource     = _netRows;
        LstPackages.ItemsSource = _deployPackages;
        LoadFromDatabase();
        RefreshProfiles();
        InitCrTab();
        InitWorkflowTab();
        _ = LoadOuiDatabaseAsync();
        TxtAboutVersion.Text = $"v{CurrentVersion} ";
        // Controlla aggiornamenti in background 3s dopo l'avvio
        Dispatcher.BeginInvoke(async () => await CheckForUpdatesAsync(silent: true),
                               System.Windows.Threading.DispatcherPriority.Background);

        // FEAT-01: Dashboard auto-refresh ogni 30s
        _dashTimer.Tick += async (_, _) => await RefreshDashboardAsync();
        _ = RefreshDashboardAsync();  // carica subito al primo avvio

        if (firstRun)
        {
            // Prima esecuzione: apre tab Impostazioni con messaggio di benvenuto
            Dispatcher.BeginInvoke(() =>
            {
                MainTabs.SelectedIndex = MainTabs.Items.Count - 1; // tab Impostazioni (ultimo)
                TxtSettingsStatus.Text = "👋  Prima esecuzione — configura il server NovaSCM e salva.";
                TxtSettingsStatus.Foreground = System.Windows.Media.Brushes.Gold;
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    // ── Config ────────────────────────────────────────────────────────────────
    private void LoadConfig()
    {
        if (File.Exists(ConfigPath))
            try { _config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath)) ?? new(); }
            catch { _config = new(); }
        // BUG-04: decripta campi DPAPI (fallback al plain per retrocompatibilità)
        if (!string.IsNullOrEmpty(_config.UnifiPassE))     _config.UnifiPass     = DpapiDecrypt(_config.UnifiPassE);
        if (!string.IsNullOrEmpty(_config.AdminPassE))     _config.AdminPass     = DpapiDecrypt(_config.AdminPassE);
        if (!string.IsNullOrEmpty(_config.NovaSCMApiKeyE)) _config.NovaSCMApiKey = DpapiDecrypt(_config.NovaSCMApiKeyE);
        // ARCH-01: ricrea il servizio API con i nuovi parametri di configurazione
        _apiSvc = string.IsNullOrWhiteSpace(_config.NovaSCMApiUrl)
            ? null
            : new NovaSCMApiService(_config.NovaSCMApiUrl, _config.NovaSCMApiKey, _apiCache);
        ApplyConfigToUI();
        InitConfigWatcher();
    }

    // DX-01: FileSystemWatcher per hot reload config
    private void InitConfigWatcher()
    {
        _configWatcher?.Dispose();
        var dir = Path.GetDirectoryName(ConfigPath);
        if (dir == null || !Directory.Exists(dir)) return;
        _configWatcher = new FileSystemWatcher(dir, Path.GetFileName(ConfigPath))
        {
            NotifyFilter      = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _configWatcher.Changed += (_, _) =>
        {
            Thread.Sleep(100); // debounce
            Dispatcher.Invoke(() =>
            {
                LoadConfig();
                // config ricaricata — mostra notifica
                Notifier.Show("Config aggiornata", "config.json ricaricato automaticamente",
                              Notifier.Level.Info, autoCloseSec: 4);
            });
        };
    }

    private void ApplyConfigToUI()
    {
        TxtCertportalUrl.Text = _config.CertportalUrl;
        TxtUnifiUrl.Text      = _config.UnifiUrl;
        TxtUnifiUser.Text     = _config.UnifiUser;
        TxtUnifiPass.Password = _config.UnifiPass;
        TxtSsid.Text          = _config.Ssid;
        TxtRadiusIp.Text      = _config.RadiusIp;
        TxtCertDays.Text      = _config.CertDays;
        TxtOrgName.Text       = _config.OrgName;
        TxtDomain.Text        = _config.Domain;
        TxtScanIp.Text        = _config.ScanNetwork;
        TxtScanSubnet.Text    = _config.ScanSubnet;
        TxtScanNetworks.Text  = _config.ScanNetworks;
        TxtAdminUser.Text     = _config.AdminUser;
        TxtAdminPass.Password = _config.AdminPass;
        TxtNovaSCMApiUrl.Text    = _config.NovaSCMApiUrl;
        TxtNovaSCMApiKey.Password = _config.NovaSCMApiKey;
    }

    private void SaveConfig()
    {
        _config.CertportalUrl = TxtCertportalUrl.Text.Trim();
        _config.UnifiUrl      = TxtUnifiUrl.Text.Trim();
        _config.UnifiUser     = TxtUnifiUser.Text.Trim();
        _config.Ssid          = TxtSsid.Text.Trim();
        _config.RadiusIp      = TxtRadiusIp.Text.Trim();
        _config.CertDays      = TxtCertDays.Text.Trim();
        _config.OrgName       = TxtOrgName.Text.Trim();
        _config.Domain        = TxtDomain.Text.Trim();
        _config.ScanNetwork   = TxtScanIp.Text.Trim();
        _config.ScanSubnet    = TxtScanSubnet.Text.Trim();
        _config.ScanNetworks  = TxtScanNetworks.Text;
        _config.AdminUser     = TxtAdminUser.Text.Trim();
        _config.NovaSCMApiUrl = TxtNovaSCMApiUrl.Text.Trim();
        // BUG-04: cifra le credenziali con DPAPI, non salvare plaintext
        _config.UnifiPassE     = DpapiEncrypt(TxtUnifiPass.Password);
        _config.AdminPassE     = DpapiEncrypt(TxtAdminPass.Password);
        _config.NovaSCMApiKeyE = DpapiEncrypt(TxtNovaSCMApiKey.Password);
        _config.UnifiPass      = "";
        _config.AdminPass      = "";
        _config.NovaSCMApiKey  = "";
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath,
            JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ── Scansione rete ────────────────────────────────────────────────────────
    private async void BtnScan_Click(object s, RoutedEventArgs e)
    {
        if (!IPAddress.TryParse(TxtScanIp.Text.Trim(), out var baseIp))
        { SetStatus("⚠️ IP non valido"); return; }
        if (!int.TryParse(TxtScanSubnet.Text.Trim(), out int cidr) || cidr < 1 || cidr > 30)
        { SetStatus("⚠️ Subnet non valida (1-30)"); return; }
        List<IPAddress> ips;
        try { ips = GetHostsInSubnet(baseIp, cidr); }
        catch (Exception ex) { App.Log($"GetHostsInSubnet error: {ex}"); SetStatus($"❌ {ex.Message}"); return; }
        App.Log($"Scansione avviata — /{cidr} → {ips.Count} host");
        await RunScanAsync(ips);
    }

    private async void BtnScanAll_Click(object s, RoutedEventArgs e)
    {
        try
        {
            var lines = _config.ScanNetworks.Split('\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var allIps = new List<IPAddress>();
            foreach (var net in lines)
            {
                var parts = net.Split('/');
                if (parts.Length != 2) continue;
                if (!IPAddress.TryParse(parts[0].Trim(), out var baseIp)) continue;
                if (!int.TryParse(parts[1].Trim(), out int cidr) || cidr < 1 || cidr > 30) continue;
                try { allIps.AddRange(GetHostsInSubnet(baseIp, cidr)); } catch { }
            }
            if (allIps.Count == 0) { SetStatus("⚠️ Nessuna subnet valida configurata"); return; }
            App.Log($"Scansione VLAN avviata — {lines.Length} subnet, {allIps.Count} host totali");
            await RunScanAsync(allIps);
        }
        catch (Exception ex) { App.Log($"[BtnScanAll_Click] {ex}"); SetStatus($"❌ {ex.Message}"); }
    }

    private async Task RunScanAsync(List<IPAddress> ips)
    {
        _netRows.Clear();
        BtnScan.IsEnabled    = false;
        BtnScanAll.IsEnabled = false;
        BtnStop.IsEnabled    = true;
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;
        StartRadar();

        int total = ips.Count, done = 0, found = 0;
        ScanProgress.Maximum = total;
        ScanProgress.Value   = 0;

        var semaphore = new SemaphoreSlim(50);
        try
        {
            await Task.Run(async () =>
            {
                var tasks = ips.Select(async ip =>
                {
                    if (token.IsCancellationRequested) return;
                    await semaphore.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        using var ping = new Ping();
                        PingReply reply;
                        try { reply = await ping.SendPingAsync(ip, 800).ConfigureAwait(false); }
                        catch { Interlocked.Increment(ref done); return; }

                        Interlocked.Increment(ref done);

                        if (reply.Status == IPStatus.Success)
                        {
                            var row = new DeviceRow { Ip = ip.ToString(), Status = "🟢 Online" };
                            await Dispatcher.InvokeAsync(() => { _netRows.Add(row); found++; AddRadarBlip(row); TxtRadarStatus.Text = $"● SCANNING — {found} FOUND"; });
                            App.Log($"  Online: {ip}");

                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var host = await Dns.GetHostEntryAsync(ip.ToString()).ConfigureAwait(false);
                                    row.Name = host.HostName.Split('.')[0];
                                }
                                catch { }
                            });

                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var ipStr = ip.ToString();

                                    // MAC: ARP per device locali, NetworkInterface per se stesso
                                    var mac = GetMacFromArp(ipStr);
                                    if (mac == "—") mac = GetLocalMacForIp(ipStr);
                                    row.Mac    = mac;
                                    row.Vendor = LookupVendor(mac);
                                    if (row.Vendor == "—" && mac != "—")
                                        row.Vendor = await LookupVendorOnlineAsync(mac);

                                    // Tipo connessione per IP locali
                                    var localConn = GetLocalConnectionType(ipStr);
                                    if (localConn != "❓") row.ConnectionType = localConn;

                                    // Quick port scan — rileva tipo device automaticamente
                                    var sigPorts = new[] { 22, 80, 443, 3389, 8006, 8123, 9000, 9090, 1883 };
                                    var openSig  = new System.Collections.Concurrent.ConcurrentBag<int>();
                                    await Task.WhenAll(sigPorts.Select(async port =>
                                    {
                                        if (await QuickPortOpenAsync(ipStr, port, 400))
                                            openSig.Add(port);
                                    }));
                                    row.DetectType(openSig);

                                    // Deduplicazione per MAC — stessa NIC, due IP (cavo+WiFi)
                                    if (mac != "—")
                                    {
                                        await Dispatcher.InvokeAsync(() =>
                                        {
                                            var dupes = _netRows.Where(r => r.Mac == mac).ToList();
                                            if (dupes.Count > 1)
                                            {
                                                var primary = dupes.First();
                                                foreach (var d in dupes.Skip(1))
                                                {
                                                    if (primary.Name == "—" && d.Name != "—")
                                                        primary.Name = d.Name;
                                                    _netRows.Remove(d);
                                                }
                                                App.Log($"  Dedup MAC {mac}: tenuto {primary.Ip}");
                                            }
                                        });
                                    }
                                }
                                catch (Exception ex2) { App.Log($"ARP error {ip}: {ex2.Message}"); }
                            });
                        }

                        var d2 = done; var f2 = found;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            ScanProgress.Value = d2;
                            TxtScanStatus.Text = $"Scansione: {d2}/{total} — {f2} device trovati";
                        });
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { App.Log($"Scan error {ip}: {ex.Message}"); }
                    finally { semaphore.Release(); }
                });

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }, CancellationToken.None);
        }
        catch (Exception ex) { App.Log($"Scan task error: {ex}"); }

        BtnScan.IsEnabled    = true;
        BtnScanAll.IsEnabled = true;
        BtnStop.IsEnabled    = false;
        ScanProgress.Value   = total;
        var msg = token.IsCancellationRequested
            ? $"⏹ Scansione interrotta — {found} device trovati"
            : $"✅ Completata — {found} device online su {total} host";
        TxtScanStatus.Text = msg;
        SetStatus(msg);
        App.Log($"Scansione terminata: {found}/{total}");
        StopRadar(found);

        // Salva risultati scansione nel DB
        foreach (var d in _netRows)
            Database.UpsertDevice(d);

        // Change log: confronta con scan precedente
        if (!token.IsCancellationRequested)
            ShowChangeLog(_netRows.ToList());

        // Arricchisce con tipo connessione da UniFi (anche MAC mancanti per altre VLAN)
        if (!token.IsCancellationRequested && !string.IsNullOrEmpty(_config.UnifiUrl))
            _ = EnrichConnectionTypeFromUnifiAsync();
    }

    private void BtnStop_Click(object s, RoutedEventArgs e)
    {
        _scanCts?.Cancel();
        BtnScan.IsEnabled    = true;
        BtnScanAll.IsEnabled = true;
        BtnStop.IsEnabled    = false;
    }

    // ── Live Ping Graph ───────────────────────────────────────────────────────
    private CancellationTokenSource? _pingCts;
    private readonly List<double> _pingHistory = [];
    private const int PingHistoryMax = 60;

    private void NetGrid_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (NetGrid.SelectedItem is DeviceRow dev && dev.Status.Contains("Online") ||
            NetGrid.SelectedItem is DeviceRow d2 && d2.Status.Contains("🟢"))
        {
            var target = NetGrid.SelectedItem as DeviceRow;
            if (target == null) return;
            StartPingGraph(target.Ip, target.Name != "—" ? target.Name : target.Ip);
        }
        else
        {
            StopPingGraph();
        }
    }

    private void StartPingGraph(string ip, string label)
    {
        StopPingGraph();
        _pingHistory.Clear();
        PingPanel.Visibility = Visibility.Visible;
        TxtPingHost.Text     = label;
        TxtPingLast.Text     = "— ms";
        TxtPingAvg.Text      = "avg: — ms";
        TxtPingMin.Text      = "min: — ms";
        TxtPingMax.Text      = "max: — ms";

        _pingCts = new CancellationTokenSource();
        var token = _pingCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                double ms = -1;
                try
                {
                    using var ping = new System.Net.NetworkInformation.Ping();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var reply = await ping.SendPingAsync(ip, 1000);
                    sw.Stop();
                    if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                    {
                        // Stopwatch per precisione sub-millisecondo su reti locali
                        ms = reply.RoundtripTime > 0
                            ? reply.RoundtripTime
                            : Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                        if (ms < 0.1) ms = 0.1; // evita 0 esatto
                    }
                }
                catch { ms = -1; }

                Dispatcher.Invoke(() =>
                {
                    _pingHistory.Add(ms);
                    if (_pingHistory.Count > PingHistoryMax)
                        _pingHistory.RemoveAt(0);
                    UpdatePingGraph(ms);
                });

                await Task.Delay(1000, token).ContinueWith(_ => { });
            }
        }, token);
    }

    private void StopPingGraph()
    {
        _pingCts?.Cancel();
        _pingCts = null;
        PingPanel.Visibility = Visibility.Collapsed;
        _pingHistory.Clear();
    }

    private void UpdatePingGraph(double latestMs)
    {
        PingCanvas.Children.Clear();
        double w = PingCanvas.ActualWidth;
        double h = PingCanvas.ActualHeight;
        if (w < 10 || h < 10 || _pingHistory.Count < 2) return;

        var valid = _pingHistory.Where(v => v >= 0).ToList();
        if (valid.Count == 0) return;

        double maxMs = Math.Max(valid.Max(), 200);
        double minMs = 0;

        // Linee griglia orizzontali
        foreach (var ms in new[] { maxMs * 0.25, maxMs * 0.5, maxMs * 0.75 })
        {
            double gy = h - ((ms - minMs) / (maxMs - minMs)) * h;
            var gl = new System.Windows.Shapes.Line
            {
                X1 = 0, Y1 = gy, X2 = w, Y2 = gy,
                Stroke = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(25, 255, 255, 255)),
                StrokeThickness = 1
            };
            PingCanvas.Children.Add(gl);
            var lbl = new TextBlock
            {
                Text = $"{ms:0}ms", FontSize = 8,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(100, 148, 163, 184))
            };
            Canvas.SetLeft(lbl, 2);
            Canvas.SetTop(lbl, gy - 10);
            PingCanvas.Children.Add(lbl);
        }

        // Area fill sotto la curva
        var geo = new System.Windows.Media.StreamGeometry();
        using (var ctx = geo.Open())
        {
            bool first = true;
            for (int i = 0; i < _pingHistory.Count; i++)
            {
                if (_pingHistory[i] < 0) continue;
                double x = (double)i / (PingHistoryMax - 1) * w;
                double y = h - ((_pingHistory[i] - minMs) / (maxMs - minMs)) * h;
                if (first) { ctx.BeginFigure(new System.Windows.Point(x, h), true, true); first = false; }
                ctx.LineTo(new System.Windows.Point(x, y), true, false);
            }
            if (!first)
            {
                ctx.LineTo(new System.Windows.Point(w, h), true, false);
            }
        }
        geo.Freeze();
        var fill = new System.Windows.Shapes.Path
        {
            Data = geo,
            Fill = new System.Windows.Media.LinearGradientBrush(
                System.Windows.Media.Color.FromArgb(80, 16, 185, 129),
                System.Windows.Media.Color.FromArgb(0,  16, 185, 129),
                90)
        };
        PingCanvas.Children.Add(fill);

        // Linea principale
        var lineGeo = new System.Windows.Media.StreamGeometry();
        using (var ctx = lineGeo.Open())
        {
            bool first = true;
            for (int i = 0; i < _pingHistory.Count; i++)
            {
                if (_pingHistory[i] < 0) continue;
                double x = (double)i / (PingHistoryMax - 1) * w;
                double y = h - ((_pingHistory[i] - minMs) / (maxMs - minMs)) * h;
                if (first) { ctx.BeginFigure(new System.Windows.Point(x, y), false, false); first = false; }
                else ctx.LineTo(new System.Windows.Point(x, y), true, false);
            }
        }
        lineGeo.Freeze();

        // Colore in base alla latenza
        var lineColor = latestMs < 0   ? System.Windows.Media.Color.FromRgb(239, 68, 68)
                      : latestMs < 50  ? System.Windows.Media.Color.FromRgb(16, 185, 129)
                      : latestMs < 150 ? System.Windows.Media.Color.FromRgb(245, 158, 11)
                      :                  System.Windows.Media.Color.FromRgb(239, 68, 68);

        var linePath = new System.Windows.Shapes.Path
        {
            Data = lineGeo,
            Stroke = new System.Windows.Media.SolidColorBrush(lineColor),
            StrokeThickness = 2,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = lineColor, BlurRadius = 6, ShadowDepth = 0, Opacity = 0.8
            }
        };
        PingCanvas.Children.Add(linePath);

        // Punto corrente
        if (latestMs >= 0)
        {
            double lx = ((double)(_pingHistory.Count - 1) / (PingHistoryMax - 1)) * w;
            double ly = h - ((latestMs - minMs) / (maxMs - minMs)) * h;
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 8, Height = 8,
                Fill = new System.Windows.Media.SolidColorBrush(lineColor),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = lineColor, BlurRadius = 10, ShadowDepth = 0
                }
            };
            Canvas.SetLeft(dot, lx - 4);
            Canvas.SetTop(dot,  ly - 4);
            PingCanvas.Children.Add(dot);
        }

        // Aggiorna statistiche
        string FmtMs(double v) => v < 1 ? $"< 1 ms" : $"{v:0} ms";
        TxtPingLast.Text = latestMs >= 0 ? FmtMs(latestMs) : "timeout";
        TxtPingLast.Foreground = new System.Windows.Media.SolidColorBrush(lineColor);
        if (valid.Count > 0)
        {
            TxtPingAvg.Text = $"avg: {FmtMs(valid.Average())}";
            TxtPingMin.Text = $"min: {FmtMs(valid.Min())}";
            TxtPingMax.Text = $"max: {FmtMs(valid.Max())}";
        }
    }

    // ── Network Radar ─────────────────────────────────────────────────────────
    private const double RadarW = 700, RadarH = 480;
    private double _radarCx, _radarCy, _radarMaxR;
    private System.Windows.Controls.Canvas? _radarBlipLayer;

    private void StartRadar()
    {
        _radarBlipLayer = null;
        RadarCanvas.Children.Clear();
        RadarPanel.Visibility  = Visibility.Visible;
        RadarPanel.Opacity     = 1;
        NetGrid.Visibility     = Visibility.Collapsed;
        MapPanel.Visibility    = Visibility.Collapsed;

        _radarCx   = RadarW / 2;
        _radarCy   = RadarH / 2;
        _radarMaxR = Math.Min(_radarCx, _radarCy) * 0.86;

        DrawRadarBackground();
        StartRadarSweep();
    }

    private void StopRadar(int devices)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            TxtRadarStatus.Text = $"● SCAN COMPLETE — {devices} DEVICES FOUND";
            await Task.Delay(2500);
            var fade = new System.Windows.Media.Animation.DoubleAnimation(
                1, 0, new Duration(TimeSpan.FromSeconds(1.2)));
            fade.Completed += (_, _) =>
            {
                RadarPanel.Visibility = Visibility.Collapsed;
                NetGrid.Visibility    = Visibility.Visible;
            };
            RadarPanel.BeginAnimation(UIElement.OpacityProperty, fade);
        });
    }

    private void DrawRadarBackground()
    {
        double cx = _radarCx, cy = _radarCy, maxR = _radarMaxR;

        // Dot grid
        for (int gx = 0; gx < RadarW; gx += 24)
            for (int gy = 0; gy < RadarH; gy += 24)
            {
                var d = new System.Windows.Shapes.Rectangle
                {
                    Width = 1.2, Height = 1.2,
                    Fill = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(35, 0, 200, 80))
                };
                Canvas.SetLeft(d, gx); Canvas.SetTop(d, gy);
                RadarCanvas.Children.Add(d);
            }

        // Concentric rings
        for (int i = 1; i <= 4; i++)
        {
            double r = maxR * i / 4;
            var ring = new System.Windows.Shapes.Ellipse
            {
                Width = r * 2, Height = r * 2,
                Stroke = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(55, 0, 200, 80)),
                StrokeThickness = 1
            };
            Canvas.SetLeft(ring, cx - r); Canvas.SetTop(ring, cy - r);
            RadarCanvas.Children.Add(ring);

            var rl = new TextBlock
            {
                Text = $"/{i * 64}", FontSize = 8,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(70, 0, 200, 80))
            };
            Canvas.SetLeft(rl, cx + r - 18); Canvas.SetTop(rl, cy + 3);
            RadarCanvas.Children.Add(rl);
        }

        // Cross + diagonal lines
        foreach (var (x1, y1, x2, y2) in new[]
        {
            (cx - maxR, cy, cx + maxR, cy),
            (cx, cy - maxR, cx, cy + maxR),
            (cx - maxR * .707, cy - maxR * .707, cx + maxR * .707, cy + maxR * .707),
            (cx - maxR * .707, cy + maxR * .707, cx + maxR * .707, cy - maxR * .707)
        })
        {
            RadarCanvas.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(22, 0, 200, 80)),
                StrokeThickness = 1
            });
        }

        // Center blip
        var center = new System.Windows.Shapes.Ellipse
        {
            Width = 9, Height = 9,
            Fill = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0, 255, 100)),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = System.Windows.Media.Color.FromRgb(0, 255, 100),
                BlurRadius = 14, ShadowDepth = 0
            }
        };
        Canvas.SetLeft(center, cx - 4.5); Canvas.SetTop(center, cy - 4.5);
        RadarCanvas.Children.Add(center);

        // Header
        var hdr = new TextBlock
        {
            Text = "POLARISCORE ◆ NETWORK RADAR ◆ LIVE SCAN",
            FontSize = 10, FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(140, 0, 200, 80))
        };
        Canvas.SetLeft(hdr, 12); Canvas.SetTop(hdr, 9);
        RadarCanvas.Children.Add(hdr);
    }

    private void StartRadarSweep()
    {
        double cx = _radarCx, cy = _radarCy, maxR = _radarMaxR;

        // Sweep cone — layers of fading trail lines
        var sweepCanvas = new System.Windows.Controls.Canvas
        {
            Width = RadarW, Height = RadarH,
            RenderTransformOrigin = new System.Windows.Point(cx / RadarW, cy / RadarH),
            RenderTransform = new System.Windows.Media.RotateTransform(0)
        };

        for (int trail = 0; trail <= 50; trail += 2)
        {
            byte alpha = (byte)((50 - trail) / 50.0 * (trail == 0 ? 230 : 90));
            double rad  = (-90 - trail) * Math.PI / 180;
            var tl = new System.Windows.Shapes.Line
            {
                X1 = cx, Y1 = cy,
                X2 = cx + maxR * Math.Cos(rad),
                Y2 = cy + maxR * Math.Sin(rad),
                Stroke = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(alpha, 0, 220, 80)),
                StrokeThickness = trail == 0 ? 2.5 : 1.5
            };
            if (trail == 0)
                tl.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = System.Windows.Media.Color.FromRgb(0, 255, 80),
                    BlurRadius = 14, ShadowDepth = 0, Opacity = 1
                };
            sweepCanvas.Children.Add(tl);
        }
        RadarCanvas.Children.Add(sweepCanvas);

        var rot = (System.Windows.Media.RotateTransform)sweepCanvas.RenderTransform;
        rot.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 360,
                new Duration(TimeSpan.FromSeconds(3)))
            {
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            });

        // Blip layer on top
        _radarBlipLayer = new System.Windows.Controls.Canvas { Width = RadarW, Height = RadarH };
        RadarCanvas.Children.Add(_radarBlipLayer);
    }

    private void AddRadarBlip(DeviceRow device)
    {
        if (_radarBlipLayer == null) return;

        var parts = device.Ip.Split('.');
        if (parts.Length < 4 ||
            !int.TryParse(parts[2], out int third) ||
            !int.TryParse(parts[3], out int fourth)) return;

        double ringF = ((third % 30) + 10.0) / 40.0;
        double r     = _radarMaxR * ringF * 0.92;
        double angle = fourth / 254.0 * 360 - 90;
        double rad   = angle * Math.PI / 180;
        double bx    = _radarCx + r * Math.Cos(rad);
        double by    = _radarCy + r * Math.Sin(rad);

        bool online = device.Status.Contains("Online") || device.Status.Contains("🟢");
        var col = online
            ? System.Windows.Media.Color.FromRgb(0,  255, 100)
            : System.Windows.Media.Color.FromRgb(255, 60,  60);

        // Pulse ring — una sola volta (non Forever, evita accumulo animazioni)
        var pulse = new System.Windows.Shapes.Ellipse
        {
            Width = 18, Height = 18,
            Stroke = new System.Windows.Media.SolidColorBrush(col),
            StrokeThickness = 1.5, Opacity = 0,
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            RenderTransform = new System.Windows.Media.ScaleTransform(1, 1)
        };
        Canvas.SetLeft(pulse, bx - 9); Canvas.SetTop(pulse, by - 9);
        var scaleAnim = new System.Windows.Media.Animation.DoubleAnimation(1, 2.5,
            new Duration(TimeSpan.FromSeconds(0.8))) { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop };
        ((System.Windows.Media.ScaleTransform)pulse.RenderTransform).BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleAnim);
        ((System.Windows.Media.ScaleTransform)pulse.RenderTransform).BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
            new System.Windows.Media.Animation.DoubleAnimation(1, 2.5, new Duration(TimeSpan.FromSeconds(0.8))) { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop });
        pulse.BeginAnimation(UIElement.OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0.8, 0,
                new Duration(TimeSpan.FromSeconds(0.8))));
        _radarBlipLayer.Children.Add(pulse);

        // Core dot — statico, nessuna animazione né effetti costosi
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 7, Height = 7,
            Fill = new System.Windows.Media.SolidColorBrush(col),
            Opacity = 1
        };
        Canvas.SetLeft(dot, bx - 3.5); Canvas.SetTop(dot, by - 3.5);
        _radarBlipLayer.Children.Add(dot);

        // Label
        var lbl = new TextBlock
        {
            Text = device.Name != "—" ? device.Name : device.Ip,
            FontSize = 8, Opacity = 0,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(200, 0, 220, 80))
        };
        Canvas.SetLeft(lbl, bx + 7); Canvas.SetTop(lbl, by - 5);
        lbl.BeginAnimation(UIElement.OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 1,
                new Duration(TimeSpan.FromSeconds(0.5))));
        _radarBlipLayer.Children.Add(lbl);
    }

    // ── Mappa rete visuale ────────────────────────────────────────────────────
    private bool _mapVisible = false;

    private void BtnToggleMap_Click(object s, RoutedEventArgs e)
    {
        _mapVisible = !_mapVisible;
        MapPanel.Visibility = _mapVisible ? Visibility.Visible : Visibility.Collapsed;
        NetGrid.Visibility  = _mapVisible ? Visibility.Collapsed : Visibility.Visible;
        BtnToggleMap.Content = _mapVisible ? "☰  Lista" : "🗺  Mappa";
        if (_mapVisible) DrawNetworkMap();
    }

    private void DrawNetworkMap()
    {
        NetMapCanvas.Children.Clear();

        var devices = _netRows.ToList();
        if (devices.Count == 0) return;

        double cx = NetMapCanvas.Width  / 2;
        double cy = NetMapCanvas.Height / 2;

        // Disegna griglia di sfondo
        for (int x = 0; x < NetMapCanvas.Width;  x += 60)
        {
            var line = new System.Windows.Shapes.Line
            {
                X1 = x, Y1 = 0, X2 = x, Y2 = NetMapCanvas.Height,
                Stroke = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(20, 255, 255, 255)),
                StrokeThickness = 1
            };
            NetMapCanvas.Children.Add(line);
        }
        for (int y = 0; y < NetMapCanvas.Height; y += 60)
        {
            var line = new System.Windows.Shapes.Line
            {
                X1 = 0, Y1 = y, X2 = NetMapCanvas.Width, Y2 = y,
                Stroke = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(20, 255, 255, 255)),
                StrokeThickness = 1
            };
            NetMapCanvas.Children.Add(line);
        }

        // Trova il gateway (IP con .1 o .254)
        var gateway = devices.FirstOrDefault(d =>
            d.Ip.EndsWith(".1") || d.Ip.EndsWith(".254") ||
            d.DeviceType.Contains("Router") || d.DeviceType.Contains("Gateway"))
            ?? devices[0];

        // Gateway al centro
        DrawNode(cx, cy, gateway, isGateway: true);

        // Altri device in cerchi concentrici
        var others = devices.Where(d => d != gateway).ToList();
        int perRing  = Math.Min(12, others.Count);
        double ringR = Math.Min(cx, cy) * 0.65;

        for (int i = 0; i < others.Count; i++)
        {
            int    ring  = i / perRing;
            int    pos   = i % perRing;
            int    count = Math.Min(perRing, others.Count - ring * perRing);
            double r     = ringR + ring * 90;
            double angle = (2 * Math.PI * pos / count) - Math.PI / 2;
            double x     = cx + r * Math.Cos(angle);
            double y     = cy + r * Math.Sin(angle);

            // Linea di connessione verso il gateway
            bool online = others[i].Status.Contains("Online") || others[i].Status.Contains("🟢");
            var connLine = new System.Windows.Shapes.Line
            {
                X1 = cx, Y1 = cy, X2 = x, Y2 = y,
                Stroke = new System.Windows.Media.SolidColorBrush(
                    online
                    ? System.Windows.Media.Color.FromArgb(60, 16, 185, 129)
                    : System.Windows.Media.Color.FromArgb(30, 100, 116, 139)),
                StrokeThickness = online ? 1.5 : 1,
                StrokeDashArray = online ? null : new System.Windows.Media.DoubleCollection { 4, 4 }
            };
            NetMapCanvas.Children.Add(connLine);

            DrawNode(x, y, others[i], isGateway: false);
        }
    }

    private void DrawNode(double x, double y, DeviceRow device, bool isGateway)
    {
        bool online = device.Status.Contains("Online") || device.Status.Contains("🟢");

        var nodeColor = isGateway
            ? System.Windows.Media.Color.FromRgb(245, 158, 11)    // amber
            : online
                ? System.Windows.Media.Color.FromRgb(16, 185, 129) // green
                : System.Windows.Media.Color.FromRgb(239, 68, 68);  // red

        double r = isGateway ? 28 : 20;

        if (online)
        {
            // Anello pulse animato
            var pulse = new System.Windows.Shapes.Ellipse
            {
                Width = r * 2, Height = r * 2,
                Stroke = new System.Windows.Media.SolidColorBrush(nodeColor),
                StrokeThickness = 2,
                Opacity = 0.6,
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new System.Windows.Media.ScaleTransform(1, 1)
            };
            Canvas.SetLeft(pulse, x - r);
            Canvas.SetTop(pulse,  y - r);
            NetMapCanvas.Children.Add(pulse);

            var anim = new System.Windows.Media.Animation.DoubleAnimation(1, 2.2,
                new Duration(TimeSpan.FromSeconds(1.8)))
            {
                AutoReverse = false, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                EasingFunction = new System.Windows.Media.Animation.CubicEase()
            };
            var animO = new System.Windows.Media.Animation.DoubleAnimation(0.6, 0,
                new Duration(TimeSpan.FromSeconds(1.8)))
            {
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            };
            ((System.Windows.Media.ScaleTransform)pulse.RenderTransform).BeginAnimation(
                System.Windows.Media.ScaleTransform.ScaleXProperty, anim);
            ((System.Windows.Media.ScaleTransform)pulse.RenderTransform).BeginAnimation(
                System.Windows.Media.ScaleTransform.ScaleYProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1, 2.2,
                    new Duration(TimeSpan.FromSeconds(1.8)))
                {
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                    EasingFunction = new System.Windows.Media.Animation.CubicEase()
                });
            pulse.BeginAnimation(UIElement.OpacityProperty, animO);
        }

        // Cerchio principale
        var node = new System.Windows.Shapes.Ellipse
        {
            Width  = r * 2, Height = r * 2,
            Fill   = new System.Windows.Media.RadialGradientBrush(
                System.Windows.Media.Color.FromArgb(255,
                    (byte)Math.Min(255, nodeColor.R + 60),
                    (byte)Math.Min(255, nodeColor.G + 60),
                    (byte)Math.Min(255, nodeColor.B + 60)),
                nodeColor),
            Stroke = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(200, nodeColor.R, nodeColor.G, nodeColor.B)),
            StrokeThickness = 2,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color     = nodeColor,
                BlurRadius = 12, ShadowDepth = 0,
                Opacity   = online ? 0.8 : 0.3
            }
        };
        Canvas.SetLeft(node, x - r);
        Canvas.SetTop(node,  y - r);
        NetMapCanvas.Children.Add(node);

        // Icona
        var icon = new TextBlock
        {
            Text = isGateway ? "🌐" : device.Icon,
            FontSize = isGateway ? 16 : 13,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment   = System.Windows.VerticalAlignment.Center,
            IsHitTestVisible    = false
        };
        Canvas.SetLeft(icon, x - r + (r * 0.45));
        Canvas.SetTop(icon,  y - r + (r * 0.35));
        NetMapCanvas.Children.Add(icon);

        // Etichetta IP
        var label = new TextBlock
        {
            Text       = device.Name != "—" ? device.Name : device.Ip,
            FontSize   = 9,
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(148, 163, 184)),
            MaxWidth   = 80,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center
        };
        Canvas.SetLeft(label, x - 40);
        Canvas.SetTop(label,  y + r + 3);
        NetMapCanvas.Children.Add(label);

        // IP piccolo sotto il nome
        var ipLabel = new TextBlock
        {
            Text       = device.Ip,
            FontSize   = 8,
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(150, 100, 116, 139)),
            MaxWidth   = 80,
            TextAlignment = TextAlignment.Center
        };
        Canvas.SetLeft(ipLabel, x - 40);
        Canvas.SetTop(ipLabel,  y + r + 14);
        NetMapCanvas.Children.Add(ipLabel);

        // Tooltip al click
        node.ToolTip = $"{device.Ip}  {device.Name}\n{device.DeviceType}  {device.Mac}\n{device.Status}";
    }

    // ── IP Subnet Heatmap ─────────────────────────────────────────────────────
    private bool _heatmapVisible = false;

    private void BtnToggleHeatmap_Click(object s, RoutedEventArgs e)
    {
        _heatmapVisible = !_heatmapVisible;
        HeatmapPanel.Visibility = _heatmapVisible ? Visibility.Visible : Visibility.Collapsed;
        NetGrid.Visibility      = _heatmapVisible ? Visibility.Collapsed : Visibility.Visible;
        // Se la mappa è aperta, chiudila
        if (_heatmapVisible && _mapVisible)
        {
            _mapVisible = false;
            MapPanel.Visibility = Visibility.Collapsed;
            BtnToggleMap.Content = "🗺  Mappa";
        }
        BtnToggleHeatmap.Content = _heatmapVisible ? "☰  Lista" : "⬛  Heatmap";
        if (_heatmapVisible) DrawHeatmap();
    }

    private void DrawHeatmap()
    {
        HeatmapCanvas.Children.Clear();

        // Subnet base dai campi nella barra di scansione
        var baseText = TxtScanIp.Text.Trim();
        var parts    = baseText.Split('.');
        string prefix = parts.Length >= 3 ? $"{parts[0]}.{parts[1]}.{parts[2]}" : "192.168.10";

        // Costruisci dizionario IP → device
        var byIp = _netRows.ToDictionary(d => d.Ip, d => d);

        const int cols    = 16;
        const int rows    = 16;   // 256 celle: .0-.255
        const double cell = 46;
        const double gap  = 4;
        const double ox   = 10;
        const double oy   = 44;

        // Header colonne (0-15)
        for (int c = 0; c < cols; c++)
        {
            var hdr = new TextBlock
            {
                Text       = (c * rows).ToString(),
                FontSize   = 9,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(71, 85, 105)),
                Width = cell, TextAlignment = TextAlignment.Center
            };
            Canvas.SetLeft(hdr, ox + c * (cell + gap));
            Canvas.SetTop(hdr, oy - 18);
            HeatmapCanvas.Children.Add(hdr);
        }

        for (int i = 0; i < 256; i++)
        {
            int col = i % cols;
            int row = i / cols;

            string ip   = $"{prefix}.{i}";
            double x    = ox + col * (cell + gap);
            double y    = oy + row * (cell + gap);

            bool isOnline  = byIp.TryGetValue(ip, out var dev) &&
                             (dev.Status.Contains("Online") || dev.Status.Contains("🟢"));
            bool hasCert   = isOnline && dev!.CertStatus.Contains("✅");
            bool isGw      = ip.EndsWith(".1") || ip.EndsWith(".254");
            bool isScanned = byIp.ContainsKey(ip);

            var fill = hasCert   ? System.Windows.Media.Color.FromRgb(0x3b, 0x82, 0xf6)  // blue
                     : isGw && isOnline ? System.Windows.Media.Color.FromRgb(0xf5, 0x9e, 0x0b) // amber
                     : isOnline  ? System.Windows.Media.Color.FromRgb(0x10, 0xb9, 0x81)  // green
                     : isScanned ? System.Windows.Media.Color.FromRgb(0x1e, 0x2e, 0x55)  // dark blue (offline)
                                 : System.Windows.Media.Color.FromRgb(0x0d, 0x14, 0x28); // very dark (unknown)

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width  = cell, Height = cell,
                Fill   = new System.Windows.Media.SolidColorBrush(fill),
                RadiusX = 3, RadiusY = 3,
                Opacity = isOnline ? 1.0 : 0.7
            };

            if (isOnline)
                rect.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color     = fill,
                    BlurRadius = 8, ShadowDepth = 0, Opacity = 0.5
                };

            rect.ToolTip = isScanned && dev != null
                ? $"{ip}  —  {dev.Name}\n{dev.DeviceType}  {dev.Mac}\n{dev.Status}"
                : $"{ip}  —  non scansionato";

            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            HeatmapCanvas.Children.Add(rect);

            // Label IP (ultimo ottetto)
            var lbl = new TextBlock
            {
                Text       = i.ToString(),
                FontSize   = 8,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                Foreground = new System.Windows.Media.SolidColorBrush(
                    isOnline
                    ? System.Windows.Media.Color.FromRgb(226, 232, 240)
                    : System.Windows.Media.Color.FromRgb(71, 85, 105)),
                Width = cell, TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(lbl, x);
            Canvas.SetTop(lbl, y + (cell / 2) - 6);
            HeatmapCanvas.Children.Add(lbl);

            // Icona per device online
            if (isOnline && dev != null)
            {
                var ico = new TextBlock
                {
                    Text     = dev.Icon,
                    FontSize = 11,
                    IsHitTestVisible = false,
                    Width = cell, TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(ico, x);
                Canvas.SetTop(ico, y + 4);
                HeatmapCanvas.Children.Add(ico);
            }
        }
    }

    // ── Matrix Rain (About tab) ───────────────────────────────────────────────
    private System.Windows.Threading.DispatcherTimer? _matrixTimer;
    private readonly Random _matrixRng = new();
    private int[]?   _matrixHead;   // posizione Y della "testa" di ogni colonna
    private double[] _matrixX = [];
    private const int MatrixCharPx = 16;
    private const string MatrixChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789アイウエオカキクケコ<>{}[]|/\\";

    private void StartMatrixRain()
    {
        if (_matrixTimer?.IsEnabled == true) return;

        MatrixCanvas.Children.Clear();
        double w = MatrixCanvas.ActualWidth;
        double h = MatrixCanvas.ActualHeight;
        if (w < 10 || h < 10) return;

        int numCols = (int)(w / MatrixCharPx);
        _matrixHead = new int[numCols];
        _matrixX    = new double[numCols];

        for (int i = 0; i < numCols; i++)
        {
            _matrixX[i]    = i * MatrixCharPx;
            _matrixHead[i] = -_matrixRng.Next(1, (int)(h / MatrixCharPx) + 5);
        }

        _matrixTimer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(55) };
        _matrixTimer.Tick += MatrixTick;
        _matrixTimer.Start();
    }

    private void StopMatrixRain()
    {
        _matrixTimer?.Stop();
        _matrixTimer = null;
        MatrixCanvas.Children.Clear();
        _matrixHead = null;
    }

    private void MatrixTick(object? sender, EventArgs e)
    {
        if (_matrixHead == null) return;
        double h = MatrixCanvas.ActualHeight;
        int maxRow = (int)(h / MatrixCharPx) + 2;

        // Rimuovi i vecchi TextBlock (ridisegniamo tutto)
        MatrixCanvas.Children.Clear();

        for (int col = 0; col < _matrixHead.Length; col++)
        {
            int head = _matrixHead[col];
            double x = _matrixX[col];

            // Disegna la colonna
            for (int row = Math.Max(0, head - 20); row <= head; row++)
            {
                if (row < 0) continue;
                double y = row * MatrixCharPx;
                if (y > h) break;

                double dist    = head - row;
                byte   alpha   = dist == 0 ? (byte)255
                               : dist <= 3  ? (byte)(200 - dist * 40)
                               : (byte)Math.Max(20, 120 - dist * 8);
                byte   green   = dist == 0 ? (byte)255 : (byte)(180 - dist * 6);
                var    color   = dist == 0
                    ? System.Windows.Media.Color.FromRgb(200, 255, 200)   // bianco-verde (testa)
                    : System.Windows.Media.Color.FromArgb(alpha, 0, (byte)Math.Max(80, (int)green), 0);

                var ch = new TextBlock
                {
                    Text       = MatrixChars[_matrixRng.Next(MatrixChars.Length)].ToString(),
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize   = MatrixCharPx - 2,
                    Foreground = new System.Windows.Media.SolidColorBrush(color),
                    Width      = MatrixCharPx,
                    TextAlignment = TextAlignment.Center,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(ch, x);
                Canvas.SetTop(ch,  y);
                MatrixCanvas.Children.Add(ch);
            }

            // Avanza la testa
            _matrixHead[col]++;
            if (_matrixHead[col] > maxRow + 5)
                _matrixHead[col] = -_matrixRng.Next(5, 20);
        }
    }

    // ── Helpers scansione ─────────────────────────────────────────────────────
    private static List<IPAddress> GetHostsInSubnet(IPAddress baseIp, int cidr)
    {
        var bytes = baseIp.GetAddressBytes();
        uint ipInt = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) |
                     ((uint)bytes[2] << 8)  |  (uint)bytes[3];
        uint mask  = cidr == 0 ? 0 : 0xFFFFFFFF << (32 - cidr);
        uint net   = ipInt & mask;
        uint bcast = net | ~mask;
        var list   = new List<IPAddress>();
        for (uint i = net + 1; i < bcast; i++)
            list.Add(new IPAddress(new[]
            {
                (byte)(i >> 24), (byte)(i >> 16), (byte)(i >> 8), (byte)i
            }));
        return list;
    }

    private static string GetMacFromArp(string ip)
    {
        try
        {
            // Forza una connessione UDP per popolare ARP
            try
            {
                using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                s.Connect(ip, 1);
            }
            catch { }

            var startInfo = new ProcessStartInfo("arp")
            {
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-a");
            startInfo.ArgumentList.Add(ip);
            var p = Process.Start(startInfo);
            if (p == null) return "—";
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(1000);

            var match = Regex.Match(output,
                @"([0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2})");
            if (match.Success)
                return match.Value.Replace("-", ":").ToUpper();
        }
        catch { }
        return "—";
    }

    // Database OUI essenziale (prefix → vendor)
    // ── Database OUI completo (maclookup.app) ────────────────────────────────
    private static Dictionary<string, string>? _ouiDb;
    private static readonly string OuiDbPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "PolarisManager", "oui.json");

    // maclookup.app — database più completo (~7MB JSON, 50k+ record)
    // Gist aallan — fallback (~23k record, formato testo semplice)
    private const string OuiJsonUrl = "https://maclookup.app/downloads/json-database/get-db";
    private const string OuiTxtUrl  =
        "https://gist.githubusercontent.com/aallan/b4bb86db86079509e6159810ae9bd3e4/raw/mac-vendor.txt";

    private static async Task LoadOuiDatabaseAsync()
    {
        try
        {
            bool needDownload = !File.Exists(OuiDbPath) ||
                (DateTime.Now - File.GetLastWriteTime(OuiDbPath)).TotalDays > 30;

            if (needDownload)
            {
                App.Log("[OUI] Download database MAC (maclookup.app)...");
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(90) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("NovaSCM/0.1.0");

                string content;
                bool   isJson;
                try
                {
                    content = await http.GetStringAsync(OuiJsonUrl);
                    isJson  = true;
                    App.Log("[OUI] Download completato (JSON)");
                }
                catch
                {
                    App.Log("[OUI] maclookup.app non raggiungibile, uso Gist fallback...");
                    content = await http.GetStringAsync(OuiTxtUrl);
                    isJson  = false;
                    App.Log("[OUI] Download completato (TXT)");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(OuiDbPath)!);
                // Salva con prefisso per sapere il formato al prossimo avvio
                File.WriteAllText(OuiDbPath, (isJson ? "JSON\n" : "TXT\n") + content);
            }

            _ouiDb = ParseOuiFile();
            App.Log($"[OUI] Database caricato: {_ouiDb.Count} record");
        }
        catch (Exception ex) { App.Log($"[OUI] Errore: {ex.Message}"); }
    }

    private static Dictionary<string, string> ParseOuiFile()
    {
        var lines  = File.ReadAllLines(OuiDbPath);
        var isJson = lines.Length > 0 && lines[0].Trim() == "JSON";
        var db     = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (isJson)
        {
            // JSON: array di {macPrefix, vendorName}
            var json    = string.Join('\n', lines.Skip(1));
            var records = JsonSerializer.Deserialize<List<OuiRecord>>(json);
            if (records != null)
                foreach (var r in records)
                    if (!string.IsNullOrEmpty(r.MacPrefix) && !string.IsNullOrEmpty(r.VendorName))
                        db.TryAdd(r.MacPrefix.Replace(":", "").ToUpperInvariant(), r.VendorName);
        }
        else
        {
            // TXT: "XXXXXX VendorName" per riga
            foreach (var line in lines.Skip(1))
            {
                if (line.Length < 8 || line.StartsWith('#')) continue;
                var sep = line.IndexOf(' ');
                if (sep < 6) continue;
                var prefix = line[..6].ToUpperInvariant();
                var vendor = line[(sep + 1)..].Trim();
                if (!string.IsNullOrEmpty(vendor))
                    db.TryAdd(prefix, vendor);
            }
        }

        return db;
    }

    private static string LookupVendor(string mac)
    {
        if (mac == "—" || mac.Length < 8) return "—";

        var raw    = mac.Replace(":", "").Replace("-", "").ToUpperInvariant();
        if (raw.Length < 6) return "—";
        var prefix = raw[..6];

        // Se bit "locally administered" impostato → prova prima con bit azzerato
        bool laSet = byte.TryParse(prefix[..2], System.Globalization.NumberStyles.HexNumber,
                         null, out var b0) && (b0 & 0x02) != 0;
        if (laSet)
        {
            var basePrefix = (b0 & ~0x02).ToString("X2") + prefix[2..];
            var r = LookupPrefix(basePrefix);
            if (r != "—") return r;
            return "🔒 Privacy MAC";
        }

        return LookupPrefix(prefix);
    }

    private static string LookupPrefix(string prefix6)
    {
        if (_ouiDb != null && _ouiDb.TryGetValue(prefix6, out var v)) return v;
        return "—";
    }

    // MAC del PC locale (arp -a non mostra se stesso)
    private static string GetLocalMacForIp(string ip)
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.GetIPProperties().UnicastAddresses
                       .Any(a => a.Address.ToString() == ip))
                {
                    var raw = ni.GetPhysicalAddress().ToString(); // "10FFE0B3CEEB"
                    if (raw.Length == 12)
                        return string.Join(":", Enumerable.Range(0, 6)
                            .Select(i => raw.Substring(i * 2, 2))).ToUpper();
                }
            }
        }
        catch { }
        return "—";
    }

    // Cache vendor online per evitare chiamate duplicate
    private static readonly Dictionary<string, string> _vendorCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly System.Net.Http.HttpClient _vendorHttp  = new() { Timeout = TimeSpan.FromSeconds(4) };

    private static async Task<string> LookupVendorOnlineAsync(string mac)
    {
        var prefix = mac.Replace(":", "").Replace("-", "")[..6].ToUpper();
        lock (_vendorCache)
            if (_vendorCache.TryGetValue(prefix, out var cached)) return cached;
        try
        {
            var vendor = (await _vendorHttp.GetStringAsync(
                $"https://api.macvendors.com/{prefix}")).Trim();
            if (!string.IsNullOrEmpty(vendor) && !vendor.StartsWith("{"))
            {
                lock (_vendorCache) _vendorCache[prefix] = vendor;
                return vendor;
            }
        }
        catch { }
        return "—";
    }

    // Scansione porta veloce per rilevazione tipo device
    private static async Task<bool> QuickPortOpenAsync(string ip, int port, int timeoutMs)
    {
        try
        {
            using var tcp = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);
            await tcp.ConnectAsync(ip, port, cts.Token);
            return true;
        }
        catch { return false; }
    }

    // Tipo connessione per IP locali (stesso PC)
    private static string GetLocalConnectionType(string ip)
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (!ni.GetIPProperties().UnicastAddresses.Any(a => a.Address.ToString() == ip)) continue;
                return ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                    ? "📶 WiFi"
                    : "🔌 LAN";
            }
        }
        catch { }
        return "❓";
    }

    // Arricchisce i device con tipo connessione da UniFi API
    private async Task EnrichConnectionTypeFromUnifiAsync()
    {
        var url  = _config.UnifiUrl.TrimEnd('/');
        var user = _config.UnifiUser;
        var pass = _config.UnifiPass;

        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
        {
            App.Log("[UniFi] Credenziali non configurate — skip connessione tipo");
            return;
        }

        App.Log($"[UniFi] Connessione a {url} con utente {user}");
        try
        {
            var handler = new System.Net.Http.HttpClientHandler
            {
                // Accetta solo UntrustedRoot (self-signed); blocca scaduti, revocati, hostname errato
                ServerCertificateCustomValidationCallback = (_, _, chain, errors) =>
                    errors == System.Net.Security.SslPolicyErrors.None ||
                    (errors == System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors &&
                     chain?.ChainStatus.Length == 1 &&
                     chain.ChainStatus[0].Status == System.Security.Cryptography.X509Certificates.X509ChainStatusFlags.UntrustedRoot)
            };
            using var http = new System.Net.Http.HttpClient(handler)
                { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(12) };

            // Login
            var loginBody = JsonSerializer.Serialize(new { username = user, password = pass });
            var loginResp = await http.PostAsync("/api/auth/login",
                new System.Net.Http.StringContent(loginBody, System.Text.Encoding.UTF8, "application/json"));
            App.Log($"[UniFi] Login status: {(int)loginResp.StatusCode}");
            if (!loginResp.IsSuccessStatusCode) return;

            if (loginResp.Headers.TryGetValues("x-csrf-token", out var csrf))
                http.DefaultRequestHeaders.TryAddWithoutValidation("x-csrf-token", csrf.First());

            // MAC → (isWired, ssid, ip): legge SIA wireless (stat/sta) SIA cablati (stat/alluser)
            var clientMap = new Dictionary<string, (bool wired, string ssid, string ip)>(StringComparer.OrdinalIgnoreCase);

            foreach (var endpoint in new[] { "/proxy/network/api/s/default/stat/sta",
                                             "/proxy/network/api/s/default/stat/alluser" })
            {
                var resp = await http.GetAsync(endpoint);
                App.Log($"[UniFi] {endpoint} → {(int)resp.StatusCode}");
                if (!resp.IsSuccessStatusCode) continue;

                var doc  = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                if (!doc.RootElement.TryGetProperty("data", out var data)) continue;

                foreach (var c in data.EnumerateArray())
                {
                    var mac     = c.TryGetProperty("mac",      out var m) ? m.GetString() ?? "" : "";
                    var isWired = c.TryGetProperty("is_wired", out var w) && w.GetBoolean();
                    var ssid    = c.TryGetProperty("essid",    out var es) ? es.GetString() ?? "" : "";
                    var clientIp= c.TryGetProperty("ip",       out var ip) ? ip.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(mac) && !clientMap.ContainsKey(mac.ToUpper()))
                        clientMap[mac.ToUpper()] = (isWired, ssid, clientIp);
                }
            }

            App.Log($"[UniFi] Totale client mappa: {clientMap.Count}");

            // Mappa IP → MAC per riempire MAC mancanti su altre VLAN
            var ipToMac = clientMap
                .Where(kv => !string.IsNullOrEmpty(kv.Value.ip))
                .ToDictionary(kv => kv.Value.ip, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

            // Applica ai DeviceRow
            Dispatcher.Invoke(() =>
            {
                int updated = 0;
                foreach (var row in _netRows)
                {
                    // Riempi MAC mancante tramite IP (funziona anche per altri VLAN)
                    if (row.Mac == "—" && ipToMac.TryGetValue(row.Ip, out var unifiMac))
                    {
                        var macClean   = unifiMac.Replace(":", "").Replace("-", "").ToUpper();
                        var normMacFmt = string.Join(":", Enumerable.Range(0, 6)
                            .Select(i => macClean.Substring(i * 2, 2)));
                        row.Mac    = normMacFmt;
                        row.Vendor = LookupVendor(normMacFmt);
                        App.Log($"[UniFi] MAC risolto via IP: {row.Ip} → {normMacFmt}");
                    }

                    // ConnectionType
                    if (row.ConnectionType != "❓") continue; // già noto (locale)
                    var normMac = row.Mac.Replace(":", "").Replace("-", "").ToUpper();
                    var key = clientMap.Keys.FirstOrDefault(k =>
                        k.Replace(":", "").Replace("-", "")
                         .Equals(normMac, StringComparison.OrdinalIgnoreCase));
                    if (key == null) continue;
                    var (wired, ssid, _) = clientMap[key];
                    row.ConnectionType = wired ? "🔌 LAN"
                        : string.IsNullOrEmpty(ssid) ? "📶 WiFi" : $"📶 {ssid}";
                    updated++;
                }
                App.Log($"[UniFi] Device aggiornati: {updated}");
            });
        }
        catch (Exception ex) { App.Log($"[UniFi] EnrichConnection errore: {ex.Message}"); }
    }

    // ── Dati demo (altri tab) ─────────────────────────────────────────────────
    private void LoadFromDatabase()
    {
        // ── Certificati ──────────────────────────────────────────────────────
        var certs = Database.GetCerts();
        if (certs.Count == 0)
        {
            // Prima esecuzione: popola con dati demo e salva nel DB
            certs =
            [
                new("💻","PC-OFFICE-01","AA:BB:CC:11:22:33","2026-01-15","2036-01-15","✅ Attivo"),
                new("💻","PC-OFFICE-02","AA:BB:CC:11:22:34","2026-01-15","2036-01-15","✅ Attivo"),
                new("📱","Smartphone-1","AA:BB:CC:11:22:35","2026-02-01","2036-02-01","✅ Attivo"),
                new("💻","LAPTOP-01",   "AA:BB:CC:11:22:36","2026-03-01","2036-03-01","✅ Attivo"),
                new("💻","PC-VECCHIO",  "AA:BB:CC:11:22:37","2025-06-01","2035-06-01","⏸ Revocato"),
            ];
            foreach (var c in certs) Database.UpsertCert(c);
        }
        CertGrid.ItemsSource = new ObservableCollection<CertRow>(certs);

        // ── App Queue ─────────────────────────────────────────────────────────
        var queue = Database.GetAppQueue();
        if (queue.Count == 0)
        {
            queue =
            [
                new("💻 pc-office-01","192.168.1.101","AA:BB:CC:11:22:33","VLC, Firefox","⏳ In installazione"),
                new("💻 pc-office-02","192.168.1.102","AA:BB:CC:11:22:34","—",           "✅ Aggiornato"),
                new("💻 laptop-01",   "192.168.1.103","AA:BB:CC:11:22:36","7-Zip",       "⏳ In attesa"),
            ];
            foreach (var q in queue) Database.UpsertAppQueue(q);
        }
        AppQueueGrid.ItemsSource = new ObservableCollection<AppQueueRow>(queue);

        // ── Catalogo App (statico) ─────────────────────────────────────────────
        AppCatalog.ItemsSource = new[]
        {
            new AppCatRow("🌐 Browser",  "Firefox   │   Chrome   │   Brave   │   Edge"),
            new AppCatRow("📄 Office",   "LibreOffice   │   OnlyOffice   │   Notepad++"),
            new AppCatRow("🎬 Media",    "VLC   │   Spotify   │   MPC-HC   │   Kodi"),
            new AppCatRow("🔧 Utility",  "7-Zip   │   WinRAR   │   Everything   │   TreeSize"),
            new AppCatRow("💻 Dev",      "VS Code   │   Git   │   Python   │   Node.js"),
            new AppCatRow("⭐ Mie App",  "Pioneer MCACC   │   Custom App 1"),
        };

        // ── OPSI ──────────────────────────────────────────────────────────────
        var opsi = Database.GetOpsi();
        if (opsi.Count == 0)
        {
            opsi =
            [
                new("firefox",       "132.0", "✅ OK",        "2026-03-01"),
                new("vlc",           "3.0.21","✅ OK",        "2026-02-28"),
                new("pioneer-mcacc", "1.0.0", "✅ OK",        "2026-03-04"),
                new("chrome",        "124.0", "⚠️ Aggiorna","2026-01-15"),
                new("7zip",          "24.08", "✅ OK",        "2026-03-02"),
            ];
            foreach (var o in opsi) Database.UpsertOpsi(o);
        }
        OpsiGrid.ItemsSource = new ObservableCollection<OpsiRow>(opsi);

        // ── PC Gestiti ────────────────────────────────────────────────────────
        var pcs = Database.GetPcs();
        if (pcs.Count == 0)
        {
            pcs =
            [
                new("💻","PC-OFFICE-01","192.168.1.101","Win 11 Pro", "12%","8.2/32 GB","🟢 Online","✅"),
                new("💻","PC-OFFICE-02","192.168.1.102","Win 11 Pro", "4%", "4.1/32 GB","🟢 Online","✅"),
                new("💻","LAPTOP-01",   "192.168.1.103","Win 11 Home","8%", "6.3/16 GB","🟢 Online","✅"),
                new("💻","PC-VECCHIO",  "—",            "Win 10 Pro", "—",  "—",        "🔴 Offline","⚠️"),
            ];
            foreach (var p in pcs) Database.UpsertPc(p);
        }
        PcGrid.ItemsSource = new ObservableCollection<PcRow>(pcs);

        // ── Dispositivi rete (ultima scansione) ───────────────────────────────
        var devices = Database.GetDevices();
        foreach (var d in devices)
        {
            d.WasOnline = d.Status.Contains("Online");
            _netRows.Add(d);
        }
    }

    // ── Handler pulsanti ──────────────────────────────────────────────────────
    private async void BtnRegisterDevices_Click(object s, RoutedEventArgs e)
    {
        try
        {
        var selected = NetGrid.SelectedItems.Cast<DeviceRow>().ToList();
        if (selected.Count == 0)
        {
            SetStatus("⚠️ Seleziona uno o più device dalla lista (Ctrl+click per multi-selezione)");
            return;
        }

        // Controlla che tutti i device abbiano il MAC risolto
        var senzaMac = selected.Where(d => d.Mac == "—").ToList();
        if (senzaMac.Count > 0)
        {
            var ips = string.Join(", ", senzaMac.Select(d => d.Ip));
            MessageBox.Show(
                $"MAC non ancora risolto per:\n{ips}\n\n" +
                "Attendi qualche secondo dopo la scansione che l'ARP si aggiorni, poi riprova.",
                "MAC mancante", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var list = string.Join("\n", selected.Select(d =>
            $"  • {d.Mac}  {(d.Name != "—" ? d.Name : d.Ip)}  ({d.Ip})"));

        var r = MessageBox.Show(
            $"Registrare {selected.Count} device sul server tramite MAC?\n\n{list}\n\n" +
            "Diventeranno disponibili per la gestione App e OPSI.",
            "Registra device", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        SetStatus($"⏳ Registrazione {selected.Count} device in corso...");
        App.Log($"Registrazione {selected.Count} device");

        int ok = 0, fail = 0;
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        foreach (var dev in selected)
        {
            try
            {
                // MAC come chiave primaria — l'IP è solo informativo
                var macClean = dev.Mac.Replace(":", "").Replace("-", "").ToUpper();
                var payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    mac      = macClean,
                    ip       = dev.Ip,
                    hostname = dev.Name != "—" ? dev.Name : "",
                    vendor   = dev.Vendor != "—" ? dev.Vendor : "",
                    source   = "PolarisManager"
                });
                var content = new System.Net.Http.StringContent(
                    payload, System.Text.Encoding.UTF8, "application/json");

                var resp = await http.PostAsync(
                    _config.CertportalUrl + "/api/register", content);

                if (resp.IsSuccessStatusCode)
                {
                    ok++;
                    dev.CertStatus = "📋 Registrato";
                    App.Log($"  OK: {dev.Mac} ({dev.Ip})");
                }
                else
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    fail++;
                    App.Log($"  FAIL {dev.Mac}: HTTP {(int)resp.StatusCode} — {body}");
                }
            }
            catch (Exception ex)
            {
                fail++;
                App.Log($"  FAIL {dev.Mac}: {ex.Message}");
            }
        }

        var msg = fail == 0
            ? $"✅ {ok} device registrati sul server"
            : $"⚠️ {ok} OK, {fail} falliti — controlla il log";
        SetStatus(msg);
        MessageBox.Show(msg, "Registrazione completata",
            MessageBoxButton.OK,
            fail == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex) { App.Log($"[BtnRegisterDevices_Click] {ex.Message}"); SetStatus($"❌ {ex.Message}"); }
    }

    private void BtnGenerateCert_Click(object s, RoutedEventArgs e)
    {
        if (NetGrid.SelectedItem is DeviceRow d)
            SetStatus($"🔐 Generazione cert per {d.Name} ({d.Ip})... (demo)");
        else
            SetStatus("⚠️ Seleziona un device dalla lista");
    }

    private void BtnQrNet_Click(object s, RoutedEventArgs e)
    {
        if (NetGrid.SelectedItem is DeviceRow d)
            ShowQr(d.Name == "—" ? d.Ip : d.Name, d.Mac);
        else
            ShowQr("Enrollment WiFi", "");
    }

    private void BtnQrCert_Click(object s, RoutedEventArgs e)
    {
        if (CertGrid.SelectedItem is CertRow c) ShowQr(c.Name, c.Mac);
    }

    private void NetGrid_DoubleClick(object s, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (NetGrid.SelectedItem is DeviceRow dev)
        {
            var dlg = new DeviceDetailWindow(dev, _config.CertportalUrl) { Owner = this };
            dlg.PortsScanned += ports => dev.DetectType(ports);
            dlg.ShowDialog();
        }
    }

    private async void BtnMonitor_Click(object s, RoutedEventArgs e)
    {
        try
        {
        if (_monitoring)
        {
            _monitorCts?.Cancel();
            _monitoring = false;
            BtnMonitor.Content = "👁  Monitora";
            SetStatus("⏹ Monitoraggio fermato");
            return;
        }

        if (!IPAddress.TryParse(TxtScanIp.Text.Trim(), out var baseIp) ||
            !int.TryParse(TxtScanSubnet.Text.Trim(), out int cidr))
        {
            SetStatus("⚠️ Configura prima IP e subnet"); return;
        }

        _monitoring = true;
        _monitorCts = new CancellationTokenSource();
        BtnMonitor.Content = "⏹  Stop Monitor";
        SetStatus("👁 Monitoraggio attivo — scansione ogni 30s");

        var token = _monitorCts.Token;
        var known = new Dictionary<string, bool>(); // IP → wasOnline

        await Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                var ips  = GetHostsInSubnet(baseIp, cidr);
                var sem  = new SemaphoreSlim(50);
                var current = new Dictionary<string, bool>();

                await Task.WhenAll(ips.Select(async ip =>
                {
                    await sem.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        using var ping = new Ping();
                        var r = await ping.SendPingAsync(ip, 600).ConfigureAwait(false);
                        current[ip.ToString()] = r.Status == IPStatus.Success;
                    }
                    catch { current[ip.ToString()] = false; }
                    finally { sem.Release(); }
                }));

                foreach (var kv in current)
                {
                    bool wasKnown = known.TryGetValue(kv.Key, out bool wasOn);
                    if (kv.Value && (!wasKnown || !wasOn))
                    {
                        // Nuovo device o tornato online
                        var ip = kv.Key;
                        Dispatcher.Invoke(() =>
                        {
                            var existing = _netRows.FirstOrDefault(r => r.Ip == ip);
                            if (existing == null)
                            {
                                _netRows.Add(new DeviceRow { Ip = ip, Status = "🟢 Online", Icon = "❓" });
                                SetStatus($"🆕 Nuovo device: {ip}");
                                App.Log($"[Monitor] Nuovo device: {ip}");
                                ShowToast("🆕 Nuovo device", $"Rilevato nuovo device in rete: {ip}");
                            }
                            else
                            {
                                existing.Status = "🟢 Online";
                                SetStatus($"🟢 Tornato online: {ip}");
                                ShowToast("🟢 Device Online", $"{existing.Name} ({ip}) è tornato online");
                            }
                        });
                    }
                    else if (!kv.Value && wasKnown && wasOn)
                    {
                        var ip = kv.Key;
                        Dispatcher.Invoke(() =>
                        {
                            var existing = _netRows.FirstOrDefault(r => r.Ip == ip);
                            if (existing != null)
                            {
                                existing.Status = "🔴 Offline";
                                SetStatus($"🔴 Offline: {ip}");
                                App.Log($"[Monitor] Offline: {ip}");
                                ShowToast("🔴 Device Offline", $"{existing.Name} ({ip}) non risponde");
                            }
                        });
                    }
                }

                known = current;
                await Task.Delay(30000, token).ConfigureAwait(false);
            }
        }, token).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnCanceled);

        if (!token.IsCancellationRequested) return;
        _monitoring = false;
        Dispatcher.Invoke(() => BtnMonitor.Content = "👁  Monitora");
        }
        catch (Exception ex) { App.Log($"[BtnMonitor_Click] {ex.Message}"); SetStatus($"❌ {ex.Message}"); }
    }

    private void BtnRefresh_Click(object s, RoutedEventArgs e) =>
        SetStatus("🔄 Clicca Scansiona per aggiornare i device in rete");

    private void BtnNewCert_Click(object s, RoutedEventArgs e) =>
        SetStatus("✨ Seleziona device e genera certificato... (demo)");

    private void BtnRevoke_Click(object s, RoutedEventArgs e)
    {
        if (CertGrid.SelectedItem is CertRow c)
        {
            var r = MessageBox.Show(
                $"Revocare il certificato di {c.Name}?\n\nIl device non potrà più connettersi a {_config.Ssid}.",
                "Conferma revoca", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r == MessageBoxResult.Yes)
                SetStatus($"⏸ Certificato {c.Name} revocato (demo)");
        }
        else SetStatus("⚠️ Seleziona un certificato");
    }

    private void BtnInstallApp_Click(object s, RoutedEventArgs e) =>
        SetStatus("📦 Seleziona app e PC target... (demo)");

    private void BtnUploadApp_Click(object s, RoutedEventArgs e) =>
        SetStatus("📤 Upload installer custom... (demo)");

    private void BtnClearQueue_Click(object s, RoutedEventArgs e) =>
        SetStatus("🗑 Coda svuotata (demo)");

    private void BtnOpsiCreate_Click(object s, RoutedEventArgs e) =>
        SetStatus("🚀 Wizard creazione pacchetto OPSI... (demo)");

    private void BtnOpsiUpdate_Click(object s, RoutedEventArgs e)
    {
        if (OpsiGrid.SelectedItem is OpsiRow p) SetStatus($"⬆ Aggiornamento {p.Name}... (demo)");
        else SetStatus("⚠️ Seleziona un pacchetto");
    }

    private void BtnOpsiDelete_Click(object s, RoutedEventArgs e)
    {
        if (OpsiGrid.SelectedItem is OpsiRow p)
        {
            if (MessageBox.Show($"Eliminare {p.Name}?", "Conferma",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                SetStatus($"🗑 {p.Name} eliminato (demo)");
        }
        else SetStatus("⚠️ Seleziona un pacchetto");
    }

    private void BtnRunScript_Click(object s, RoutedEventArgs e)
    {
        if (PcGrid.SelectedItem is PcRow p && p.Status.Contains("Online"))
            SetStatus($"📜 Script inviato a {p.Name}... (demo)");
        else SetStatus("⚠️ Seleziona un PC online");
    }

    private void BtnRdp_Click(object s, RoutedEventArgs e)
    {
        if (PcGrid.SelectedItem is PcRow p && p.Ip != "—")
        {
            var mstscInfo = new ProcessStartInfo("mstsc");
            mstscInfo.ArgumentList.Add($"/v:{p.Ip}");
            Process.Start(mstscInfo);
            SetStatus($"🖥️ RDP verso {p.Name} ({p.Ip})");
        }
        else SetStatus("⚠️ Seleziona un PC online con IP valido");
    }

    // ── Menu contestuale DataGrid ─────────────────────────────────────────────
    private void MenuCopyIp_Click(object s, RoutedEventArgs e)
    {
        if (NetGrid.SelectedItem is DeviceRow r)
        {
            Clipboard.SetText(r.Ip);
            SetStatus($"📋 IP copiato: {r.Ip}");
        }
    }

    private void MenuCopyMac_Click(object s, RoutedEventArgs e)
    {
        if (NetGrid.SelectedItem is DeviceRow r && r.Mac != "—")
        {
            Clipboard.SetText(r.Mac);
            SetStatus($"📋 MAC copiato: {r.Mac}");
        }
    }

    private void MenuCopyBoth_Click(object s, RoutedEventArgs e)
    {
        if (NetGrid.SelectedItem is DeviceRow r)
        {
            var text = $"{r.Ip}\t{r.Mac}";
            Clipboard.SetText(text);
            SetStatus($"📋 Copiato: {r.Ip}  {r.Mac}");
        }
    }

    private void MenuDetails_Click(object s, RoutedEventArgs e)
    {
        if (NetGrid.SelectedItem is DeviceRow r)
        {
            var win = new DeviceDetailWindow(r, _config.CertportalUrl) { Owner = this };
            win.PortsScanned += ports => r.DetectType(ports);
            win.Show();
        }
    }

    private void BtnInventory_Click(object s, RoutedEventArgs e)
    {
        // Funziona sia con PC dal tab PC sia con device selezionato in NetGrid
        string name, ip;
        if (PcGrid.SelectedItem is PcRow pc && pc.Ip != "—")
        {
            name = pc.Name;
            ip   = pc.Ip;
        }
        else if (NetGrid.SelectedItem is DeviceRow dev)
        {
            name = dev.Name != "—" ? dev.Name : dev.Ip;
            ip   = dev.Ip;
        }
        else
        {
            SetStatus("⚠️ Seleziona un PC dalla tabella");
            return;
        }
        new InventoryWindow(name, ip, _config.AdminUser, _config.AdminPass)
            { Owner = this }.Show();
        SetStatus($"📊 Inventario avviato per {name} ({ip})");
    }

    private void BtnUpdateAgent_Click(object s, RoutedEventArgs e) =>
        SetStatus("🔄 Aggiornamento agent... (demo)");

    // ═══════════════════════════════════════════════════════════════
    //  TAB SCCM
    // ═══════════════════════════════════════════════════════════════

    private System.Net.Http.HttpClient? _sccmClient;
    private string _sccmBase = "";
    private string _sccmCurrentSection = "devices";

    public class SccmItem
    {
        public string Name        { get; set; } = "";
        public string Type        { get; set; } = "";
        public string Status      { get; set; } = "";
        public string LastContact { get; set; } = "";
        public string Info        { get; set; } = "";
    }

    private async void BtnSccmConnect_Click(object s, RoutedEventArgs e)
    {
        var server = TxtSccmServer.Text.Trim();
        var userFull = TxtSccmUser.Text.Trim();
        var pass   = TxtSccmPass.Password;

        var parts  = userFull.Split('\\');
        var user   = parts.Length == 2 ? parts[1] : userFull;
        var domain = parts.Length == 2 ? parts[0] : "";

        _sccmBase = $"https://{server}/AdminService/wmi/";

        var handler = new System.Net.Http.HttpClientHandler
        {
            Credentials = new System.Net.NetworkCredential(user, pass, domain),
            // Accetta solo UntrustedRoot (self-signed); blocca scaduti, revocati, hostname errato
            ServerCertificateCustomValidationCallback = (_, _, chain, errors) =>
                errors == System.Net.Security.SslPolicyErrors.None ||
                (errors == System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors &&
                 chain?.ChainStatus.Length == 1 &&
                 chain.ChainStatus[0].Status == System.Security.Cryptography.X509Certificates.X509ChainStatusFlags.UntrustedRoot)
        };
        _sccmClient?.Dispose();
        _sccmClient = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        _sccmClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        TxtSccmConnStatus.Text = "Connessione...";
        TxtSccmConnStatus.Foreground = System.Windows.Media.Brushes.Gray;

        try
        {
            var resp = await _sccmClient.GetAsync(_sccmBase + "SMS_Site?$select=SiteName,SiteCode,Version&$top=1");
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            var doc  = System.Text.Json.JsonDocument.Parse(json);
            var site = doc.RootElement.GetProperty("value").EnumerateArray().FirstOrDefault();
            var siteName = site.TryGetProperty("SiteName", out var sn) ? sn.GetString() : server;
            var siteCode = site.TryGetProperty("SiteCode", out var sc) ? sc.GetString() : "";

            TxtSccmConnStatus.Text = $"✅  {siteName} ({siteCode})";
            TxtSccmConnStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(21, 128, 61));
            BtnSccmRefresh.IsEnabled = true;
            await LoadSccmSection(_sccmCurrentSection);
        }
        catch (Exception ex)
        {
            TxtSccmConnStatus.Text = $"❌  {ex.Message}";
            TxtSccmConnStatus.Foreground = System.Windows.Media.Brushes.Red;
            TxtSccmStatus.Text = "Connessione fallita";
        }
    }

    private async void BtnSccmRefresh_Click(object s, RoutedEventArgs e)
    {
        try { await LoadSccmSection(_sccmCurrentSection); }
        catch (Exception ex) { App.Log($"[BtnSccmRefresh_Click] {ex.Message}"); }
    }

    private async void SccmNavTree_SelectedItemChanged(object s, RoutedPropertyChangedEventArgs<object> e)
    {
        try
        {
        if (e.NewValue is TreeViewItem item && item.Tag is string tag)
            await LoadSccmSection(tag);
        }
        catch (Exception ex) { App.Log($"[SccmNavTree_SelectedItemChanged] {ex.Message}"); SetStatus($"❌ {ex.Message}"); }
    }

    private Task LoadSccmSection(string tag)
    {
        _sccmCurrentSection = tag;
        return tag switch
        {
            "devices"       => LoadSccmDevices(),
            "collections"   => LoadSccmCollections(),
            "tasksequences" => LoadSccmTaskSequences(),
            "applications"  => LoadSccmApplications(),
            "deployments"   => LoadSccmDeployments(),
            _               => Task.CompletedTask
        };
    }

    private async Task LoadSccmDevices()
    {
        if (_sccmClient == null) return;
        TxtSccmSection.Text = "Dispositivi";
        TxtSccmStatus.Text  = "Caricamento dispositivi...";
        BtnSccmClientAction.IsEnabled = false;
        BtnSccmDeploy.IsEnabled       = false;
        BtnSccmRunTs.IsEnabled        = false;
        try
        {
            var url  = _sccmBase + "SMS_R_System?$select=Name,LastLogonUserName,LastHardwareScan,Active,OperatingSystemNameandVersion&$top=500";
            var json = await _sccmClient.GetStringAsync(url);
            var doc  = System.Text.Json.JsonDocument.Parse(json);
            var items = new List<SccmItem>();
            foreach (var v in doc.RootElement.GetProperty("value").EnumerateArray())
            {
                var lastScan = v.TryGetProperty("LastHardwareScan", out var ls) ? ls.GetString() ?? "" : "";
                if (lastScan.Length > 10) lastScan = lastScan[..10];
                items.Add(new SccmItem
                {
                    Name        = v.TryGetProperty("Name", out var n)       ? n.GetString() ?? "" : "",
                    Type        = v.TryGetProperty("OperatingSystemNameandVersion", out var os) ? (os.GetString() ?? "").Replace("Microsoft Windows NT ", "Win ") : "Windows",
                    Status      = v.TryGetProperty("Active", out var a)     && a.GetInt32() == 1 ? "Attivo" : "Inattivo",
                    LastContact = lastScan,
                    Info        = v.TryGetProperty("LastLogonUserName", out var u) ? u.GetString() ?? "" : ""
                });
            }
            SccmGrid.ItemsSource = items;
            TxtSccmStatus.Text   = $"{items.Count} dispositivi";
            if (items.Count > 0) BtnSccmClientAction.IsEnabled = true;
        }
        catch (Exception ex) { TxtSccmStatus.Text = $"Errore: {ex.Message}"; }
    }

    private async Task LoadSccmCollections()
    {
        if (_sccmClient == null) return;
        TxtSccmSection.Text = "Collezioni";
        TxtSccmStatus.Text  = "Caricamento collezioni...";
        BtnSccmClientAction.IsEnabled = false;
        BtnSccmDeploy.IsEnabled       = true;
        BtnSccmRunTs.IsEnabled        = false;
        try
        {
            var url  = _sccmBase + "SMS_Collection?$select=Name,CollectionID,MemberCount,CollectionType,Comment&$top=200";
            var json = await _sccmClient.GetStringAsync(url);
            var doc  = System.Text.Json.JsonDocument.Parse(json);
            var items = new List<SccmItem>();
            foreach (var v in doc.RootElement.GetProperty("value").EnumerateArray())
            {
                var ct = v.TryGetProperty("CollectionType", out var ctype) ? ctype.GetInt32() : 0;
                items.Add(new SccmItem
                {
                    Name        = v.TryGetProperty("Name",         out var n)  ? n.GetString()  ?? "" : "",
                    Type        = ct == 2 ? "Dispositivi" : ct == 1 ? "Utenti" : "Altra",
                    Status      = v.TryGetProperty("MemberCount",  out var mc) ? $"{mc.GetInt32()} membri" : "",
                    LastContact = v.TryGetProperty("CollectionID", out var ci) ? ci.GetString() ?? "" : "",
                    Info        = v.TryGetProperty("Comment",      out var co) ? co.GetString() ?? "" : ""
                });
            }
            SccmGrid.ItemsSource = items;
            TxtSccmStatus.Text   = $"{items.Count} collezioni";
        }
        catch (Exception ex) { TxtSccmStatus.Text = $"Errore: {ex.Message}"; }
    }

    private async Task LoadSccmTaskSequences()
    {
        if (_sccmClient == null) return;
        TxtSccmSection.Text = "Task Sequences";
        TxtSccmStatus.Text  = "Caricamento Task Sequences...";
        BtnSccmClientAction.IsEnabled = false;
        BtnSccmDeploy.IsEnabled       = true;
        BtnSccmRunTs.IsEnabled        = true;
        try
        {
            var url  = _sccmBase + "SMS_TaskSequencePackage?$select=Name,PackageID,Version,LastRefreshTime&$top=100";
            var json = await _sccmClient.GetStringAsync(url);
            var doc  = System.Text.Json.JsonDocument.Parse(json);
            var items = new List<SccmItem>();
            foreach (var v in doc.RootElement.GetProperty("value").EnumerateArray())
            {
                var lr = v.TryGetProperty("LastRefreshTime", out var lrt) ? lrt.GetString() ?? "" : "";
                if (lr.Length > 10) lr = lr[..10];
                items.Add(new SccmItem
                {
                    Name        = v.TryGetProperty("Name",      out var n) ? n.GetString() ?? "" : "",
                    Type        = "Task Sequence",
                    Status      = v.TryGetProperty("Version",   out var ver) ? $"v{ver.GetString()}" : "",
                    LastContact = lr,
                    Info        = v.TryGetProperty("PackageID", out var pid) ? pid.GetString() ?? "" : ""
                });
            }
            SccmGrid.ItemsSource = items;
            TxtSccmStatus.Text   = $"{items.Count} Task Sequences";
        }
        catch (Exception ex) { TxtSccmStatus.Text = $"Errore: {ex.Message}"; }
    }

    private async Task LoadSccmApplications()
    {
        if (_sccmClient == null) return;
        TxtSccmSection.Text = "Applicazioni";
        TxtSccmStatus.Text  = "Caricamento applicazioni...";
        BtnSccmClientAction.IsEnabled = false;
        BtnSccmDeploy.IsEnabled       = true;
        BtnSccmRunTs.IsEnabled        = false;
        try
        {
            var url  = _sccmBase + "SMS_Application?$select=LocalizedDisplayName,SoftwareVersion,IsDeployed,DateCreated,NumberOfDependentDTs&$top=200&$filter=IsLatest eq true";
            var json = await _sccmClient.GetStringAsync(url);
            var doc  = System.Text.Json.JsonDocument.Parse(json);
            var items = new List<SccmItem>();
            foreach (var v in doc.RootElement.GetProperty("value").EnumerateArray())
            {
                var dc = v.TryGetProperty("DateCreated", out var dcp) ? dcp.GetString() ?? "" : "";
                if (dc.Length > 10) dc = dc[..10];
                var deployed = v.TryGetProperty("IsDeployed", out var isd) && isd.GetBoolean();
                items.Add(new SccmItem
                {
                    Name        = v.TryGetProperty("LocalizedDisplayName", out var n)  ? n.GetString()   ?? "" : "",
                    Type        = v.TryGetProperty("SoftwareVersion",      out var sv) ? sv.GetString()  ?? "" : "",
                    Status      = deployed ? "Distribuita" : "Non distribuita",
                    LastContact = dc,
                    Info        = v.TryGetProperty("NumberOfDependentDTs", out var dt) ? $"{dt.GetInt32()} deployment type" : ""
                });
            }
            SccmGrid.ItemsSource = items;
            TxtSccmStatus.Text   = $"{items.Count} applicazioni";
        }
        catch (Exception ex) { TxtSccmStatus.Text = $"Errore: {ex.Message}"; }
    }

    private async Task LoadSccmDeployments()
    {
        if (_sccmClient == null) return;
        TxtSccmSection.Text = "Deployments";
        TxtSccmStatus.Text  = "Caricamento deployments...";
        BtnSccmClientAction.IsEnabled = false;
        BtnSccmDeploy.IsEnabled       = false;
        BtnSccmRunTs.IsEnabled        = false;
        try
        {
            var url  = _sccmBase + "SMS_DeploymentSummary?$select=SoftwareName,CollectionName,NumberSuccess,NumberInProgress,NumberErrors,DeploymentTime&$top=200";
            var json = await _sccmClient.GetStringAsync(url);
            var doc  = System.Text.Json.JsonDocument.Parse(json);
            var items = new List<SccmItem>();
            foreach (var v in doc.RootElement.GetProperty("value").EnumerateArray())
            {
                var ok  = v.TryGetProperty("NumberSuccess",    out var ns) ? ns.GetInt32() : 0;
                var ip  = v.TryGetProperty("NumberInProgress", out var ni) ? ni.GetInt32() : 0;
                var err = v.TryGetProperty("NumberErrors",     out var ne) ? ne.GetInt32() : 0;
                var dt  = v.TryGetProperty("DeploymentTime",  out var dtp) ? dtp.GetString() ?? "" : "";
                if (dt.Length > 10) dt = dt[..10];
                items.Add(new SccmItem
                {
                    Name        = v.TryGetProperty("SoftwareName",   out var n) ? n.GetString() ?? "" : "",
                    Type        = v.TryGetProperty("CollectionName", out var c) ? c.GetString() ?? "" : "",
                    Status      = err > 0 ? $"⚠️ {err} errori" : ip > 0 ? $"🔄 {ip} in corso" : $"✅ {ok} ok",
                    LastContact = dt,
                    Info        = $"✅{ok}  🔄{ip}  ❌{err}"
                });
            }
            SccmGrid.ItemsSource = items;
            TxtSccmStatus.Text   = $"{items.Count} deployments";
        }
        catch (Exception ex) { TxtSccmStatus.Text = $"Errore: {ex.Message}"; }
    }

    private void SccmGrid_SelectionChanged(object s, SelectionChangedEventArgs e) { }

    private void BtnSccmClientAction_Click(object s, RoutedEventArgs e)
    {
        if (SccmGrid.SelectedItem is not SccmItem item) return;
        System.Windows.MessageBox.Show(
            $"Azioni disponibili per {item.Name}:\n\n• Machine Policy Retrieval\n• Hardware Inventory\n• Software Inventory\n\n(Funzione in sviluppo)",
            "Azione client SCCM", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnSccmDeploy_Click(object s, RoutedEventArgs e)
    {
        if (SccmGrid.SelectedItem is not SccmItem item) return;
        System.Windows.MessageBox.Show(
            $"Deploy di '{item.Name}' — funzione in sviluppo.\nUsa la SCCM Console per i deploy avanzati.",
            "Deploy SCCM", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnSccmRunTs_Click(object s, RoutedEventArgs e)
    {
        if (SccmGrid.SelectedItem is not SccmItem item) return;
        System.Windows.MessageBox.Show(
            $"Avvio Task Sequence '{item.Name}' — funzione in sviluppo.\nUsa la SCCM Console per avviare le TS.",
            "Avvia Task Sequence", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnSaveSettings_Click(object s, RoutedEventArgs e)
    {
        SaveConfig();
        TxtSettingsStatus.Text = "✅ Impostazioni salvate";
        TxtSettingsStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61));
        SetStatus("💾 Configurazione salvata");
    }

    private async void BtnTestConnection_Click(object s, RoutedEventArgs e)
    {
        TxtSettingsStatus.Text = "🔄 Test in corso...";
        TxtSettingsStatus.Foreground = System.Windows.Media.Brushes.Gray;
        var url = TxtCertportalUrl.Text.Trim();
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var resp = await http.GetAsync(url + "/api/status");
            TxtSettingsStatus.Text = resp.IsSuccessStatusCode
                ? $"✅ Connesso a {url}"
                : $"⚠️ HTTP {(int)resp.StatusCode}";
            TxtSettingsStatus.Foreground = resp.IsSuccessStatusCode
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61))
                : System.Windows.Media.Brushes.Orange;
        }
        catch (Exception ex)
        {
            TxtSettingsStatus.Text = $"❌ {ex.Message}";
            TxtSettingsStatus.Foreground = System.Windows.Media.Brushes.Salmon;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private void BtnOpenWebsite_Click(object s, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://polariscore.it") { UseShellExecute = true });

    private void BtnOpenGitHub_Click(object s, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://github.com/ClaudioBecchis/NovaSCM") { UseShellExecute = true });

    private void BtnDemoSCCM_Click(object s, RoutedEventArgs e)   => new DemoSCCM().Show();
    private void BtnDemoAI_Click(object s, RoutedEventArgs e)     => new DemoAI().Show();
    private void BtnDemoMSIX_Click(object s, RoutedEventArgs e)   => new DemoMSIX().Show();
    private void BtnDemoHybrid_Click(object s, RoutedEventArgs e) => new DemoHybrid().Show();

    // ── Sidebar navigation ────────────────────────────────────────────────────
    private static readonly string[] _navSections =
    [
        "Rete e Device",  // 0
        "Certificati",    // 1
        "Applicazioni",   // 2
        "OPSI",           // 3
        "PC Gestiti",     // 4
        "Deploy OS",      // 5
        "Workflow",       // 6
        "Richieste CR",   // 7
        "Console SCCM",   // 8
        "Script Library", // 9
        "Wiki",           // 10
        "Proxmox",        // 11
        "About",          // 12
        "Dashboard",      // 13
        "Impostazioni",   // 14
    ];

    private readonly Button[] _navBtns = [];

    private void Nav_Click(object s, RoutedEventArgs e)
    {
        if (s is Button btn && int.TryParse(btn.Tag?.ToString(), out int idx))
            MainTabs.SelectedIndex = idx;
    }

    // ── Ribbon handlers ──────────────────────────────────────────────
    private void RibbonTab_Click(object s, RoutedEventArgs e) { }

    private void RibbonBtnNuovo_Click(object s, RoutedEventArgs e)
    {
        switch (MainTabs.SelectedIndex)
        {
            case 1:  BtnNewCert_Click(s, e);      break;   // Certificati → nuovo cert
            case 3:  BtnOpsiCreate_Click(s, e);   break;   // OPSI → crea pacchetto
            case 6:  BtnWfNew_Click(s, e);        break;   // Workflow → nuovo workflow
            case 7:  BtnCrCreate_Click(s, e);     break;   // Richieste → crea CR
            default: SetStatus("Usa i controlli nel pannello corrente per creare un nuovo elemento"); break;
        }
    }

    private void RibbonBtnProprieta_Click(object s, RoutedEventArgs e) =>
        SetStatus("Seleziona un elemento nel pannello per vederne le proprietà");

    private void RibbonBtnElimina_Click(object s, RoutedEventArgs e) =>
        SetStatus("Seleziona un elemento e usa il pulsante Elimina nel pannello");

    private void RibbonBtnDistribuisci_Click(object s, RoutedEventArgs e)
    {
        MainTabs.SelectedIndex = 5;   // Deploy OS
        SwitchWorkspace("software");
        SetStatus("Deploy OS — configura e genera i file di installazione");
    }

    private void RibbonBtnStato_Click(object s, RoutedEventArgs e)
    {
        MainTabs.SelectedIndex = 6;   // Workflow
        SwitchWorkspace("software");
        SetStatus("Workflow — monitora lo stato delle distribuzioni");
    }

    private void RibbonBtnAggiorna_Click(object s, RoutedEventArgs e)
    {
        switch (MainTabs.SelectedIndex)
        {
            case 0:  BtnScan_Click(s, e);          break;   // Rete → scansiona
            case 7:  BtnCrRefresh_Click(s, e);     break;   // Richieste → aggiorna lista
            case 8:  BtnSccmRefresh_Click(s, e);   break;   // SCCM → aggiorna
            default: SetStatus("Aggiornamento — usa il pulsante Aggiorna nel pannello corrente"); break;
        }
    }

    private void RibbonBtnImporta_Click(object s, RoutedEventArgs e) =>
        SetStatus("Importa — funzione in sviluppo");

    private void RibbonBtnEsporta_Click(object s, RoutedEventArgs e) =>
        ExportDevicesToCsv();

    private void RibbonBtnImpostazioni_Click(object s, RoutedEventArgs e)
    {
        MainTabs.SelectedIndex = MainTabs.Items.Count - 1; // sempre l'ultimo tab
        SwitchWorkspace("admin");
    }

    // ── Workspace switcher ────────────────────────────────────────────
    private void WsBtn_Click(object s, RoutedEventArgs e)
    {
        if (s is Button btn)
        {
            var ws = btn.Name switch
            {
                "WsBtnAsset"    => "asset",
                "WsBtnSoftware" => "software",
                "WsBtnMonitor"  => "monitor",
                "WsBtnAdmin"    => "admin",
                _ => "asset"
            };
            SwitchWorkspace(ws);
        }
    }

    private void SwitchWorkspace(string ws)
    {
        TvGroupAsset.Visibility    = ws == "asset"    ? Visibility.Visible : Visibility.Collapsed;
        TvGroupSoftware.Visibility = ws == "software" ? Visibility.Visible : Visibility.Collapsed;
        TvGroupMonitor.Visibility  = ws == "monitor"  ? Visibility.Visible : Visibility.Collapsed;
        TvGroupAdmin.Visibility    = ws == "admin"    ? Visibility.Visible : Visibility.Collapsed;

        TxtWsHeader.Text = ws switch
        {
            "asset"    => "Asset e Conformità",
            "software" => "Libreria Software",
            "monitor"  => "Monitoraggio",
            "admin"    => "Amministrazione",
            _ => ""
        };

        var active   = FindResource("WsBtnActive") as System.Windows.Style;
        var inactive = FindResource("WsBtn")       as System.Windows.Style;
        WsBtnAsset.Style    = ws == "asset"    ? active : inactive;
        WsBtnSoftware.Style = ws == "software" ? active : inactive;
        WsBtnMonitor.Style  = ws == "monitor"  ? active : inactive;
        WsBtnAdmin.Style    = ws == "admin"    ? active : inactive;
    }

    // ── TreeView navigation ───────────────────────────────────────────
    private void NavTree_ItemSelected(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.TreeViewItem item &&
            int.TryParse(item.Tag?.ToString(), out int idx))
        {
            MainTabs.SelectedIndex = idx;
            e.Handled = true;
        }
    }

    private void UpdateNavState(int idx)
    {
        var btns = new[]
        {
            NavRete, NavCert, NavApp, NavOpsi, NavPc,
            NavDeploy, NavWorkflow, NavRichieste, NavSccm, NavImpostazioni, NavAbout
        };
        var active   = FindResource("NavSideBtnActive") as System.Windows.Style;
        var inactive = FindResource("NavSideBtn")       as System.Windows.Style;
        for (int i = 0; i < btns.Length; i++)
            btns[i].Style = i == idx ? active : inactive;

        TxtNavSection.Text = idx >= 0 && idx < _navSections.Length
            ? _navSections[idx] : "";
    }

    // ── FEAT-01: Dashboard ────────────────────────────────────────────────────
    private async Task RefreshDashboardAsync()
    {
        try
        {
            var pcOnline  = _netRows.Count(r => r.Status.Contains("online", StringComparison.OrdinalIgnoreCase) || r.Status.Contains("🟢"));
            var pcTotal   = _netRows.Count;
            var wfRunning = _wfAssignRows.Count(w => w.Status.Contains("running", StringComparison.OrdinalIgnoreCase));

            int crOpen = 0;
            var feedItems = new List<string>();
            if (_apiSvc != null)
            {
                try
                {
                    var json = await _apiSvc.GetDashboardJsonAsync();
                    var doc  = System.Text.Json.JsonDocument.Parse(json);
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        var st = el.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                        if (st == "open" || st == "pending" || st == "in_progress") crOpen++;
                        var pc = el.TryGetProperty("pc_name", out var p) ? p.GetString() ?? "" : "";
                        var ts = el.TryGetProperty("created_at", out var t) ? t.GetString() ?? "" : "";
                        feedItems.Add($"📋 CR {pc}  [{st}]  {ts[..Math.Min(10, ts.Length)]}");
                    }
                }
                catch { feedItems.Add("⚠️  Server API non raggiungibile"); }
            }

            // Aggiungi eventi workflow recenti
            foreach (var wf in _wfAssignRows.Take(3))
                feedItems.Add($"⚙️  {wf.PcName} — {wf.WorkflowNome} [{wf.Status}]");

            if (feedItems.Count == 0) feedItems.Add("Nessun evento recente.");

            // Aggiorna UI — già sul thread UI dopo await (no Dispatcher.Invoke necessario)
            TxtDashPcOnline.Text    = pcTotal > 0 ? $"{pcOnline}/{pcTotal}" : "—";
            TxtDashWorkflow.Text    = _wfAssignRows.Count > 0 ? wfRunning.ToString() : "—";
            TxtDashCrOpen.Text      = crOpen.ToString();
            TxtDashDevices.Text     = pcTotal > 0 ? pcTotal.ToString() : "—";
            DashFeed.ItemsSource    = feedItems.Take(12).ToList();
            TxtDashLastRefresh.Text = $"Aggiornato: {DateTime.Now:HH:mm:ss}";
            UpdateNavBadges(wfRunning, crOpen);
        }
        catch (Exception ex) { App.Log($"[Dashboard] {ex.Message}"); }
    }

    // UI-04: badge counters sui TreeViewItem della nav
    private void UpdateNavBadges(int wfRunning, int crOpen)
    {
        TvItemWorkflow.Header  = wfRunning > 0
            ? $"⚙️  Workflow  [{wfRunning}]"
            : "⚙️  Workflow";
        TvItemRichieste.Header = crOpen > 0
            ? $"📋  Richieste CR  [{crOpen}]"
            : "📋  Richieste CR";
    }

    private void BtnDashRefresh_Click(object s, RoutedEventArgs e)
        => _ = RefreshDashboardAsync();

    private void BtnDashScan_Click(object s, RoutedEventArgs e)
    {
        MainTabs.SelectedIndex = 0;
        SwitchWorkspace("asset");
        BtnScan_Click(s, e);
    }

    private void BtnDashWorkflow_Click(object s, RoutedEventArgs e)
    {
        MainTabs.SelectedIndex = 6;
        SwitchWorkspace("software");
        BtnWfNew_Click(s, e);
    }

    // ── DX-02: log viewer ─────────────────────────────────────────────────────
    private bool _logVisible = false;
    private const int LogPanelHeight = 160;

    private void AppLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}]  {message}";
        Dispatcher.Invoke(() =>
        {
            TxtLogViewer.AppendText(line + "\n");
            TxtLogViewer.ScrollToEnd();
        });
    }

    private void BtnToggleLog_Click(object s, System.Windows.Input.MouseButtonEventArgs e) => ToggleLog();
    private void BtnCloseLog_Click(object s, RoutedEventArgs e) => ToggleLog(forceClose: true);
    private void BtnClearLog_Click(object s, RoutedEventArgs e) => TxtLogViewer.Clear();

    private void ToggleLog(bool forceClose = false)
    {
        _logVisible = forceClose ? false : !_logVisible;
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            To       = _logVisible ? LogPanelHeight : 0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        LogPanel.BeginAnimation(FrameworkElement.HeightProperty, anim);
    }

    // ── UI-02: dark/light mode toggle ────────────────────────────────────────
    private bool _isDarkTheme = true;

    private void BtnThemeToggle_Click(object s, RoutedEventArgs e)
    {
        _isDarkTheme = !_isDarkTheme;
        ModernWpf.ThemeManager.Current.ApplicationTheme = _isDarkTheme
            ? ModernWpf.ApplicationTheme.Dark
            : ModernWpf.ApplicationTheme.Light;
        BtnThemeToggle.Content = _isDarkTheme ? "☀️  Modalità chiara" : "🌙  Modalità scura";
    }

    // ── UI-07: sidebar collapse/expand ────────────────────────────────────────
    private bool _navCollapsed = false;

    private void BtnNavCollapse_Click(object s, RoutedEventArgs e) => ToggleSidebar();
    private void NavExpandBtn_Click(object s, System.Windows.Input.MouseButtonEventArgs e) => ToggleSidebar();

    private void ToggleSidebar()
    {
        _navCollapsed = !_navCollapsed;
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            To           = _navCollapsed ? 0 : 236,
            Duration     = TimeSpan.FromMilliseconds(180),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        NavGrid.BeginAnimation(FrameworkElement.WidthProperty, anim);
        NavExpandBtn.Visibility = _navCollapsed ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── FEAT-03: Ricerca globale Ctrl+K ──────────────────────────────────────
    private void SearchOverlay_BackdropClick(object s, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.Source == SearchOverlay) CloseSearch();
    }

    private void SearchOverlay_ContentClick(object s, System.Windows.Input.MouseButtonEventArgs e)
        => e.Handled = true;

    private void GlobalSearch_KeyDown(object s, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape) CloseSearch();
        else if (e.Key == System.Windows.Input.Key.Enter && SearchResults.Items.Count > 0)
        {
            if (SearchResults.SelectedIndex < 0) SearchResults.SelectedIndex = 0;
            SearchResult_Selected(SearchResults, null!);
        }
        else if (e.Key == System.Windows.Input.Key.Down)
        {
            if (SearchResults.SelectedIndex < SearchResults.Items.Count - 1)
                SearchResults.SelectedIndex++;
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Up)
        {
            if (SearchResults.SelectedIndex > 0) SearchResults.SelectedIndex--;
            e.Handled = true;
        }
    }

    private void GlobalSearch_TextChanged(object s, System.Windows.Controls.TextChangedEventArgs e)
    {
        var q = TxtGlobalSearch.Text.ToLowerInvariant();
        TxtSearchHint.Visibility = string.IsNullOrEmpty(q) ? Visibility.Visible : Visibility.Collapsed;
        if (q.Length < 2) { SearchResults.ItemsSource = null; return; }

        var results = new List<SearchResult>();

        // Device dalla scan rete
        foreach (var r in _netRows.Where(r =>
            r.Ip.Contains(q) || r.Name.ToLower().Contains(q) ||
            r.Vendor.ToLower().Contains(q)).Take(5))
            results.Add(new SearchResult($"📡  {r.Ip}", $"{r.Name}  {r.Vendor}", 0, "asset"));

        // Workflow
        foreach (var w in _wfRows.Where(w => w.Nome.ToLower().Contains(q)).Take(5))
            results.Add(new SearchResult($"⚙️  {w.Nome}", $"Workflow — {w.Descrizione}", 6, "software"));

        // Workflow assignments (PC)
        foreach (var a in _wfAssignRows.Where(a =>
            a.PcName.ToLower().Contains(q) || a.WorkflowNome.ToLower().Contains(q)).Take(5))
            results.Add(new SearchResult($"🖥️  {a.PcName}", $"{a.WorkflowNome}  [{a.Status}]", 6, "software"));

        SearchResults.ItemsSource = results;
    }

    private void SearchResult_Selected(object s, System.Windows.Controls.SelectionChangedEventArgs? e)
    {
        if (SearchResults.SelectedItem is not SearchResult r) return;
        CloseSearch();
        SwitchWorkspace(r.Workspace);
        MainTabs.SelectedIndex = r.TabIndex;
    }

    private void CloseSearch()
    {
        SearchOverlay.Visibility = Visibility.Collapsed;
        TxtGlobalSearch.Text     = "";
        SearchResults.ItemsSource = null;
    }

    private void OpenSearch()
    {
        SearchOverlay.Visibility = Visibility.Visible;
        TxtGlobalSearch.Focus();
        TxtSearchHint.Visibility = Visibility.Visible;
    }

    private const string CurrentVersion = "1.7.0";
    private string? _updateDownloadUrl;

    // BUG-09: confronto semver corretto — string.Compare è lessicografico ("1.10" < "1.9")
    private static bool IsNewerVersion(string remote, string current)
    {
        if (Version.TryParse(remote.TrimStart('v'), out var r) &&
            Version.TryParse(current.TrimStart('v'), out var c))
            return r > c;
        // fallback lessicografico solo se il parse fallisce
        return string.Compare(remote, current, StringComparison.OrdinalIgnoreCase) > 0;
    }

    // ── Controlla aggiornamenti dal server NovaSCM ────────────────────────────

    private const string GitHubReleasesApi =
        "https://api.github.com/repos/ClaudioBecchis/NovaSCM/releases/latest";

    private async Task CheckForUpdatesAsync(bool silent = true)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            // GitHub API richiede User-Agent
            http.DefaultRequestHeaders.Add("User-Agent", $"NovaSCM/{CurrentVersion}");
            http.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

            var json = await http.GetStringAsync(GitHubReleasesApi);
            var doc  = JsonDocument.Parse(json);

            // tag_name es: "v1.0.7" — rimuoviamo il 'v' iniziale
            var tag       = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var serverVer = tag.TrimStart('v');
            var notes     = doc.RootElement.TryGetProperty("body",     out var b) ? b.GetString() ?? "" : "";
            // Prima riga delle note come sommario
            var notesSummary = notes.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                    .FirstOrDefault()?.Trim() ?? "";
            if (notesSummary.Length > 80) notesSummary = notesSummary[..77] + "…";

            // URL download: primo asset .exe, oppure pagina release HTML
            var dlUrl = doc.RootElement.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
            if (doc.RootElement.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        dlUrl = asset.TryGetProperty("browser_download_url", out var du)
                            ? du.GetString() ?? dlUrl : dlUrl;
                        break;
                    }
                }
            }

            bool hasUpdate = !string.IsNullOrEmpty(serverVer) &&
                             IsNewerVersion(serverVer, CurrentVersion);

            if (hasUpdate && !string.IsNullOrEmpty(dlUrl))
            {
                _updateDownloadUrl = dlUrl;
                TxtUpdateBanner.Text    = $"🔄  NovaSCM v{serverVer} disponibile" +
                                          (string.IsNullOrEmpty(notesSummary) ? "" : $"  —  {notesSummary}");
                UpdateBanner.Visibility = Visibility.Visible;

                var toast = new UpdateToast(serverVer, notesSummary,
                    () => BtnInstallUpdate_Click(this, new RoutedEventArgs()));
                toast.Show();

                if (!silent)
                {
                    TxtUpdateStatus.Text       = $"🆕  Nuova versione v{serverVer} disponibile — clicca il banner in alto!";
                    TxtUpdateStatus.Foreground = System.Windows.Media.Brushes.Orange;
                }
            }
            else if (!silent)
            {
                TxtUpdateStatus.Text       = $"✅  Sei aggiornato (v{CurrentVersion})";
                TxtUpdateStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(21, 128, 61));
            }
        }
        catch
        {
            if (!silent)
            {
                TxtUpdateStatus.Text       = $"⚠️  Impossibile contattare GitHub (v{CurrentVersion} installata)";
                TxtUpdateStatus.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }
    }

    private async void BtnCheckUpdate_Click(object s, RoutedEventArgs e)
    {
        try
        {
            TxtUpdateStatus.Text       = "⏳  Controllo in corso...";
            TxtUpdateStatus.Foreground = System.Windows.Media.Brushes.Gray;
            await CheckForUpdatesAsync(silent: false);
        }
        catch (Exception ex) { App.Log($"[BtnCheckUpdate_Click] {ex}"); }
    }

    // ── Auto-update: scarica e sostituisce l'exe ──────────────────────────────

    private async void BtnInstallUpdate_Click(object s, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_updateDownloadUrl)) return;

        BtnInstallUpdate.IsEnabled = false;
        BtnInstallUpdate.Content   = "⏳  Download...";

        try
        {
            // Determina se l'asset è un installer Inno Setup (nome contiene "Setup")
            bool isInstaller = _updateDownloadUrl.Contains("Setup", StringComparison.OrdinalIgnoreCase);
            var  tmpFile     = Path.Combine(Path.GetTempPath(),
                                            isInstaller ? "NovaSCM_setup.exe" : "NovaSCM_update.exe");

            // Scarica
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.Add("User-Agent", $"NovaSCM/{CurrentVersion}");
            var bytes = await http.GetByteArrayAsync(_updateDownloadUrl);
            await File.WriteAllBytesAsync(tmpFile, bytes);

            if (isInstaller)
            {
                // Inno Setup: avvia silenzioso — chiude l'app corrente e la rilancia automaticamente
                var si = new ProcessStartInfo(tmpFile) { UseShellExecute = true };
                si.ArgumentList.Add("/VERYSILENT");
                si.ArgumentList.Add("/CLOSEAPPLICATIONS");
                si.ArgumentList.Add("/RESTARTAPPLICATIONS");
                si.ArgumentList.Add("/SP-");
                si.ArgumentList.Add("/SUPPRESSMSGBOXES");
                Process.Start(si);
            }
            else
            {
                // Portable exe: script bat copia con retry e rilancia
                var currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;
                var batPath    = Path.Combine(Path.GetTempPath(), "novascm_updater.bat");
                var batContent = $"@echo off\r\n" +
                                 $"ping 127.0.0.1 -n 4 > nul\r\n" +
                                 $":retry\r\n" +
                                 $"copy /Y \"{tmpFile}\" \"{currentExe}\" > nul 2>&1\r\n" +
                                 $"if errorlevel 1 ( ping 127.0.0.1 -n 3 > nul & goto retry )\r\n" +
                                 $"start \"\" \"{currentExe}\"\r\n" +
                                 $"del \"%~f0\"\r\n";
                await File.WriteAllTextAsync(batPath, batContent, System.Text.Encoding.ASCII);

                var updaterInfo = new ProcessStartInfo("cmd.exe")
                {
                    UseShellExecute = true,
                    WindowStyle     = ProcessWindowStyle.Hidden
                };
                updaterInfo.ArgumentList.Add("/C");
                updaterInfo.ArgumentList.Add(batPath);
                Process.Start(updaterInfo);
            }

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            BtnInstallUpdate.IsEnabled = true;
            BtnInstallUpdate.Content   = "⬇️  Installa ora";
            MessageBox.Show($"Errore durante l'aggiornamento:\n{ex.Message}",
                            "NovaSCM", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnDismissUpdate_Click(object s, RoutedEventArgs e) =>
        UpdateBanner.Visibility = Visibility.Collapsed;

    private void SetStatus(string msg) => StatusBarMsg.Text = msg;

    private void ShowQr(string name, string mac)
    {
        var url = $"{_config.CertportalUrl}/android/{mac.Replace(":", "").ToUpper()}";
        new QrWindow(name, url) { Owner = this }.ShowDialog();
    }

    // ── Deploy tab ────────────────────────────────────────────────────────────
    private string? _deployTmpDir;
    // Winget package ID: Publisher.Name, solo caratteri sicuri (no injection)
    private static readonly System.Text.RegularExpressions.Regex _wingetIdRegex =
        new(@"^[a-zA-Z0-9][a-zA-Z0-9.\-_]{0,127}$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly string ProfilesDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "PolarisManager", "profiles");

    private readonly ObservableCollection<string> _deployPackages =
    [
        "Mozilla.Firefox",
        "7zip.7zip",
        "Notepad++.Notepad++",
    ];

    private DeployConfig BuildDeployConfigFromUi()
    {
        var cfg = new DeployConfig
        {
            AdminPassword  = TxtDeployAdminPass.Password,
            UserName       = TxtDeployUsername.Text.Trim(),
            UserPassword   = TxtDeployUserPass.Password,
            PcNameTemplate = TxtDeployPcName.Text.Trim(),
            ProductKey     = TxtDeployProductKey.Text.Trim(),
            IncludeAgent      = ChkIncludeAgent.IsChecked == true,
            ServerUrl         = TxtDeployServerUrl.Text.Trim(),
            NovaSCMCrApiUrl   = _config.NovaSCMApiUrl,
        };

        // Edizione
        if (CboWinEdition.SelectedItem is System.Windows.Controls.ComboBoxItem edItem)
        {
            cfg.WinEdition   = edItem.Content?.ToString() ?? "Windows 11 Pro";
            cfg.WinEditionId = edItem.Tag?.ToString()     ?? "Professional";
        }

        // Locale
        if (CboWinLocale.SelectedItem is System.Windows.Controls.ComboBoxItem lcItem)
            cfg.Locale = lcItem.Tag?.ToString() ?? "it-IT";

        // Software winget — dalla lista dinamica
        cfg.WingetPackages.AddRange(_deployPackages);

        cfg.UseMicrosoftAccount = RbMsAccount.IsChecked == true;

        // Dominio
        if (RbAdLocale.IsChecked == true)
        {
            cfg.DomainJoin         = "AD";
            cfg.DomainName         = TxtDomainName.Text.Trim();
            cfg.DomainUser         = TxtDomainUser.Text.Trim();
            cfg.DomainPassword     = TxtDomainPass.Password;
            cfg.DomainControllerIp = TxtDomainControllerIp.Text.Trim();
        }
        else if (RbAzureAd.IsChecked == true)
        {
            cfg.DomainJoin    = "AzureAD";
            cfg.AzureTenantId = TxtAzureTenant.Text.Trim();
        }

        return cfg;
    }

    private void RbDomain_Checked(object s, RoutedEventArgs e)
    {
        if (PanelAdLocale == null) return; // guard durante InitializeComponent
        PanelAdLocale.Visibility = RbAdLocale.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelAzureAd.Visibility  = RbAzureAd.IsChecked  == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RbAccount_Checked(object s, RoutedEventArgs e)
    {
        if (TxtAccountNote == null) return;
        TxtAccountNote.Visibility = RbMsAccount.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Profili Deploy ────────────────────────────────────────────────────────
    private void RefreshProfiles()
    {
        Directory.CreateDirectory(ProfilesDir);
        var profiles = Directory.GetFiles(ProfilesDir, "*.json")
                                .Select(Path.GetFileNameWithoutExtension)
                                .OrderBy(n => n)
                                .ToList();
        CboProfiles.ItemsSource   = profiles;
        CboProfiles.SelectedIndex = profiles.Count > 0 ? 0 : -1;
    }

    private void BtnSaveProfile_Click(object s, RoutedEventArgs e)
    {
        var name = Microsoft.VisualBasic.Interaction.InputBox(
            "Nome profilo:", "Salva profilo",
            CboProfiles.SelectedItem?.ToString() ?? "Profilo1");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = string.Concat(name.Split(Path.GetInvalidFileNameChars()));
        var cfg  = BuildDeployConfigFromUi();
        var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(ProfilesDir);
        File.WriteAllText(Path.Combine(ProfilesDir, $"{name}.json"), json);
        RefreshProfiles();
        CboProfiles.SelectedItem   = name;
        TxtDeployStatus.Text       = $"💾  Profilo '{name}' salvato";
        TxtDeployStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61));
    }

    private void BtnLoadProfile_Click(object s, RoutedEventArgs e)
    {
        if (CboProfiles.SelectedItem is not string name) return;
        var path = Path.Combine(ProfilesDir, $"{name}.json");
        if (!File.Exists(path)) return;
        var cfg = JsonSerializer.Deserialize<DeployConfig>(File.ReadAllText(path));
        if (cfg == null) return;
        ApplyProfileToUi(cfg);
        TxtDeployStatus.Text       = $"📂  Profilo '{name}' caricato";
        TxtDeployStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61));
    }

    private void BtnImportProfile_Click(object s, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.OpenFileDialog
        {
            Title  = "Importa profilo deploy",
            Filter = "Tutti i profili|*.json;*.xml|Profilo JSON (*.json)|*.json|autounattend XML (*.xml)|*.xml",
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        try
        {
            var ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
            DeployConfig? cfg = ext == ".xml"
                ? ParseAutounattendXml(File.ReadAllText(dlg.FileName))
                : JsonSerializer.Deserialize<DeployConfig>(File.ReadAllText(dlg.FileName));
            if (cfg == null) return;
            var name = Path.GetFileNameWithoutExtension(dlg.FileName);
            Directory.CreateDirectory(ProfilesDir);
            File.WriteAllText(Path.Combine(ProfilesDir, $"{name}.json"),
                JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
            RefreshProfiles();
            CboProfiles.SelectedItem   = name;
            ApplyProfileToUi(cfg);
            TxtDeployStatus.Text       = $"📁  Profilo '{name}' importato e caricato";
            TxtDeployStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore nel file:\n{ex.Message}", "Importazione fallita",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static DeployConfig ParseAutounattendXml(string xml)
    {
        var cfg = new DeployConfig();
        var doc = System.Xml.Linq.XDocument.Parse(xml);
        System.Xml.Linq.XNamespace ns = "urn:schemas-microsoft-com:unattend";

        // Helper: cerca elemento (anche annidato) dentro un componente specifico in un pass
        string El(string pass, string component, string element)
        {
            return doc.Descendants(ns + "settings")
                .FirstOrDefault(s => (string?)s.Attribute("pass") == pass)
                ?.Descendants(ns + "component")
                .FirstOrDefault(c => ((string?)c.Attribute("name") ?? "").Contains(component))
                ?.Descendants(ns + element).FirstOrDefault()?.Value?.Trim() ?? "";
        }

        // Locale — UILanguage è figlio diretto del componente (non dentro SetupUILanguage)
        var localeVal = El("windowsPE", "International-Core-WinPE", "UILanguage");
        cfg.Locale = string.IsNullOrEmpty(localeVal) ? "it-IT" : localeVal;

        // Edizione — MetaData dove Key = "/IMAGE/NAME"
        var edition = doc.Descendants(ns + "MetaData")
            .FirstOrDefault(m => m.Element(ns + "Key")?.Value?.Trim() == "/IMAGE/NAME")
            ?.Element(ns + "Value")?.Value?.Trim() ?? "";
        if (!string.IsNullOrEmpty(edition)) { cfg.WinEdition = edition; cfg.WinEditionId = edition; }

        // Product key — dentro <ProductKey><Key>...</Key></ProductKey>
        cfg.ProductKey = doc.Descendants(ns + "ProductKey")
            .FirstOrDefault()?.Element(ns + "Key")?.Value?.Trim() ?? "";

        // Computer name
        var pcName = El("specialize", "Shell-Setup", "ComputerName");
        if (!string.IsNullOrEmpty(pcName) && pcName != "*") cfg.PcNameTemplate = pcName;

        // TimeZone
        var tz = El("specialize", "Shell-Setup", "TimeZone");
        if (!string.IsNullOrEmpty(tz)) cfg.TimeZone = tz;

        // BypassNRO → account locale
        cfg.UseMicrosoftAccount = !xml.Contains("BypassNRO");

        // Admin password — dentro AdministratorPassword/Value
        var adminPw = doc.Descendants(ns + "AdministratorPassword")
            .FirstOrDefault()?.Element(ns + "Value")?.Value?.Trim() ?? "";
        if (!string.IsNullOrEmpty(adminPw)) cfg.AdminPassword = adminPw;

        // OOBE HideOnlineAccountScreens
        var hideOnline = El("oobeSystem", "Shell-Setup", "HideOnlineAccountScreens");
        if (hideOnline == "true") cfg.UseMicrosoftAccount = false;

        // Domain join — formato vecchio: UnattendedJoin component
        var joinDomain = El("specialize", "UnattendedJoin", "JoinDomain");
        if (!string.IsNullOrEmpty(joinDomain))
        {
            cfg.DomainJoin     = "AD";
            cfg.DomainName     = joinDomain;
            cfg.DomainUser     = El("specialize", "UnattendedJoin", "Username");
            cfg.DomainPassword = El("specialize", "UnattendedJoin", "Password");
        }
        else
        {
            // Formato nuovo: Add-Computer in RunSynchronousCommands
            var addPcCmd = doc.Descendants(ns + "Path")
                .FirstOrDefault(p => p.Value.Contains("Add-Computer") && p.Value.Contains("DomainName"))?.Value ?? "";
            if (!string.IsNullOrEmpty(addPcCmd))
            {
                cfg.DomainJoin = "AD";
                var m1 = Regex.Match(addPcCmd, @"-DomainName\s+'([^']+)'");
                if (m1.Success) cfg.DomainName = m1.Groups[1].Value;
                var m2 = Regex.Match(addPcCmd, @"PSCredential\('([^\\]+)\\([^']+)'");
                if (m2.Success) cfg.DomainUser = m2.Groups[2].Value;
                var m3 = Regex.Match(addPcCmd, @"ConvertTo-SecureString\s+'([^']+)'");
                if (m3.Success) cfg.DomainPassword = m3.Groups[1].Value;
            }
        }

        // DNS DC IP — formato vecchio: netsh static | formato nuovo: Set-DnsClientServerAddress
        var netshCmd = doc.Descendants(ns + "Path")
            .FirstOrDefault(p => p.Value.Contains("netsh") && p.Value.Contains("static"))?.Value ?? "";
        if (!string.IsNullOrEmpty(netshCmd))
        {
            var matches = Regex.Matches(netshCmd, @"(\d+\.\d+\.\d+\.\d+)");
            foreach (Match m in matches)
                if (m.Value != "0.0.0.0") { cfg.DomainControllerIp = m.Value; break; }
        }
        else
        {
            var dnsCmd = doc.Descendants(ns + "Path")
                .FirstOrDefault(p => p.Value.Contains("Set-DnsClientServerAddress"))?.Value ?? "";
            var dm = Regex.Match(dnsCmd, @"ServerAddresses\s+'(\d+\.\d+\.\d+\.\d+)'");
            if (dm.Success) cfg.DomainControllerIp = dm.Groups[1].Value;
        }

        return cfg;
    }

    private void BtnDeleteProfile_Click(object s, RoutedEventArgs e)
    {
        if (CboProfiles.SelectedItem is not string name) return;
        var r = MessageBox.Show($"Eliminare il profilo '{name}'?", "Conferma",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        File.Delete(Path.Combine(ProfilesDir, $"{name}.json"));
        RefreshProfiles();
        TxtDeployStatus.Text       = $"🗑  Profilo '{name}' eliminato";
        TxtDeployStatus.Foreground = System.Windows.Media.Brushes.Orange;
    }

    private void ApplyProfileToUi(DeployConfig cfg)
    {
        // Edizione
        foreach (System.Windows.Controls.ComboBoxItem item in CboWinEdition.Items)
            if (item.Tag?.ToString() == cfg.WinEditionId) { CboWinEdition.SelectedItem = item; break; }
        // Locale
        foreach (System.Windows.Controls.ComboBoxItem item in CboWinLocale.Items)
            if (item.Tag?.ToString() == cfg.Locale) { CboWinLocale.SelectedItem = item; break; }

        TxtDeployPcName.Text       = cfg.PcNameTemplate;
        TxtDeployProductKey.Text   = cfg.ProductKey;
        TxtDeployAdminPass.Password = cfg.AdminPassword;
        TxtDeployUsername.Text     = cfg.UserName;
        TxtDeployUserPass.Password = cfg.UserPassword;
        TxtDeployServerUrl.Text    = cfg.ServerUrl;
        ChkIncludeAgent.IsChecked  = cfg.IncludeAgent;
        RbLocalAccount.IsChecked   = !cfg.UseMicrosoftAccount;
        RbMsAccount.IsChecked      = cfg.UseMicrosoftAccount;

        // Dominio
        RbWorkgroup.IsChecked  = cfg.DomainJoin == "Workgroup";
        RbAdLocale.IsChecked   = cfg.DomainJoin == "AD";
        RbAzureAd.IsChecked    = cfg.DomainJoin == "AzureAD";
        TxtDomainName.Text     = cfg.DomainName;
        TxtDomainUser.Text     = cfg.DomainUser;
        TxtDomainPass.Password = cfg.DomainPassword;
        TxtDomainControllerIp.Text = cfg.DomainControllerIp;
        TxtAzureTenant.Text    = cfg.AzureTenantId;

        // Software
        _deployPackages.Clear();
        foreach (var p in cfg.WingetPackages) _deployPackages.Add(p);
    }

    private void BtnAddPackage_Click(object s, RoutedEventArgs e) => AddPackage();

    private void TxtNewPackage_KeyDown(object s, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) AddPackage();
    }

    private void AddPackage()
    {
        var id = TxtNewPackage.Text.Trim();
        if (string.IsNullOrEmpty(id)) return;
        if (!_wingetIdRegex.IsMatch(id))
        {
            SetStatus("⚠️ ID pacchetto non valido (usa formato Publisher.Nome, es: Mozilla.Firefox)");
            return;
        }
        if (!_deployPackages.Contains(id, StringComparer.OrdinalIgnoreCase))
            _deployPackages.Add(id);
        TxtNewPackage.Clear();
        TxtNewPackage.Focus();
    }

    private void BtnRemovePackage_Click(object s, RoutedEventArgs e)
    {
        if (s is Button btn && btn.DataContext is string pkg)
            _deployPackages.Remove(pkg);
    }

    // ── TAB RICHIESTE (CR) ────────────────────────────────────────────────────

    private readonly ObservableCollection<string> _crPackages = [];
    private string CrApiBase => _config.NovaSCMApiUrl;

    private void InitCrTab()
    {
        LstCrPackages.ItemsSource = _crPackages;
    }

    // UI-03: fade-in del contenuto al cambio tab
    private void FadeInContent()
    {
        MainTabs.Opacity = 0;
        var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 1,
            new Duration(TimeSpan.FromMilliseconds(160)))
        {
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        MainTabs.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private async void MainTabs_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        try
        {
        if (e.Source != MainTabs) return;
        FadeInContent();
        UpdateNavState(MainTabs.SelectedIndex);
        if (MainTabs.SelectedItem is not TabItem tab) return;
        var header = tab.Header?.ToString() ?? "";

        // FEAT-01: avvia/ferma timer dashboard
        if (header.Contains("Dashboard"))
        {
            _dashTimer.Start();
            await RefreshDashboardAsync();
            return;
        }
        _dashTimer.Stop();

        if (header.Contains("Richieste"))
            await LoadCrListAsync();
        else if (header.Contains("Workflow"))
        {
            await LoadWorkflowsAsync();
            await LoadWorkflowAssignmentsAsync();
        }
        else if (header.Contains("About"))
        {
            await Task.Delay(150);
            StartMatrixRain();
        }
        else if (header.Contains("PC"))
        {
            StopMatrixRain();
            if (_gaugeTimer == null) StartGauges();
        }
        else if (header.Contains("Wiki"))
        {
            StopMatrixRain();
            if (WikiNavList.SelectedItem == null && WikiNavList.Items.Count > 0)
                WikiNavList.SelectedIndex = 0;
        }
        else if (header.Contains("Script"))
        {
            StopMatrixRain();
            if (ScriptList.Items.Count == 0) InitScriptLibrary();
        }
        else
        {
            StopMatrixRain();
        }
        }
        catch (Exception ex) { App.Log($"[MainTabs_SelectionChanged] {ex.Message}"); SetStatus($"❌ {ex.Message}"); }
    }

    private void TxtCrNewPackage_KeyDown(object s, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) CrAddPackage();
    }

    private void BtnCrAddPackage_Click(object s, RoutedEventArgs e) => CrAddPackage();

    private void CrAddPackage()
    {
        var id = TxtCrNewPackage.Text.Trim();
        if (string.IsNullOrEmpty(id)) return;
        if (!_wingetIdRegex.IsMatch(id))
        {
            Notifier.Show("ID pacchetto non valido", $"'{id}' non è un ID winget valido.\nUsa il formato Publisher.Nome (es: Mozilla.Firefox)", Notifier.Level.Warning, autoCloseSec: 4);
            return;
        }
        if (!_crPackages.Contains(id, StringComparer.OrdinalIgnoreCase))
            _crPackages.Add(id);
        TxtCrNewPackage.Clear();
        TxtCrNewPackage.Focus();
    }

    private void BtnCrRemovePackage_Click(object s, RoutedEventArgs e)
    {
        if (s is Button btn && btn.DataContext is string pkg)
            _crPackages.Remove(pkg);
    }

    private void SvPanel_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        var sv = (System.Windows.Controls.ScrollViewer)sender;
        sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    private void BtnCrCopyDjoin_Click(object s, RoutedEventArgs e)
    {
        var pcName = TxtCrPcName.Text.Trim().ToUpper();
        var domain = TxtCrDomain.Text.Trim();
        var ou     = TxtCrOu.Text.Trim();
        if (string.IsNullOrEmpty(pcName) || string.IsNullOrEmpty(domain))
        {
            TxtCrStatus.Text       = "⚠️  Inserisci Nome PC e Dominio prima";
            TxtCrStatus.Foreground = System.Windows.Media.Brushes.Orange;
            return;
        }
        var ouPart = string.IsNullOrEmpty(ou) ? "" : $" /machineou \"{ou}\"";
        var cmd = $"# Esegui sul Domain Controller (PowerShell come Amministratore):\r\n" +
                  $"djoin /provision /domain \"{domain}\" /machine \"{pcName}\"{ouPart} /savefile \"$env:TEMP\\{pcName}.djoin\" /reuse\r\n" +
                  $"[System.Convert]::ToBase64String([System.IO.File]::ReadAllBytes(\"$env:TEMP\\{pcName}.djoin\"))";
        Clipboard.SetText(cmd);
        TxtCrStatus.Text       = "📋  Comando copiato — esegui sul DC, incolla l'output in 'ODJ Blob'";
        TxtCrStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61));
    }

    private async void BtnCrCreate_Click(object s, RoutedEventArgs e)
    {
        if (!EnsureApiConfigured()) return;
        var pcName = TxtCrPcName.Text.Trim().ToUpper();
        var domain = TxtCrDomain.Text.Trim();
        if (string.IsNullOrEmpty(pcName) || string.IsNullOrEmpty(domain))
        {
            TxtCrStatus.Text       = "⚠️  Nome PC e Dominio sono obbligatori";
            TxtCrStatus.Foreground = System.Windows.Media.Brushes.Orange;
            return;
        }

        try
        {
            var json = await _apiSvc!.PostCrAsync(new
            {
                pc_name       = pcName,
                domain        = domain,
                ou            = TxtCrOu.Text.Trim(),
                dc_ip         = TxtCrDcIp.Text.Trim(),
                join_user     = TxtCrJoinUser.Text.Trim(),
                join_pass     = TxtCrJoinPass.Password,
                odj_blob      = TxtCrOdjBlob.Text.Trim(),
                admin_pass    = TxtCrAdminPass.Password,
                assigned_user = TxtCrAssignedUser.Text.Trim(),
                software      = _crPackages.ToList(),
                notes         = TxtCrNotes.Text.Trim(),
            });
            // Salva anche nel DB locale (cache)
            try
            {
                var respDoc = System.Text.Json.JsonDocument.Parse(json);
                int newId   = respDoc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
                Database.UpsertCr(new CrRow
                {
                    Id = newId, PcName = pcName,
                    Domain = TxtCrDomain.Text.Trim(), Ou = TxtCrOu.Text.Trim(),
                    AssignedUser = TxtCrAssignedUser.Text.Trim(),
                    Status = "pending", CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                    Notes = TxtCrNotes.Text.Trim(),
                });
            }
            catch { }
            TxtCrStatus.Text       = $"✅  CR creato per {pcName}";
            TxtCrStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61));
            TxtCrPcName.Clear();
            await LoadCrListAsync();
        }
        catch (Exception ex)
        {
            // Salva in DB locale se il server non è disponibile
            Database.InsertCr(new CrRow
            {
                PcName = pcName,
                Domain = TxtCrDomain.Text.Trim(), Ou = TxtCrOu.Text.Trim(),
                AssignedUser = TxtCrAssignedUser.Text.Trim(),
                Status = "pending", CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Notes = TxtCrNotes.Text.Trim(),
            });
            TxtCrStatus.Text       = $"📴  Salvato localmente (server non disponibile): {ex.Message}";
            TxtCrStatus.Foreground = System.Windows.Media.Brushes.SteelBlue;
            TxtCrPcName.Clear();
            await LoadCrListAsync();
        }
    }

    private async void BtnCrRefresh_Click(object s, RoutedEventArgs e)
    {
        try { await LoadCrListAsync(); }
        catch (Exception ex) { App.Log($"[BtnCrRefresh_Click] {ex.Message}"); SetStatus($"❌ {ex.Message}"); }
    }

    private async Task LoadCrListAsync()
    {
        // Prova prima il server API; se non disponibile usa il DB locale
        if (!string.IsNullOrEmpty(CrApiBase))
        {
            try
            {
                string json;
                if (!_apiCache.TryGet(CrApiBase, out json))
                {
                    json = await _apiSvc!.GetCrListJsonAsync();
                    _apiCache.Set(CrApiBase, json, TimeSpan.FromSeconds(30));
                }
                var doc  = System.Text.Json.JsonDocument.Parse(json);
                var rows = new List<CrRow>();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var cr = new CrRow
                    {
                        Id           = el.TryGetProperty("id",            out var i)  ? i.GetInt32()         : 0,
                        PcName       = el.TryGetProperty("pc_name",       out var p)  ? p.GetString() ?? ""  : "",
                        Domain       = el.TryGetProperty("domain",        out var d)  ? d.GetString() ?? ""  : "",
                        Ou           = el.TryGetProperty("ou",            out var o)  ? o.GetString() ?? ""  : "",
                        AssignedUser = el.TryGetProperty("assigned_user", out var u)  ? u.GetString() ?? ""  : "",
                        Status       = el.TryGetProperty("status",        out var st) ? st.GetString() ?? "" : "",
                        CreatedAt    = el.TryGetProperty("created_at",    out var ca) ? ca.GetString() ?? "" : "",
                        Notes        = el.TryGetProperty("notes",         out var n)  ? n.GetString() ?? ""  : "",
                        LastSeen     = el.TryGetProperty("last_seen",     out var ls) ? ls.GetString() ?? "" : "",
                    };
                    rows.Add(cr);
                    Database.UpsertCr(cr);   // cache locale
                }
                CrGrid.ItemsSource = rows;
                var col = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61));
                TxtCrStatus.Text       = $"{rows.Count} richieste";
                TxtCrStatus.Foreground = col;
                return;
            }
            catch { /* fallback a DB locale */ }
        }

        // Offline: legge dal DB locale
        var local = Database.GetCrs();
        CrGrid.ItemsSource = local;
        var offline = string.IsNullOrEmpty(CrApiBase) ? "⚙️  Configura URL API nelle Impostazioni" : $"📴  Offline — {local.Count} richieste dal DB locale";
        TxtCrStatus.Text       = offline;
        TxtCrStatus.Foreground = string.IsNullOrEmpty(CrApiBase) ? System.Windows.Media.Brushes.Gold : System.Windows.Media.Brushes.SteelBlue;
    }

    private void CrGrid_SelectionChanged(object s, SelectionChangedEventArgs e) { }

    private async void MenuCrDebug_Click(object s, RoutedEventArgs e)
    {
        if (!EnsureApiConfigured()) return;
        var cr = GetSelectedCr();
        if (cr == null) return;
        try
        {
            var json = await _apiSvc!.GetCrJsonAsync(cr.Id);
            var doc  = System.Text.Json.JsonDocument.Parse(json);
            var win  = new CrDebugWindow(doc.RootElement, CrApiBase);
            win.Owner = this;
            win.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore apertura debugger: {ex.Message}", "NovaSCM",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private CrRow? GetSelectedCr() => CrGrid.SelectedItem as CrRow;

    private async void MenuCrComplete_Click(object s, RoutedEventArgs e)
        => await UpdateCrStatusAsync(GetSelectedCr(), "completed");

    private async void MenuCrInProgress_Click(object s, RoutedEventArgs e)
        => await UpdateCrStatusAsync(GetSelectedCr(), "in_progress");

    private async Task UpdateCrStatusAsync(CrRow? cr, string status)
    {
        if (cr == null) return;
        if (!EnsureApiConfigured()) return;
        try
        {
            await _apiSvc!.SetCrStatusAsync(cr.Id, status);
            _apiCache.Invalidate(CrApiBase);
            await LoadCrListAsync();
        }
        catch (Exception ex)
        {
            TxtCrStatus.Text       = $"❌  {ex.Message}";
            TxtCrStatus.Foreground = System.Windows.Media.Brushes.Tomato;
        }
    }

    private async void MenuCrDelete_Click(object s, RoutedEventArgs e)
    {
        if (!EnsureApiConfigured()) return;
        var cr = GetSelectedCr();
        if (cr == null) return;
        if (MessageBox.Show($"Eliminare CR per '{cr.PcName}'?", "Conferma",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await _apiSvc!.DeleteCrAsync(cr.Id);
            _apiCache.Invalidate(CrApiBase);
            await LoadCrListAsync();
        }
        catch (Exception ex)
        {
            TxtCrStatus.Text       = $"❌  {ex.Message}";
            TxtCrStatus.Foreground = System.Windows.Media.Brushes.Tomato;
        }
    }

    private async void MenuCrDownloadXml_Click(object s, RoutedEventArgs e)
    {
        if (!EnsureApiConfigured()) return;
        var cr = GetSelectedCr();
        if (cr == null) return;
        try
        {
            using var dlg = new System.Windows.Forms.SaveFileDialog
            {
                FileName = "autounattend.xml",
                Filter   = "XML|*.xml",
                Title    = $"Salva autounattend.xml per {cr.PcName}",
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            var xml = await _apiSvc!.GetCrXmlAsync(cr.PcName);
            File.WriteAllText(dlg.FileName, xml, System.Text.Encoding.UTF8);
            TxtCrStatus.Text       = $"💾  Salvato: {dlg.FileName}";
            TxtCrStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61));
        }
        catch (Exception ex)
        {
            TxtCrStatus.Text       = $"❌  {ex.Message}";
            TxtCrStatus.Foreground = System.Windows.Media.Brushes.Tomato;
        }
    }

    // ── Genera file USB direttamente dalla CR ─────────────────────────────────

    private async void MenuCrGenUsb_Click(object s, RoutedEventArgs e)
    {
        if (!EnsureApiConfigured()) return;
        var cr = GetSelectedCr();
        if (cr == null) return;

        // Scegli cartella di output
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description         = $"Scegli cartella per i file USB — {cr.PcName}",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        var folder = dlg.SelectedPath;
        TxtCrStatus.Text       = "⏳  Generazione file in corso...";
        TxtCrStatus.Foreground = System.Windows.Media.Brushes.Gray;

        try
        {
            // 1. Scarica autounattend.xml dal server (generato dalla CR)
            var xml = await _apiSvc!.GetCrXmlAsync(cr.PcName);
            File.WriteAllText(Path.Combine(folder, "autounattend.xml"), xml, System.Text.Encoding.UTF8);

            // 2. Genera postinstall.ps1 dalla CR
            var crJson = await _apiSvc!.GetCrJsonAsync(cr.Id);
            var crData = System.Text.Json.JsonDocument.Parse(crJson).RootElement;

            var software = new List<string>();
            if (crData.TryGetProperty("software", out var sw) && sw.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var item in sw.EnumerateArray())
                    if (item.GetString() is string p) software.Add(p);

            var cfg = new DeployConfig
            {
                PcNameTemplate  = cr.PcName,
                AdminPassword   = crData.TryGetProperty("admin_pass", out var ap) ? ap.GetString() ?? "" : "",
                WingetPackages  = software,
                IncludeAgent    = false,
                NovaSCMCrApiUrl = _config.NovaSCMApiUrl
            };
            var ps1 = BuildPostInstallScript(cfg);
            File.WriteAllText(Path.Combine(folder, "postinstall.ps1"), ps1, System.Text.Encoding.UTF8);

            // 3. Apri Explorer nella cartella
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });

            TxtCrStatus.Text       = $"✅  File generati in {folder}";
            TxtCrStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61));
        }
        catch (Exception ex)
        {
            TxtCrStatus.Text       = $"❌  {ex.Message}";
            TxtCrStatus.Foreground = System.Windows.Media.Brushes.Tomato;
        }
    }

    private void BtnDeployGenerate_Click(object s, RoutedEventArgs e)
    {
        var cfg = BuildDeployConfigFromUi();
        if (string.IsNullOrEmpty(cfg.AdminPassword))
        {
            TxtDeployStatus.Text = "⚠️  Inserisci la password Amministratore";
            TxtDeployStatus.Foreground = System.Windows.Media.Brushes.Orange;
            return;
        }

        var xml = BuildAutounattendXml(cfg);
        var ps1 = BuildPostInstallScript(cfg);

        _deployTmpDir = Path.Combine(Path.GetTempPath(), "NovaSCM_Deploy");
        Directory.CreateDirectory(_deployTmpDir);
        File.WriteAllText(Path.Combine(_deployTmpDir, "autounattend.xml"), xml, System.Text.Encoding.UTF8);
        File.WriteAllText(Path.Combine(_deployTmpDir, "postinstall.ps1"),  ps1, System.Text.Encoding.UTF8);

        TxtDeployPreview.Text = xml;

        BtnDeploySave.IsEnabled = true;
        BtnDeployUsb.IsEnabled  = true;
        BtnDeployPxe.IsEnabled  = true;

        // SEC-04: avvisa l'utente che la password di domain join è in chiaro nel file generato
        var domainWarning = cfg.DomainJoin == "AD" && !string.IsNullOrEmpty(cfg.DomainPassword)
            ? "\n⚠️  La password di join AD è scritta in chiaro nel postinstall.ps1 — proteggi il server PXE e le chiavette USB."
            : "";

        TxtDeployStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61));
        TxtDeployStatus.Text =
            $"✅  File generati — {cfg.WinEdition} · {cfg.WingetPackages.Count} software · " +
            $"{(cfg.IncludeAgent ? "agente incluso" : "senza agente")}\n" +
            "💡  Per USB: copia autounattend.xml + postinstall.ps1 nella radice della chiavetta insieme all'ISO Windows." +
            domainWarning;
        App.Log($"[Deploy] File generati in {_deployTmpDir}");
    }

    private void BtnDeploySave_Click(object s, RoutedEventArgs e)
    {
        if (_deployTmpDir == null) return;
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description         = "Seleziona cartella dove salvare autounattend.xml e postinstall.ps1",
            UseDescriptionForTitle = true,
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var dst = dlg.SelectedPath;
        File.Copy(Path.Combine(_deployTmpDir, "autounattend.xml"),
                  Path.Combine(dst,           "autounattend.xml"), overwrite: true);
        File.Copy(Path.Combine(_deployTmpDir, "postinstall.ps1"),
                  Path.Combine(dst,           "postinstall.ps1"),  overwrite: true);
        TxtDeployStatus.Text       = $"💾  File salvati in: {dst}";
        TxtDeployStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61));
        App.Log($"[Deploy] Salvati in {dst}");
    }

    private void BtnDeployUsb_Click(object s, RoutedEventArgs e)
    {
        if (_deployTmpDir == null) return;
        var removable = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Removable && d.IsReady)
            .ToList();
        if (removable.Count == 0)
        {
            MessageBox.Show("Nessuna USB rimovibile trovata. Inserisci la chiavetta e riprova.",
                "USB non trovata", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        // Se un solo drive → conferma diretta; più drive → chiede quale
        DriveInfo drive;
        if (removable.Count == 1)
        {
            var r = MessageBox.Show(
                $"Scrivi i file su {removable[0].Name} ({removable[0].VolumeLabel}  " +
                $"{removable[0].TotalSize / 1_073_741_824L} GB)?",
                "Conferma USB", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            drive = removable[0];
        }
        else
        {
            var driveList = string.Join("\n", removable.Select((d, i) =>
                $"  [{i + 1}]  {d.Name}  {d.VolumeLabel}  ({d.TotalSize / 1_073_741_824L} GB)"));
            var result = MessageBox.Show(
                $"USB disponibili:\n{driveList}\n\nVerranno copiati sulla prima: {removable[0].Name}\nProcedere?",
                "Scrivi su USB", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
            drive = removable[0];
        }
        File.Copy(Path.Combine(_deployTmpDir, "autounattend.xml"),
                  Path.Combine(drive.Name,    "autounattend.xml"), overwrite: true);
        File.Copy(Path.Combine(_deployTmpDir, "postinstall.ps1"),
                  Path.Combine(drive.Name,    "postinstall.ps1"),  overwrite: true);

        // Copia NovaSCM.exe per la schermata OSD
        var publishExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NovaSCM.exe");
        if (!File.Exists(publishExe))
            publishExe = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!,
                "NovaSCM.exe");
        if (File.Exists(publishExe))
            File.Copy(publishExe, Path.Combine(drive.Name, "NovaSCM.exe"), overwrite: true);

        TxtDeployStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61));
        TxtDeployStatus.Text =
            $"🖴  File copiati su {drive.Name}\n" +
            "Passo successivo: copia i file di Windows 11 ISO sulla stessa USB (con Rufus o 7-Zip), poi avvia il PC.";
        App.Log($"[Deploy] Copiati su USB {drive.Name}");
    }

    private async void BtnDeployPxe_Click(object s, RoutedEventArgs e)
    {
        if (_deployTmpDir == null) return;
        var cfg     = BuildDeployConfigFromUi();
        var pxeIp   = cfg.PxeServerIp;
        var pxePath = cfg.PxeServerPath;
        var keyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_ed25519");

        TxtDeployStatus.Foreground = System.Windows.Media.Brushes.Yellow;
        TxtDeployStatus.Text       = $"⏳  Upload su PXE {pxeIp}...";
        BtnDeployPxe.IsEnabled     = false;

        try
        {
            await Task.Run(() =>
            {
                foreach (var file in new[] { "autounattend.xml", "postinstall.ps1" })
                {
                    var src  = Path.Combine(_deployTmpDir!, file);
                    // SEC-03: ArgumentList evita injection da pxeIp/pxePath controllati dall'utente
                    var scpInfo = new ProcessStartInfo("scp")
                    {
                        UseShellExecute        = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        CreateNoWindow         = true,
                    };
                    scpInfo.ArgumentList.Add("-i");
                    scpInfo.ArgumentList.Add(keyPath);
                    scpInfo.ArgumentList.Add("-o");
                    scpInfo.ArgumentList.Add("StrictHostKeyChecking=accept-new");
                    scpInfo.ArgumentList.Add(src);
                    scpInfo.ArgumentList.Add($"root@{pxeIp}:{pxePath}{file}");
                    using var proc = Process.Start(scpInfo)!;
                    proc.WaitForExit(30_000);
                    if (proc.ExitCode != 0)
                        throw new Exception($"scp {file} fallito (exit {proc.ExitCode}): " +
                                            proc.StandardError.ReadToEnd());
                }
            });

            TxtDeployStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61));
            TxtDeployStatus.Text       = $"🌐  File copiati su {pxeIp}:{pxePath}";
            App.Log($"[Deploy] PXE upload OK → {pxeIp}:{pxePath}");
        }
        catch (Exception ex)
        {
            TxtDeployStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
            TxtDeployStatus.Text       = $"❌  Errore PXE: {ex.Message}";
            App.Log($"[Deploy] PXE error: {ex}");
        }
        finally { BtnDeployPxe.IsEnabled = true; }
    }

    // ── Builder autounattend.xml ──────────────────────────────────────────────
    private static string BuildAutounattendXml(DeployConfig cfg)
    {
        // Ternario locale → locale + keyboard
        var (inputLocale, kbLayout) = cfg.Locale switch
        {
            "en-US" => ("en-US", "0409:00000409"),
            "en-GB" => ("en-GB", "0809:00000809"),
            "fr-FR" => ("fr-FR", "040c:0000040c"),
            "de-DE" => ("de-DE", "0407:00000407"),
            _       => ("it-IT", "0410:00000410"),
        };

        // ComputerName: se contiene {MAC6} usiamo wildcard *, il PS1 rinominerà dopo
        var pcName   = cfg.PcNameTemplate.Contains("{MAC6}") ? "*" : cfg.PcNameTemplate;
        var isServer = cfg.WinEdition.Contains("Server");

        // Product key (opzionale — lascia vuoto per KMS/MAK/valutazione)
        var keySection = string.IsNullOrWhiteSpace(cfg.ProductKey) ? "" :
            $"      <ProductKey><Key>{cfg.ProductKey.Trim().ToUpper()}</Key></ProductKey>\n";

        // Utente standard aggiunto come secondo LocalAccount (no wrapper extra)
        var userSection = string.IsNullOrEmpty(cfg.UserName) ? "" :
            $@"
          <LocalAccount wcm:action=""add"">
            <Name>{cfg.UserName}</Name>
            <Group>Users</Group>
            <DisplayName>{cfg.UserName}</DisplayName>
            <Password><Value>{cfg.UserPassword}</Value><PlainText>true</PlainText></Password>
          </LocalAccount>";

        // OOBE: Client ha schermata skip, Server no
        // Con account Microsoft: NON nascondere HideOnlineAccountScreens (utente fa login)
        string oobeSection;
        if (isServer)
        {
            oobeSection = "      <!-- Server: nessun OOBE interattivo -->";
        }
        else if (cfg.UseMicrosoftAccount)
        {
            oobeSection = @"      <OOBE>
        <HideEULAPage>true</HideEULAPage>
        <HideOEMRegistrationScreen>true</HideOEMRegistrationScreen>
        <HideWirelessSetupInOOBE>true</HideWirelessSetupInOOBE>
        <NetworkLocation>Work</NetworkLocation>
        <ProtectYourPC>3</ProtectYourPC>
      </OOBE>";
        }
        else
        {
            oobeSection = @"      <OOBE>
        <HideEULAPage>true</HideEULAPage>
        <HideOEMRegistrationScreen>true</HideOEMRegistrationScreen>
        <HideWirelessSetupInOOBE>true</HideWirelessSetupInOOBE>
        <ProtectYourPC>3</ProtectYourPC>
      </OOBE>";
        }

        // ── RunSynchronousCommands per specialize (pre-calcolato) ─────────────
        var _rsSb  = new System.Text.StringBuilder();
        int _rsOrd = 1;
        if (!cfg.UseMicrosoftAccount && cfg.DomainJoin != "AD")
            _rsSb.Append($@"
        <RunSynchronousCommand wcm:action=""add"">
          <Order>{_rsOrd++}</Order>
          <Path>reg add HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\OOBE /v BypassNRO /t REG_DWORD /d 1 /f</Path>
          <Description>Bypass account Microsoft</Description>
        </RunSynchronousCommand>");
        if (cfg.DomainJoin == "AD" && !string.IsNullOrEmpty(cfg.DomainControllerIp))
            _rsSb.Append($@"
        <RunSynchronousCommand wcm:action=""add"">
          <Order>{_rsOrd++}</Order>
          <Path>powershell.exe -NonInteractive -Command ""for($i=0;$i-lt30;$i++){{$n=Get-NetAdapter|?{{$_.Status-eq'Up'-and$_.HardwareInterface}}|Select -First 1;if($n){{Set-DnsClientServerAddress -InterfaceIndex $n.InterfaceIndex -ServerAddresses '{cfg.DomainControllerIp}';break}};Start-Sleep 2}}""</Path>
          <Description>Attendi rete e imposta DNS DC</Description>
        </RunSynchronousCommand>");
        if (cfg.DomainJoin == "AD" && !string.IsNullOrEmpty(cfg.DomainName))
            _rsSb.Append($@"
        <RunSynchronousCommand wcm:action=""add"">
          <Order>{_rsOrd++}</Order>
          <Path>powershell.exe -NonInteractive -Command ""Add-Computer -DomainName '{cfg.DomainName}' -Credential (New-Object PSCredential('{cfg.DomainName}\{cfg.DomainUser}',(ConvertTo-SecureString '{cfg.DomainPassword}' -AsPlainText -Force))) -Force -ErrorAction SilentlyContinue""</Path>
          <Description>Join dominio AD</Description>
        </RunSynchronousCommand>");
        var runSyncBlock = _rsSb.Length > 0
            ? $@"      <RunSynchronousCommands wcm:action=""add"">{_rsSb}
      </RunSynchronousCommands>"
            : "";

        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<unattend xmlns=""urn:schemas-microsoft-com:unattend""
          xmlns:wcm=""http://schemas.microsoft.com/WMIConfig/2002/State""
          xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">

  <!-- ═══ windowsPE: disk + image selection ═══ -->
  <settings pass=""windowsPE"">
    <component name=""Microsoft-Windows-International-Core-WinPE""
               processorArchitecture=""amd64""
               publicKeyToken=""31bf3856ad364e35""
               language=""neutral"" versionScope=""nonSxS"">
      <SetupUILanguage><UILanguage>{cfg.Locale}</UILanguage></SetupUILanguage>
      <InputLocale>{inputLocale}</InputLocale>
      <SystemLocale>{cfg.Locale}</SystemLocale>
      <UILanguage>{cfg.Locale}</UILanguage>
      <UserLocale>{cfg.Locale}</UserLocale>
    </component>

    <component name=""Microsoft-Windows-Setup""
               processorArchitecture=""amd64""
               publicKeyToken=""31bf3856ad364e35""
               language=""neutral"" versionScope=""nonSxS"">
      <DiskConfiguration>
        <WillShowUI>OnError</WillShowUI>
        <Disk wcm:action=""add"">
          <DiskID>0</DiskID>
          <WillWipeDisk>true</WillWipeDisk>
          <CreatePartitions>
            <CreatePartition wcm:action=""add"">
              <Order>1</Order><Type>EFI</Type><Size>100</Size>
            </CreatePartition>
            <CreatePartition wcm:action=""add"">
              <Order>2</Order><Type>MSR</Type><Size>16</Size>
            </CreatePartition>
            <CreatePartition wcm:action=""add"">
              <Order>3</Order><Type>Primary</Type><Extend>true</Extend>
            </CreatePartition>
          </CreatePartitions>
          <ModifyPartitions>
            <ModifyPartition wcm:action=""add"">
              <Order>1</Order><PartitionID>1</PartitionID><Format>FAT32</Format><Label>System</Label>
            </ModifyPartition>
            <ModifyPartition wcm:action=""add"">
              <Order>2</Order><PartitionID>3</PartitionID><Format>NTFS</Format><Label>Windows</Label><Letter>C</Letter>
            </ModifyPartition>
          </ModifyPartitions>
        </Disk>
      </DiskConfiguration>
      <ImageInstall>
        <OSImage>
          <InstallTo><DiskID>0</DiskID><PartitionID>3</PartitionID></InstallTo>
          <InstallFrom>
            <MetaData wcm:action=""add"">
              <Key>/IMAGE/NAME</Key><Value>{cfg.WinEdition}</Value>
            </MetaData>
          </InstallFrom>
          <WillShowUI>OnError</WillShowUI>
        </OSImage>
      </ImageInstall>
      <UserData>
        <AcceptEula>true</AcceptEula>
        <FullName>Utente</FullName>
        <Organization>NovaSCM</Organization>
{keySection}      </UserData>
    </component>
  </settings>

  <!-- ═══ specialize: computer name + timezone ═══ -->
  <settings pass=""specialize"">
    <component name=""Microsoft-Windows-Shell-Setup""
               processorArchitecture=""amd64""
               publicKeyToken=""31bf3856ad364e35""
               language=""neutral"" versionScope=""nonSxS"">
      <ComputerName>{pcName}</ComputerName>
      <TimeZone>{cfg.TimeZone}</TimeZone>
      <RegisteredOrganization>NovaSCM</RegisteredOrganization>
      {runSyncBlock}
    </component>
    <component name=""Microsoft-Windows-International-Core""
               processorArchitecture=""amd64""
               publicKeyToken=""31bf3856ad364e35""
               language=""neutral"" versionScope=""nonSxS"">
      <InputLocale>{inputLocale}</InputLocale>
      <SystemLocale>{cfg.Locale}</SystemLocale>
      <UILanguage>{cfg.Locale}</UILanguage>
      <UserLocale>{cfg.Locale}</UserLocale>
    </component>
  </settings>

  <!-- ═══ oobeSystem: skip OOBE + autologon + postinstall ═══ -->
  <settings pass=""oobeSystem"">
    <component name=""Microsoft-Windows-Shell-Setup""
               processorArchitecture=""amd64""
               publicKeyToken=""31bf3856ad364e35""
               language=""neutral"" versionScope=""nonSxS"">
      {oobeSection}
      <UserAccounts>
        <LocalAccounts>
          <LocalAccount wcm:action=""add"">
            <Name>Administrator</Name>
            <Group>Administrators</Group>
            <Password>
              <Value>{cfg.AdminPassword}</Value>
              <PlainText>true</PlainText>
            </Password>
          </LocalAccount>{userSection}
        </LocalAccounts>
      </UserAccounts>
      <AutoLogon>
        <Password><Value>{cfg.AdminPassword}</Value><PlainText>true</PlainText></Password>
        <Enabled>true</Enabled>
        <LogonCount>1</LogonCount>
        <Username>Administrator</Username>
      </AutoLogon>
      <FirstLogonCommands>
        <SynchronousCommand wcm:action=""add"">
          <Order>1</Order>
          <CommandLine>{(string.IsNullOrEmpty(cfg.ServerUrl)
              ? @"cmd /c for %d in (D E F G H I J K L M N O P Q R S T U V W X Y Z) do if exist %d:\postinstall.ps1 copy /Y %d:\postinstall.ps1 C:\Windows\postinstall.ps1"
              : $@"powershell.exe -NonInteractive -ExecutionPolicy Bypass -Command ""iwr '{cfg.ServerUrl.TrimEnd('/')}/deploy/postinstall.ps1' -OutFile C:\Windows\postinstall.ps1 -UseBasicParsing""")}</CommandLine>
          <Description>NovaSCM: recupera postinstall.ps1</Description>
        </SynchronousCommand>
        <SynchronousCommand wcm:action=""add"">
          <Order>2</Order>
          <CommandLine>powershell.exe -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File C:\Windows\postinstall.ps1</CommandLine>
          <Description>NovaSCM post-install</Description>
        </SynchronousCommand>
      </FirstLogonCommands>
    </component>
  </settings>

</unattend>
<!-- Generato da NovaSCM il {DateTime.Now:yyyy-MM-dd HH:mm} -->
";
    }

    // ── Builder postinstall.ps1 ───────────────────────────────────────────────
    private static string BuildPostInstallScript(DeployConfig cfg)
    {
        var pkgLines = cfg.WingetPackages.Count == 0 ? "# (nessun pacchetto selezionato)" :
            string.Join("\n", cfg.WingetPackages.Select(p =>
                $"Report-Step 'install_{p}' 'running'\n" +
                $"winget install --id {p} --silent --accept-package-agreements --accept-source-agreements 2>&1 | Write-Output\n" +
                $"Report-Step 'install_{p}' 'done'"));

        // Funzione Report-Step (telemetria step-by-step verso NovaSCM API)
        var reportStepFn = !string.IsNullOrEmpty(cfg.NovaSCMCrApiUrl) ? $@"
$_crApi    = '{cfg.NovaSCMCrApiUrl}'
$_hostname = $env:COMPUTERNAME
function Report-Step {{
    param([string]$Step, [string]$Status = 'done')
    if (-not $_crApi) {{ return }}
    try {{
        $b = [Text.Encoding]::UTF8.GetBytes(
            (ConvertTo-Json @{{step=$Step;status=$Status;ts=(Get-Date -Format 'o')}} -Compress))
        $r = [Net.WebRequest]::Create(""$_crApi/by-name/$_hostname/step"")
        $r.Method = 'POST'; $r.ContentType = 'application/json'
        $r.ContentLength = $b.Length; $r.Timeout = 5000
        $s = $r.GetRequestStream(); $s.Write($b,0,$b.Length); $s.Close()
        $r.GetResponse().Close()
    }} catch {{}}
}}" : "function Report-Step { param([string]$Step, [string]$Status = 'done') }  # API non configurata";

        var renamePc = cfg.PcNameTemplate.Contains("{MAC6}") ? @"
# Rinomina PC con ultimi 6 hex del MAC del primo adapter fisico
$adapter = Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and $_.HardwareInterface } |
           Sort-Object InterfaceIndex | Select-Object -First 1
if ($adapter) {
    $mac6 = ($adapter.MacAddress -replace '[:\-]','').Substring(6).ToUpper()
    $newName = '" + cfg.PcNameTemplate.Replace("{MAC6}", "' + $mac6 + '") + @"'
    if ($env:COMPUTERNAME -ne $newName) {
        Rename-Computer -NewName $newName -Force -ErrorAction SilentlyContinue
        Write-Output ""PC rinominato: $newName""
    }
}" : $@"
# Nome PC fisso: {cfg.PcNameTemplate} (impostato dall'autounattend)";

        var domainSection = cfg.DomainJoin switch
        {
            "AzureAD" => $@"
# Azure AD Join
Write-Output 'Unione ad Azure AD in corso...'
try {{
    $dsreg = 'C:\Windows\System32\dsregcmd.exe'
    {(string.IsNullOrEmpty(cfg.AzureTenantId) ? "" : $"$env:AAD_TENANT_ID = '{cfg.AzureTenantId}'")}
    & $dsreg /join 2>&1 | Write-Output
    Write-Output 'Azure AD join avviato — completare il login al primo avvio'
}} catch {{
    Write-Warning ""Azure AD join fallito: $($_.Exception.Message)""
}}",
            "AD" => $"# Unione a {cfg.DomainName} già completata in fase di setup (autounattend specialize)",
            _    => "# Nessun dominio configurato (Workgroup)"
        };

        var checkinSection = !string.IsNullOrEmpty(cfg.NovaSCMCrApiUrl) ? $@"
# Check-in NovaSCM — registra completamento installazione
try {{
    $hostname = $env:COMPUTERNAME
    $body = ConvertTo-Json @{{ hostname=$hostname; event='postinstall_done'; timestamp=(Get-Date -Format 'o') }}
    Invoke-RestMethod -Uri '{cfg.NovaSCMCrApiUrl}/by-name/$hostname/checkin' -Method POST `
        -Body $body -ContentType 'application/json' -UseBasicParsing -ErrorAction Stop
    Write-Output 'Check-in NovaSCM: OK'
}} catch {{
    Write-Warning ""Check-in NovaSCM non riuscito (continua comunque): $($_.Exception.Message)""
}}" : "# NovaSCM API non configurata — skip check-in";

        // Agente WiFi EAP-TLS (esistente)
        var agentSection = cfg.IncludeAgent && !string.IsNullOrEmpty(cfg.ServerUrl) ? $@"
# Installa agente NovaSCM (enrollment WiFi EAP-TLS)
try {{
    $agentUrl = '{cfg.ServerUrl.TrimEnd('/')}/agent/install.ps1'
    Write-Output ""Download agente da: $agentUrl""
    Invoke-RestMethod -Uri $agentUrl -UseBasicParsing | Invoke-Expression
    Write-Output 'Agente NovaSCM installato'
}} catch {{
    Write-Warning ""Agente NovaSCM non raggiungibile: $($_.Exception.Message)""
}}" : "# Agente WiFi EAP-TLS non incluso";

        // NovaSCM Workflow Agent (nuovo — installa il servizio di polling workflow)
        var novaSCMBaseUrl = !string.IsNullOrEmpty(cfg.NovaSCMCrApiUrl)
            ? cfg.NovaSCMCrApiUrl.Replace("/api/cr", "").TrimEnd('/')
            : (!string.IsNullOrEmpty(cfg.ServerUrl) ? cfg.ServerUrl.TrimEnd('/') : "");
        var workflowAgentSection = !string.IsNullOrEmpty(novaSCMBaseUrl) ? $@"
# Installa NovaSCM Workflow Agent (servizio di esecuzione workflow)
Report-Step 'workflow_agent_install' 'running'
try {{
    $wfAgentInstaller = '{novaSCMBaseUrl}/api/download/agent-install.ps1'
    Write-Output ""Download NovaSCM Workflow Agent da: $wfAgentInstaller""
    $installerScript = (Invoke-WebRequest -Uri $wfAgentInstaller -UseBasicParsing).Content
    $installerScript = $installerScript -replace '\$ApiUrl\s*=.*', '$ApiUrl = ""{novaSCMBaseUrl}""'
    Invoke-Expression $installerScript
    Write-Output 'NovaSCM Workflow Agent installato come servizio Windows'
    Report-Step 'workflow_agent_install' 'done'
}} catch {{
    Write-Warning ""NovaSCM Workflow Agent non installato: $($_.Exception.Message)""
    Report-Step 'workflow_agent_install' 'error'
}}" : "# NovaSCM Workflow Agent: API URL non configurato — skip";

        return $@"#Requires -RunAsAdministrator
# ============================================================
# postinstall.ps1 — generato da NovaSCM il {DateTime.Now:yyyy-MM-dd HH:mm}
# Eseguito automaticamente al primo avvio post-installazione
# ============================================================
Set-ExecutionPolicy Bypass -Scope Process -Force
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
{reportStepFn}

$logFile = 'C:\Windows\Temp\novascm_postinstall.log'
Start-Transcript -Path $logFile -Append

Write-Output '=== NovaSCM Post-Install avviato ==='

# Lancia schermata OSD dalla USB (se NovaSCM.exe presente)
$usbDrive = $null
foreach ($d in @('D','E','F','G','H','I','J','K','L','M','N','O','P','Q','R','S','T','U','V','W','X','Y','Z')) {{
    if (Test-Path ""${{d}}:\NovaSCM.exe"") {{ $usbDrive = ""${{d}}:""; break }}
}}
if ($usbDrive) {{
    $osdExe = 'C:\Windows\Temp\NovaSCM-OSD.exe'
    Copy-Item ""$usbDrive\NovaSCM.exe"" $osdExe -Force -ErrorAction SilentlyContinue
    if (Test-Path $osdExe) {{
        $apiArg = if ($_crApi) {{ ""--osd $env:COMPUTERNAME $_crApi"" }} else {{ ""--osd $env:COMPUTERNAME"" }}
        Start-Process $osdExe $apiArg -WindowStyle Normal -ErrorAction SilentlyContinue
        Start-Sleep 2
    }}
}}

Report-Step 'postinstall_start'
{renamePc}
Report-Step 'rename_pc'
{domainSection}

# Installa winget se mancante (Windows 10 — Win11 ce l'ha già)
if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {{
    Write-Output 'Installazione winget...'
    Report-Step 'winget_install' 'running'
    $wgUrl = 'https://github.com/microsoft/winget-cli/releases/latest/download/Microsoft.DesktopAppInstaller_8wekyb3d8bbwe.msixbundle'
    $wgTmp = ""$env:TEMP\winget.msixbundle""
    Invoke-WebRequest -Uri $wgUrl -OutFile $wgTmp -UseBasicParsing
    Add-AppxPackage -Path $wgTmp -ErrorAction SilentlyContinue
    Report-Step 'winget_install' 'done'
}}

# Installa software
Write-Output 'Installazione software...'
{pkgLines}

Report-Step 'agent_install' 'running'
{agentSection}
Report-Step 'agent_install' 'done'

{workflowAgentSection}

{checkinSection}

Write-Output '=== Post-install completato ==='
Stop-Transcript

# Riavvio finale (dopo 15 secondi)
Start-Sleep -Seconds 5
shutdown /r /t 15 /c ""NovaSCM: configurazione completata. Riavvio in 15 secondi.""
";
    }

    // ── Workflow Tab ──────────────────────────────────────────────────────────

    private void InitWorkflowTab()
    {
        LstWorkflows.ItemsSource      = _wfRows;
        GridWfSteps.ItemsSource       = _wfStepRows;
        GridWfAssignments.ItemsSource = _wfAssignRows;
    }

    private async Task LoadWorkflowsAsync()
    {
        if (string.IsNullOrEmpty(WfApiBase))
        {
            TxtWfStatus.Text = "⚙️  Configura l'URL API NovaSCM nelle Impostazioni";
            TxtWfStatus.Foreground = System.Windows.Media.Brushes.Gold;
            return;
        }
        try
        {
            var json = await _apiSvc!.GetWorkflowsJsonAsync();
            var doc  = System.Text.Json.JsonDocument.Parse(json);

            var prevId = _selectedWfId;
            _wfRows.Clear();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var wf = new WfRow
                {
                    Id          = el.TryGetProperty("id",          out var i)  ? i.GetInt32()        : 0,
                    Nome        = el.TryGetProperty("nome",        out var n)  ? n.GetString() ?? "" : "",
                    Descrizione = el.TryGetProperty("descrizione", out var d)  ? d.GetString() ?? "" : "",
                    Versione    = el.TryGetProperty("versione",    out var v)  ? v.GetInt32()        : 1,
                    StepCount   = el.TryGetProperty("steps",       out var st) ? st.GetArrayLength() : 0,
                };
                _wfRows.Add(wf);
                Database.UpsertWorkflow(wf);   // cache locale
            }

            // Riseleziona il workflow precedente se ancora presente
            if (prevId > 0)
            {
                var found = _wfRows.FirstOrDefault(w => w.Id == prevId);
                if (found != null) LstWorkflows.SelectedItem = found;
            }

            TxtWfStatus.Text = $"✅  {_wfRows.Count} workflow";
            TxtWfStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61));
        }
        catch
        {
            // Fallback: carica dal DB locale
            _wfRows.Clear();
            foreach (var wf in Database.GetWorkflows()) _wfRows.Add(wf);
            TxtWfStatus.Text       = _wfRows.Count > 0
                ? $"📴  Offline — {_wfRows.Count} workflow dal DB locale"
                : "⚙️  Configura l'URL API NovaSCM nelle Impostazioni";
            TxtWfStatus.Foreground = _wfRows.Count > 0
                ? System.Windows.Media.Brushes.SteelBlue
                : System.Windows.Media.Brushes.Gold;
        }
    }

    private async Task LoadWorkflowStepsAsync(int wfId)
    {
        _wfStepRows.Clear();
        if (string.IsNullOrEmpty(WfApiBase) || wfId <= 0) return;
        try
        {
            var json = await _apiSvc!.GetWorkflowDetailJsonAsync(wfId);
            var doc  = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("steps", out var stepsEl)) return;
            foreach (var el in stepsEl.EnumerateArray())
            {
                _wfStepRows.Add(new WfStepRow
                {
                    Id         = el.TryGetProperty("id",        out var i)  ? i.GetInt32()         : 0,
                    WorkflowId = wfId,
                    Ordine     = el.TryGetProperty("ordine",    out var o)  ? o.GetInt32()         : 0,
                    Nome       = el.TryGetProperty("nome",      out var n)  ? n.GetString() ?? ""  : "",
                    Tipo       = el.TryGetProperty("tipo",      out var t)  ? t.GetString() ?? ""  : "",
                    Parametri  = el.TryGetProperty("parametri", out var p)  ? p.GetString() ?? "{}" : "{}",
                    Platform   = el.TryGetProperty("platform",  out var pl) ? pl.GetString() ?? "all" : "all",
                    SuErrore   = el.TryGetProperty("su_errore", out var se) ? se.GetString() ?? "stop" : "stop",
                });
            }
        }
        catch { /* ignora errori di rete durante il refresh */ }
        RefreshWfTimeline();
    }

    // UI-06: aggiorna la timeline orizzontale degli step
    private void RefreshWfTimeline()
    {
        var items = _wfStepRows
            .OrderBy(s => s.Ordine)
            .Select((s, idx) => new WfTimelineItem
            {
                Ordine    = s.Ordine,
                ShortNome = s.Nome.Length > 8 ? s.Nome[..8] + "…" : s.Nome,
                BubbleColor = s.Tipo switch
                {
                    var t when t.StartsWith("winget")   => "#7C3AED",
                    var t when t.StartsWith("ps")       => "#1D4ED8",
                    var t when t.StartsWith("shell")    => "#0F766E",
                    var t when t.StartsWith("reboot")   => "#B45309",
                    _                                   => "#3B82F6",
                },
                ConnectorVisibility = idx == 0
                    ? System.Windows.Visibility.Collapsed
                    : System.Windows.Visibility.Visible,
            })
            .ToList();
        WfTimeline.ItemsSource = items;
    }

    // ── FEAT-04: Workflow drag-and-drop step reordering ───────────────────────
    private WfStepRow? _draggedStep;
    private System.Windows.Point _dragStartPoint;

    private void GridWfSteps_PreviewMouseLeftButtonDown(object s, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        var row = FindVisualParent<DataGridRow>((DependencyObject)e.OriginalSource);
        _draggedStep = row?.Item as WfStepRow;
    }

    private void GridWfSteps_DragOver(object s, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(WfStepRow)))
        { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private async void GridWfSteps_Drop(object s, DragEventArgs e)
    {
        if (_draggedStep == null || !e.Data.GetDataPresent(typeof(WfStepRow))) return;
        var targetRow = FindVisualParent<DataGridRow>((DependencyObject)e.OriginalSource);
        if (targetRow?.Item is not WfStepRow target || target == _draggedStep) { _draggedStep = null; return; }

        // Riordina in locale
        var from = _wfStepRows.IndexOf(_draggedStep);
        var to   = _wfStepRows.IndexOf(target);
        if (from < 0 || to < 0) { _draggedStep = null; return; }

        _wfStepRows.Move(from, to);
        // Ricalcola numeri d'ordine
        for (int i = 0; i < _wfStepRows.Count; i++)
            _wfStepRows[i].Ordine = i + 1;
        RefreshWfTimeline();

        // Salva il nuovo ordine sull'API
        if (string.IsNullOrEmpty(WfApiBase) || _selectedWfId <= 0) { _draggedStep = null; return; }
        try
        {
            foreach (var step in _wfStepRows)
            {
                await _apiSvc!.PutWorkflowStepAsync(_selectedWfId, step.Id, new
                {
                    nome      = step.Nome,
                    tipo      = step.Tipo,
                    parametri = step.Parametri,
                    platform  = step.Platform,
                    su_errore = step.SuErrore,
                    ordine    = step.Ordine,
                });
            }
            TxtWfStatus.Text       = "✅  Ordine step salvato";
            TxtWfStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(21, 128, 61));
        }
        catch { /* non critico — l'ordine locale rimane corretto */ }
        _draggedStep = null;
    }

    private void GridWfSteps_PreviewMouseMove(object s, System.Windows.Input.MouseEventArgs e)
    {
        if (_draggedStep == null || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
        var pos  = e.GetPosition(null);
        var diff = _dragStartPoint - pos;
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            DragDrop.DoDragDrop(GridWfSteps, _draggedStep, DragDropEffects.Move);
        }
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T typed) return typed;
            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private async Task LoadWorkflowAssignmentsAsync()
    {
        if (string.IsNullOrEmpty(WfApiBase)) return;
        try
        {
            var json = await _apiSvc!.GetPcWorkflowsJsonAsync();
            var doc  = System.Text.Json.JsonDocument.Parse(json);
            _wfAssignRows.Clear();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var status   = el.TryGetProperty("status",       out var st) ? st.GetString() ?? "" : "";
                var progress = status == "completed" ? 100 : 0;
                var a = new WfAssignRow
                {
                    Id           = el.TryGetProperty("id",            out var i)  ? i.GetInt32()         : 0,
                    PcName       = el.TryGetProperty("pc_name",       out var p)  ? p.GetString() ?? ""  : "",
                    WorkflowNome = el.TryGetProperty("workflow_nome", out var wn) ? wn.GetString() ?? "" : "",
                    WorkflowId   = el.TryGetProperty("workflow_id",   out var wi) ? wi.GetInt32()        : 0,
                    Status       = status,
                    Progress     = progress,
                    AssignedAt   = el.TryGetProperty("assigned_at",   out var at) ? at.GetString() ?? "" : "",
                    LastSeen     = el.TryGetProperty("last_seen",     out var ls) ? ls.GetString() ?? "" : "",
                };
                _wfAssignRows.Add(a);
                Database.UpsertAssignment(a);   // cache locale
            }
        }
        catch
        {
            // Fallback: carica dal DB locale
            _wfAssignRows.Clear();
            foreach (var a in Database.GetAssignments()) _wfAssignRows.Add(a);
        }
    }

    private async void LstWorkflows_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        try
        {
            if (LstWorkflows.SelectedItem is WfRow wf)
            {
                _selectedWfId = wf.Id;
                await LoadWorkflowStepsAsync(wf.Id);
            }
            else
            {
                _wfStepRows.Clear();
            }
        }
        catch (Exception ex) { App.Log($"[LstWorkflows_SelectionChanged] {ex}"); }
    }

    private async void BtnWfNew_Click(object s, RoutedEventArgs e)
    {
        var win = new WfNameWindow("Nuovo Workflow", "", "");
        if (win.ShowDialog() != true || string.IsNullOrEmpty(win.WfNome)) return;
        if (string.IsNullOrEmpty(WfApiBase)) return;
        try
        {
            await _apiSvc!.PostWorkflowAsync(new { nome = win.WfNome, descrizione = win.WfDesc });
            await LoadWorkflowsAsync();
            TxtWfStatus.Text = $"✅  Workflow '{win.WfNome}' creato";
            TxtWfStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61));
        }
        catch (Exception ex)
        {
            TxtWfStatus.Text = $"❌  {ex.Message}";
            TxtWfStatus.Foreground = System.Windows.Media.Brushes.Tomato;
        }
    }

    private async void BtnWfRename_Click(object s, RoutedEventArgs e)
    {
        if (LstWorkflows.SelectedItem is not WfRow wf)
        {
            MessageBox.Show("Seleziona un workflow.", "NovaSCM", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var win = new WfNameWindow("Modifica Workflow", wf.Nome, wf.Descrizione);
        if (win.ShowDialog() != true || string.IsNullOrEmpty(win.WfNome)) return;
        try
        {
            await _apiSvc!.PutWorkflowAsync(wf.Id, new { nome = win.WfNome, descrizione = win.WfDesc });
            await LoadWorkflowsAsync();
        }
        catch (Exception ex)
        {
            TxtWfStatus.Text = $"❌  {ex.Message}";
            TxtWfStatus.Foreground = System.Windows.Media.Brushes.Tomato;
        }
    }

    private async void BtnWfDelete_Click(object s, RoutedEventArgs e)
    {
        if (LstWorkflows.SelectedItem is not WfRow wf)
        {
            MessageBox.Show("Seleziona un workflow.", "NovaSCM", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"Eliminare il workflow \"{wf.Nome}\"?\n\nAttenzione: verranno eliminati anche tutti i suoi step.",
            "Conferma eliminazione", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await _apiSvc!.DeleteWorkflowAsync(wf.Id);
            _selectedWfId = -1;
            _wfStepRows.Clear();
            await LoadWorkflowsAsync();
        }
        catch (Exception ex)
        {
            TxtWfStatus.Text = $"❌  {ex.Message}";
            TxtWfStatus.Foreground = System.Windows.Media.Brushes.Tomato;
        }
    }

    private async void BtnWfRefresh_Click(object s, RoutedEventArgs e)
    {
        await LoadWorkflowsAsync();
        await LoadWorkflowAssignmentsAsync();
        if (_selectedWfId > 0) await LoadWorkflowStepsAsync(_selectedWfId);
    }

    private async void BtnWfAddStep_Click(object s, RoutedEventArgs e)
    {
        if (!EnsureApiConfigured()) return;
        if (LstWorkflows.SelectedItem is not WfRow wf)
        {
            MessageBox.Show("Seleziona prima un workflow dalla lista.", "NovaSCM", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var nextOrdine = _wfStepRows.Count > 0 ? _wfStepRows.Max(st => st.Ordine) + 1 : 1;
        var win = new WorkflowStepWindow(null, nextOrdine);
        if (win.ShowDialog() != true || win.Result == null) return;
        try
        {
            await _apiSvc!.PostWorkflowStepAsync(wf.Id, new
            {
                ordine    = win.Result.Ordine,
                nome      = win.Result.Nome,
                tipo      = win.Result.Tipo,
                parametri = win.Result.Parametri,
                platform  = win.Result.Platform,
                su_errore = win.Result.SuErrore,
            });
            await LoadWorkflowStepsAsync(wf.Id);
            // Aggiorna il conteggio step nella lista
            wf.StepCount = _wfStepRows.Count;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "NovaSCM", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnWfEditStep_Click(object s, RoutedEventArgs e)
    {
        if (!EnsureApiConfigured()) return;
        if (GridWfSteps.SelectedItem is not WfStepRow step)
        {
            MessageBox.Show("Seleziona uno step da modificare.", "NovaSCM", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (LstWorkflows.SelectedItem is not WfRow wf) return;
        var win = new WorkflowStepWindow(step, step.Ordine);
        if (win.ShowDialog() != true || win.Result == null) return;
        try
        {
            await _apiSvc!.PutWorkflowStepAsync(wf.Id, step.Id, new
            {
                ordine    = win.Result.Ordine,
                nome      = win.Result.Nome,
                tipo      = win.Result.Tipo,
                parametri = win.Result.Parametri,
                platform  = win.Result.Platform,
                su_errore = win.Result.SuErrore,
            });
            await LoadWorkflowStepsAsync(wf.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "NovaSCM", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnWfStepUp_Click(object s, RoutedEventArgs e)
    {
        try
        {
        if (GridWfSteps.SelectedItem is not WfStepRow step) return;
        if (LstWorkflows.SelectedItem is not WfRow wf) return;
        var prev = _wfStepRows.Where(st => st.Ordine < step.Ordine).OrderByDescending(st => st.Ordine).FirstOrDefault();
        if (prev == null) return;
        await SwapStepOrdineAsync(wf.Id, step, prev);
        }
        catch (Exception ex) { App.Log($"[BtnWfStepUp_Click] {ex.Message}"); SetStatus($"❌ {ex.Message}"); }
    }

    private async void BtnWfStepDown_Click(object s, RoutedEventArgs e)
    {
        try
        {
        if (GridWfSteps.SelectedItem is not WfStepRow step) return;
        if (LstWorkflows.SelectedItem is not WfRow wf) return;
        var next = _wfStepRows.Where(st => st.Ordine > step.Ordine).OrderBy(st => st.Ordine).FirstOrDefault();
        if (next == null) return;
        await SwapStepOrdineAsync(wf.Id, step, next);
        }
        catch (Exception ex) { App.Log($"[BtnWfStepDown_Click] {ex.Message}"); SetStatus($"❌ {ex.Message}"); }
    }

    private async Task SwapStepOrdineAsync(int wfId, WfStepRow a, WfStepRow b)
    {
        if (!EnsureApiConfigured()) return;
        // Usa ordine temporaneo 9999 per evitare il constraint UNIQUE(workflow_id, ordine)
        object StepBody(WfStepRow st, int ord) => new
            { ordine = ord, nome = st.Nome, tipo = st.Tipo, parametri = st.Parametri, platform = st.Platform, su_errore = st.SuErrore };
        await _apiSvc!.PutWorkflowStepAsync(wfId, a.Id, StepBody(a, 9999));
        await _apiSvc!.PutWorkflowStepAsync(wfId, b.Id, StepBody(b, a.Ordine));
        await _apiSvc!.PutWorkflowStepAsync(wfId, a.Id, StepBody(a, b.Ordine));
        await LoadWorkflowStepsAsync(wfId);
    }

    private async void BtnWfDeleteStep_Click(object s, RoutedEventArgs e)
    {
        if (!EnsureApiConfigured()) return;
        try
        {
        if (GridWfSteps.SelectedItem is not WfStepRow step)
        {
            MessageBox.Show("Seleziona uno step.", "NovaSCM", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (LstWorkflows.SelectedItem is not WfRow wf) return;
        if (MessageBox.Show($"Eliminare lo step \"{step.Nome}\"?", "Conferma",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await _apiSvc!.DeleteWorkflowStepAsync(wf.Id, step.Id);
        await LoadWorkflowStepsAsync(wf.Id);
        wf.StepCount = _wfStepRows.Count;
        }
        catch (Exception ex) { App.Log($"[BtnWfDeleteStep_Click] {ex.Message}"); SetStatus($"❌ {ex.Message}"); }
    }

    private async void BtnWfAssign_Click(object s, RoutedEventArgs e)
    {
        if (!EnsureApiConfigured()) return;
        var win = new WfAssignWindow(_wfRows.ToList());
        if (win.ShowDialog() != true || string.IsNullOrEmpty(win.PcName) || win.WorkflowId <= 0) return;
        try
        {
            await _apiSvc!.PostPcWorkflowAsync(new
                { pc_name = win.PcName.ToUpperInvariant(), workflow_id = win.WorkflowId });
            await LoadWorkflowAssignmentsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "NovaSCM", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnWfAssignDetail_Click(object s, RoutedEventArgs e)
    {
        if (GridWfAssignments.SelectedItem is not WfAssignRow assign)
        {
            MessageBox.Show("Seleziona un'assegnazione.", "NovaSCM", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var win = new WfAssignDetailWindow(assign.Id, assign.PcName, assign.WorkflowNome, WfApiBase);
        win.Show();
    }

    private async void BtnWfDeleteAssign_Click(object s, RoutedEventArgs e)
    {
        if (!EnsureApiConfigured()) return;
        try
        {
        if (GridWfAssignments.SelectedItem is not WfAssignRow assign)
        {
            MessageBox.Show("Seleziona un'assegnazione.", "NovaSCM", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"Eliminare l'assegnazione {assign.PcName} → {assign.WorkflowNome}?",
            "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await _apiSvc!.DeletePcWorkflowAsync(assign.Id);
        await LoadWorkflowAssignmentsAsync();
        }
        catch (Exception ex) { App.Log($"[BtnWfDeleteAssign_Click] {ex.Message}"); SetStatus($"❌ {ex.Message}"); }
    }

    private async void BtnWfRefreshAssign_Click(object s, RoutedEventArgs e)
    {
        try { await LoadWorkflowAssignmentsAsync(); }
        catch (Exception ex) { App.Log($"[BtnWfRefreshAssign_Click] {ex.Message}"); SetStatus($"❌ {ex.Message}"); }
    }

    // ── Supporta il progetto ──────────────────────────────────────────────────
    private void BtnKofi_Click(object s, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo("https://ko-fi.com/polariscore") { UseShellExecute = true });

    private void BtnGitHubSponsor_Click(object s, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo("https://github.com/sponsors/claudiobecchis") { UseShellExecute = true });

    private void BtnPayPal_Click(object s, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo("https://paypal.me/CBECCHIS") { UseShellExecute = true });

    // ── KONAMI CODE ───────────────────────────────────────────────────────────
    private static readonly System.Windows.Input.Key[] KonamiSequence =
    [
        System.Windows.Input.Key.Up,   System.Windows.Input.Key.Up,
        System.Windows.Input.Key.Down, System.Windows.Input.Key.Down,
        System.Windows.Input.Key.Left, System.Windows.Input.Key.Right,
        System.Windows.Input.Key.Left, System.Windows.Input.Key.Right,
        System.Windows.Input.Key.B,    System.Windows.Input.Key.A
    ];
    private readonly List<System.Windows.Input.Key> _konamiInput = [];
    private bool _konamiActive = false;

    private void Window_PreviewKeyDown(object s, System.Windows.Input.KeyEventArgs e)
    {
        var ctrl  = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control);
        var shift = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift);

        // DX-03 + FEAT-03: shortcut tastiera
        if (ctrl && e.Key == System.Windows.Input.Key.K)
        {
            if (SearchOverlay.Visibility == Visibility.Visible) CloseSearch();
            else OpenSearch();
            e.Handled = true; return;
        }
        if (e.Key == System.Windows.Input.Key.Escape && SearchOverlay.Visibility == Visibility.Visible)
        {
            CloseSearch(); e.Handled = true; return;
        }
        if ((e.Key == System.Windows.Input.Key.F5) ||
            (ctrl && e.Key == System.Windows.Input.Key.R))
        {
            // F5 / Ctrl+R — aggiorna tab corrente
            var header = (MainTabs.SelectedItem as TabItem)?.Header?.ToString() ?? "";
            if (header.Contains("Rete"))         BtnScan_Click(s, new RoutedEventArgs());
            else if (header.Contains("Richieste")) _ = LoadCrListAsync();
            else if (header.Contains("Workflow"))  _ = LoadWorkflowsAsync();
            else if (header.Contains("SCCM"))      BtnSccmRefresh_Click(s, new RoutedEventArgs());
            else if (header.Contains("Dashboard")) _ = RefreshDashboardAsync();
            e.Handled = true; return;
        }
        // Ctrl+1..9 → naviga al tab N
        if (ctrl && e.Key >= System.Windows.Input.Key.D1 && e.Key <= System.Windows.Input.Key.D9)
        {
            var idx = e.Key - System.Windows.Input.Key.D1;
            if (idx < MainTabs.Items.Count) MainTabs.SelectedIndex = idx;
            e.Handled = true; return;
        }
        // Ctrl+N → nuova CR
        if (ctrl && e.Key == System.Windows.Input.Key.N)
        {
            BtnCrCreate_Click(s, new RoutedEventArgs());
            e.Handled = true; return;
        }

        // Konami
        _konamiInput.Add(e.Key);
        if (_konamiInput.Count > KonamiSequence.Length)
            _konamiInput.RemoveAt(0);
        if (!_konamiActive && _konamiInput.Count == KonamiSequence.Length &&
            _konamiInput.SequenceEqual(KonamiSequence))
            _ = ActivateKonamiAsync();
    }

    private async Task ActivateKonamiAsync()
    {
        _konamiActive = true;
        _konamiInput.Clear();

        // Avvia matrix rain nell'overlay
        KonamiOverlay.Visibility = Visibility.Visible;
        KonamiCanvas.Background  = System.Windows.Media.Brushes.Transparent;

        string[] msgs = [
            "ACCESSO ILLIMITATO SBLOCCATO 🚀",
            "SEI UN VERO SYSADMIN 🏆",
            "POLARISCORE MODE: ON ⚡",
            "COFFEE++ ☕  BUGS-- 🐛"
        ];
        TxtKonamiSub.Text = msgs[new Random().Next(msgs.Length)];

        // Glitch animation: flash 3 volte
        for (int i = 0; i < 3; i++)
        {
            KonamiOverlay.Opacity = 0.4;
            await Task.Delay(80);
            KonamiOverlay.Opacity = 1.0;
            await Task.Delay(80);
        }

        // Matrix rain nel canvas overlay
        await Task.Delay(3500);

        // Fade out
        var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0,
            new Duration(TimeSpan.FromMilliseconds(600)));
        fade.Completed += (_, _) =>
        {
            KonamiOverlay.Visibility = Visibility.Collapsed;
            KonamiOverlay.Opacity    = 1;
            KonamiCanvas.Children.Clear();
            _konamiActive = false;
        };
        KonamiOverlay.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    // ── System Gauges (PC tab) ─────────────────────────────────────────────────
    private System.Windows.Threading.DispatcherTimer? _gaugeTimer;
    private System.Diagnostics.PerformanceCounter? _cpuCounter;
    private long _lastNetBytes; // cache per gauge NET — evita GetAllNetworkInterfaces() ogni 1.5s

    private void StartGauges()
    {
        // PerformanceCounter può bloccarsi secondi sul thread UI — init su background
        _ = Task.Run(() =>
        {
            try { _cpuCounter = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total"); }
            catch { _cpuCounter = null; }
        });

        // Cache iniziale NET su background
        _ = Task.Run(() =>
        {
            try
            {
                _lastNetBytes = System.Net.NetworkInformation.NetworkInterface
                    .GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                    .Sum(n => n.GetIPStatistics().BytesSent + n.GetIPStatistics().BytesReceived);
            }
            catch { }
        });

        _gaugeTimer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromSeconds(2) };
        _gaugeTimer.Tick += (_, _) => UpdateGauges();
        _gaugeTimer.Start();
    }

    private void StopGauges()
    {
        _gaugeTimer?.Stop();
        _gaugeTimer = null;
    }

    private void UpdateGauges()
    {
        float cpu = 0;
        try { cpu = _cpuCounter?.NextValue() ?? 0; }
        catch { }

        long usedBytes = Environment.WorkingSet;
        float ramUsed  = (float)(usedBytes / (1024.0 * 1024.0));

        float disk = 0;
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            disk = (float)(1.0 - (double)drive.AvailableFreeSpace / drive.TotalSize) * 100;
        }
        catch { }

        // NET: usa valore cached (aggiornato ogni 30s su background)
        float net = Math.Min(100, (float)(_lastNetBytes / 1e9 % 100));

        DrawArcGauge(GaugeCpu,  cpu,  100, "CPU",  "#3b82f6", $"{cpu:F0}%");
        DrawArcGauge(GaugeRam,  Math.Min(100, ramUsed / 40 * 100), 100, "RAM",  "#10b981", $"{ramUsed:F0} MB");
        DrawArcGauge(GaugeDisk, disk, 100, "Disco","#f59e0b", $"{disk:F0}%");
        DrawArcGauge(GaugeNet,  net,  100, "NET",  "#a78bfa", "● " + (net > 10 ? "Attivo" : "Idle"));

        // Aggiorna NET in background ogni ~30s (ogni 15 tick da 2s)
        if (DateTime.Now.Second % 30 < 2)
            _ = Task.Run(() =>
            {
                try
                {
                    _lastNetBytes = System.Net.NetworkInformation.NetworkInterface
                        .GetAllNetworkInterfaces()
                        .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                        .Sum(n => n.GetIPStatistics().BytesSent + n.GetIPStatistics().BytesReceived);
                }
                catch { }
            });
    }

    private static void DrawArcGauge(Canvas canvas, float value, float max,
                                     string label, string hexColor, string valText)
    {
        canvas.Children.Clear();
        double w = canvas.ActualWidth;
        double h = canvas.ActualHeight;
        if (w < 10 || h < 10) { canvas.Dispatcher.BeginInvoke(() => DrawArcGauge(canvas, value, max, label, hexColor, valText)); return; }

        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor);
        double cx    = w / 2;
        double cy    = h * 0.6;
        double r     = Math.Min(w, h) * 0.38;
        double start = 210 * Math.PI / 180;  // -150 gradi
        double end   = 330 * Math.PI / 180;  // +150 gradi
        double span  = (end - start + 2 * Math.PI) % (2 * Math.PI);  // 300 gradi totali
        double pct   = Math.Clamp(value / max, 0, 1);

        // Track grigio
        var track = ArcPath(cx, cy, r, start, start + span * Math.PI * 2 / Math.PI, color, 0.15);
        // Hmm, let me use a simpler arc approach

        // Track arc (background) — always full 300°
        DrawArc(canvas, cx, cy, r + 2, 210, 330, System.Windows.Media.Color.FromArgb(40, 100, 116, 139), 10);

        // Value arc — proportional
        if (pct > 0.01)
        {
            // Color based on value
            var arcColor = pct < 0.6 ? color
                         : pct < 0.85 ? System.Windows.Media.Color.FromRgb(245, 158, 11)
                                       : System.Windows.Media.Color.FromRgb(239, 68, 68);
            DrawArc(canvas, cx, cy, r + 2, 210, 210 + 300 * pct, arcColor, 10);
        }

        // Glow dot at tip
        if (pct > 0.01)
        {
            double tipAngle = (210 + 300 * pct) * Math.PI / 180;
            double tx = cx + (r + 2) * Math.Cos(tipAngle);
            double ty = cy + (r + 2) * Math.Sin(tipAngle);
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 8, Height = 8,
                Fill = new System.Windows.Media.SolidColorBrush(color),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                    { Color = color, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.9 }
            };
            Canvas.SetLeft(dot, tx - 4);
            Canvas.SetTop(dot,  ty - 4);
            canvas.Children.Add(dot);
        }

        // Valore testo
        var txt = new TextBlock
        {
            Text      = valText,
            FontFamily= new System.Windows.Media.FontFamily("Consolas"),
            FontSize  = 11, FontWeight = FontWeights.Bold,
            Foreground= new System.Windows.Media.SolidColorBrush(color),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };
        txt.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(txt, cx - txt.DesiredSize.Width / 2);
        Canvas.SetTop(txt,  cy - 8);
        canvas.Children.Add(txt);

        // Label
        var lbl = new TextBlock
        {
            Text      = label,
            FontFamily= new System.Windows.Media.FontFamily("Consolas"),
            FontSize  = 9,
            Foreground= new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(71, 85, 105)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };
        lbl.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(lbl, cx - lbl.DesiredSize.Width / 2);
        Canvas.SetTop(lbl,  cy + 10);
        canvas.Children.Add(lbl);
    }

    private static void DrawArc(Canvas canvas, double cx, double cy, double r,
                                 double startDeg, double endDeg,
                                 System.Windows.Media.Color color, double thickness)
    {
        if (endDeg <= startDeg) return;
        double sa = startDeg * Math.PI / 180;
        double ea = endDeg   * Math.PI / 180;
        double x1 = cx + r * Math.Cos(sa);
        double y1 = cy + r * Math.Sin(sa);
        double x2 = cx + r * Math.Cos(ea);
        double y2 = cy + r * Math.Sin(ea);
        bool largeArc = (endDeg - startDeg) > 180;

        var geo = new System.Windows.Media.PathGeometry();
        var fig = new System.Windows.Media.PathFigure { StartPoint = new System.Windows.Point(x1, y1) };
        fig.Segments.Add(new System.Windows.Media.ArcSegment(
            new System.Windows.Point(x2, y2),
            new System.Windows.Size(r, r),
            0, largeArc, System.Windows.Media.SweepDirection.Clockwise, true));
        geo.Figures.Add(fig);

        var path = new System.Windows.Shapes.Path
        {
            Data            = geo,
            Stroke          = new System.Windows.Media.SolidColorBrush(color),
            StrokeThickness = thickness,
            StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
            StrokeEndLineCap   = System.Windows.Media.PenLineCap.Round,
        };
        canvas.Children.Add(path);
    }

    private static System.Windows.Shapes.Path ArcPath(double cx, double cy, double r,
        double sa, double ea, System.Windows.Media.Color color, double opacity) => new();

    // ── Wiki ──────────────────────────────────────────────────────────────────
    private static readonly Dictionary<string, (string Title, string Icon, string[] Sections)> WikiData = new()
    {
        ["overview"] = ("Panoramica", "🏠", [
            "Cos'è NovaSCM|NovaSCM è uno strumento open source per la gestione di reti e fleet di PC. Combina scansione di rete, gestione certificati WiFi EAP-TLS, deploy Windows automatizzato, OPSI, SCCM e workflow di automazione in un'unica interfaccia WPF (stile SCCM Console).",
            $"Versione corrente|v{CurrentVersion} — .NET 9 · WPF · SQLite · Windows 10/11 (64-bit)\nGitHub: https://github.com/ClaudioBecchis/NovaSCM",
            "Funzionalità principali|• 📡 Scansione rete multi-VLAN con rilevamento vendor OUI\n• 🔐 Certificati WiFi EAP-TLS (Certportal + FreeRADIUS)\n• 💿 Deploy Windows zero-touch (autounattend.xml + postinstall.ps1)\n• ⚙️ Workflow automatizzati con agent distribuito\n• 📋 Change Request con tracking completo\n• 🏠 Dashboard con stat card in tempo reale\n• 🔍 Ricerca globale Ctrl+K\n• 🖥️ Integrazione Proxmox + SCCM",
            "Requisiti|• Windows 10/11 (64-bit)\n• .NET 9.0 Runtime (scaricabile da microsoft.com/dotnet)\n• Accesso alla rete da gestire\n• (Opzionale) Server NovaSCM per workflow e change requests",
            "Architettura|NovaSCM funziona in modalità offline-first: tutti i dati vengono salvati in un database SQLite locale (%APPDATA%\\PolarisManager\\novascm.db) e sincronizzati con il server quando disponibile. La config è in config.json — modifiche esterne vengono ricaricate automaticamente (hot reload).",
            "Primo avvio|1. Lancia NovaSCM.exe\n2. Si apre automaticamente il tab ⚙️ Impostazioni\n3. Configura almeno 'URL API NovaSCM' se hai un server\n4. Clicca 💾 Salva impostazioni\n5. Torna al tab 🏠 Asset → 📡 Rete per iniziare\nPuoi usare l'app anche senza server — scansione rete, deploy e certificati funzionano offline."
        ]),
        ["scan"] = ("Scansione Rete", "📡", [
            "Come funziona|NovaSCM esegue ping ICMP su tutti gli IP della subnet. Per ogni host online, legge il MAC dall'ARP table e lo confronta con il database OUI per identificare il vendor.",
            "Scansione base|1. Inserisci l'IP base (es. 192.168.10.0) e la subnet mask (es. 24)\n2. Clicca ▶ Scansiona\n3. I device vengono mostrati in tempo reale nella tabella\n4. Doppio click su un device per la scansione porte",
            "Scansione multi-VLAN|Configura le subnet multiple in ⚙️ Impostazioni (una per riga, formato 192.168.x.0/24). Usa il pulsante 🌐 Tutte le VLAN per scansionare tutto in parallelo.",
            "Vista Heatmap|Clicca ⬛ Heatmap per vedere tutti i 254 IP della subnet come griglia colorata:\n• 🟢 Verde = online\n• 🔵 Blu = ha certificato EAP-TLS\n• 🟡 Amber = gateway\n• ⬛ Scuro = offline o non scansionato",
            "Vista Mappa|Clicca 🗺 Mappa per la vista grafica hub-and-spoke con animazioni pulse sui device online.",
            "Live Ping|Seleziona un device nella tabella per avviare automaticamente il live ping graph nel pannello inferiore."
        ]),
        ["certs"] = ("Certificati EAP-TLS", "🔐", [
            "Cos'è EAP-TLS|EAP-TLS è un metodo di autenticazione WiFi enterprise che usa certificati digitali invece di password. Ogni device ha un certificato unico firmato dalla CA dell'organizzazione.",
            "Prerequisiti|• FreeRADIUS configurato con la tua CA (es. CT 105: 192.168.20.105)\n• SSID WPA2-Enterprise sul controller WiFi\n• Certportal in esecuzione (CT 103: 192.168.20.110:9090)",
            "Generare un certificato|1. Esegui una scansione di rete\n2. Seleziona il device\n3. Clicca 🔐 Genera Cert\n4. Il certificato viene generato dal Certportal e salvato nel database",
            "Enrollment automatico|Su Windows, esegui da PowerShell admin:\n\n  iwr http://192.168.20.110:9090/agent/install.ps1 | iex\n\nIl device si registrerà automaticamente ad ogni avvio."
        ]),
        ["deploy"] = ("Deploy Windows", "💿", [
            "Zero-touch deployment|NovaSCM genera automaticamente i file necessari per installare Windows senza alcun intervento manuale:\n• autounattend.xml — risponde a tutte le domande di setup\n• postinstall.ps1 — installa software e agenti dopo il riavvio",
            "Configurazione|Nel tab 💿 Deploy:\n1. Scegli edizione Windows (Pro/Home/Enterprise)\n2. Imposta template nome PC (es. PC-{MAC6})\n3. Imposta password Administrator\n4. Seleziona software winget da installare\n5. Clicca ⚙️ Genera file",
            "Distribuzione USB|1. Scarica ISO Windows 11 da microsoft.com\n2. Estrai l'ISO su chiavetta FAT32\n3. Copia autounattend.xml + postinstall.ps1 nella RADICE della USB\n4. Avvia il PC dalla USB — installazione completamente automatica",
            "Distribuzione PXE|Se hai un server PXE configurato (CT 110), usa il pulsante 🌐 Deploy PXE per copiare i file via SCP automaticamente.",
            "Template nome PC|{MAC6} = ultimi 6 caratteri del MAC address (es. PC-B3CEEB)\n{nn} = contatore incrementale (es. PC-001)"
        ]),
        ["workflow"] = ("Workflow", "⚙️", [
            "Cos'è un Workflow|Un workflow è una sequenza di step automatizzati che vengono eseguiti su un PC. Simile alle Task Sequence di SCCM, ma più semplice e configurabile via GUI.",
            "Creare un workflow|1. Vai nel tab ⚙️ Workflow\n2. Clicca + Nuovo Workflow\n3. Inserisci nome e descrizione\n4. Aggiungi step con + Aggiungi Step",
            "Tipi di step|• cmd — esegui un comando\n• powershell — esegui script PS\n• winget — installa/aggiorna pacchetto\n• robocopy — copia file\n• reboot — riavvia il PC\n• wait — attendi X secondi",
            "Assegnare a un PC|Usa il pulsante Assegna Workflow per collegare un workflow a un nome PC specifico. Il PC eseguirà il workflow al prossimo check-in.",
            "Monitoraggio|La colonna Progresso mostra l'avanzamento in tempo reale. I colori indicano: Blu = in esecuzione, Verde = completato, Rosso = errore."
        ]),
        ["pc"] = ("Fleet PC", "🖥️", [
            "Gestione PC|Il tab 🖥️ PC mostra tutti i PC registrati con il loro stato agente, OS, CPU e RAM.",
            "Agent NovaSCM|L'agent è un servizio Windows che:\n• Fa check-in sul server ogni 30s\n• Esegue i workflow assegnati\n• Riporta inventario hardware/software\n• Mantiene il certificato WiFi aggiornato",
            "Inventario|Clicca 📊 Inventario su un PC selezionato per raccogliere:\n• Hardware (CPU, RAM, disco, schede di rete)\n• OS e versione\n• Software installato (da registro Windows)\n• Patch mancanti",
            "RDP|Clicca 🖥️ RDP per aprire una connessione Remote Desktop al PC selezionato (richiede porta 3389 aperta).",
            "Gauge risorse|I 4 gauge animati mostrano le risorse del PC locale in tempo reale: CPU%, RAM, Disco% e stato rete."
        ]),
        ["opsi"] = ("OPSI Software", "📦", [
            "Cos'è OPSI|OPSI è un sistema open source per la distribuzione automatica di software su PC Windows. NovaSCM si integra con il server OPSI per gestire i pacchetti.",
            "Configurare OPSI|In ⚙️ Impostazioni, imposta l'URL del server OPSI. Assicurati che il server OPSI sia raggiungibile dalla rete.",
            "Gestire pacchetti|Nel tab 📦 OPSI puoi:\n• Vedere tutti i pacchetti disponibili\n• Installare/aggiornare pacchetti su PC specifici\n• Verificare lo stato di deployment",
            "Integrazione con Workflow|Puoi usare step di tipo 'winget' nei workflow come alternativa a OPSI per distribuzione software senza server dedicato."
        ]),
        ["sccm"] = ("SCCM / OSD", "🏢", [
            "Integrazione SCCM|NovaSCM include un visualizzatore di Task Sequence compatibile con SCCM (Microsoft Endpoint Configuration Manager).",
            "Task Sequence Debugger|Nel tab 🏢 SCCM puoi:\n• Visualizzare le Task Sequence attive\n• Monitorare lo stato di esecuzione\n• Visualizzare variabili e log",
            "OSD (OS Deployment)|Per ambienti con SCCM esistente, NovaSCM può lavorare in parallelo per:\n• Registrare device nel database locale\n• Monitorare il progresso del deployment\n• Gestire certificati post-deploy",
            "Infrastruttura suggerita|• VM Windows Server 2025 con AD + SQL + SCCM 2403\n• Dominio: corp.nomeazienda.it\n• Task Sequence per Windows 11 Pro con scelta dominio/workgroup"
        ]),
        ["cr"] = ("Change Requests", "📋", [
            "Cos'è una CR|Una Change Request (CR) è una richiesta di provisioning di un nuovo PC. Permette di tracciare il ciclo di vita completo: dalla richiesta alla consegna.",
            "Creare una CR|1. Vai nel tab 📋 Richieste\n2. Clicca + Nuova Richiesta\n3. Compila: nome PC, dominio, OU, utente assegnato\n4. Seleziona software da installare\n5. Clicca Crea\nLa CR viene salvata localmente se offline.",
            "Stati CR|• ⏳ Pending — in attesa di approvazione\n• 🔄 In Progress — deployment in corso\n• ✅ Completata — PC consegnato e configurato\n• ❌ Rifiutata — richiesta respinta",
            "Modalità offline|NovaSCM salva le CR nel database locale quando il server non è raggiungibile. Vengono sincronizzate automaticamente al prossimo collegamento."
        ]),
        ["faq"] = ("FAQ", "❓", [
            "Come faccio a scansionare più subnet?|Vai in ⚙️ Impostazioni → campo 'Subnet multiple' → inserisci una subnet per riga in formato CIDR (es. 192.168.10.0/24). Poi usa il pulsante 🌐 Tutte le VLAN nel tab Rete.",
            "Il MAC non viene trovato|Il MAC viene letto dall'ARP table Windows. Se il device è su una subnet diversa (router in mezzo), il MAC potrebbe non essere visibile. Usa uno switch managed con VLAN access.",
            "Il certificato non viene generato|Verifica che:\n1. Il Certportal sia raggiungibile all'URL configurato\n2. La CA sia presente in /ca/ca.crt sul server\n3. Il MAC del device sia noto al Certportal",
            "La scansione è lenta|Su subnet /24 sono normali 15-30 secondi. Reti con firewall aggressivo possono richiedere più tempo. Il semaforo scansiona max 50 IP in parallelo.",
            "Come aggiorno NovaSCM?|L'aggiornamento è automatico: all'avvio viene confrontata la versione locale con GitHub Releases. Se disponibile, appare un banner giallo con pulsante 'Installa ora'.",
            "Come cambio tema chiaro/scuro?|Vai in ⚙️ Impostazioni → sezione '🎨 Aspetto' → clicca il pulsante 'Modalità chiara' / 'Modalità scura'.",
            "Come uso il log viewer?|Clicca '📋 Log' nella barra di stato in basso a sinistra per aprire il pannello log. Mostra tutti gli eventi dell'applicazione in tempo reale.",
            "Il Matrix Rain si attiva?|Sì! Apri il tab ℹ️ About per vederlo. Esiste anche un Easter Egg nascosto... cerca il codice Konami. 😏"
        ]),
        ["ui"] = ("Interfaccia", "🎨", [
            "Navigazione SCCM-style|La sidebar sinistra usa il modello console SCCM con 4 workspace:\n• 🏠 Asset e Conformità — Rete, PC, Certificati, Dashboard\n• 📦 Libreria Software — App, OPSI, Deploy, Workflow, Script\n• 📊 Monitoraggio — Richieste CR, Console SCCM\n• ⚙️ Amministrazione — Proxmox, Impostazioni, About",
            "Sidebar collapse (UI-07)|Clicca il pulsante ◀ nell'header della sidebar per nasconderla e guadagnare spazio. Clicca la sottile barra ▶ a sinistra per riaprirla. L'animazione dura 180ms.",
            "Ricerca globale Ctrl+K (FEAT-03)|Premi Ctrl+K in qualsiasi momento per aprire la ricerca. Cerca tra:\n• Device scansionati (per IP, nome, vendor)\n• Workflow definiti\n• PC con workflow assegnati\nPremi Invio o clicca per navigare direttamente.",
            "Shortcut tastiera (DX-03)|• Ctrl+K — ricerca globale\n• F5 / Ctrl+R — aggiorna tab corrente\n• Ctrl+N — nuova Change Request\n• Ctrl+1..9 — naviga al tab N\n• Escape — chiudi overlay",
            "Dashboard (FEAT-01)|La 🏠 Dashboard mostra 4 stat card in tempo reale con auto-refresh ogni 30 secondi:\n• PC online vs totale scansionati\n• Workflow in esecuzione\n• Change Request aperte\n• Device rilevati\nIncludes activity feed con gli ultimi eventi CR e workflow.",
            "Tema chiaro/scuro (UI-02)|In ⚙️ Impostazioni → sezione '🎨 Aspetto' puoi alternare tra tema scuro (default) e chiaro. La preferenza NON è ancora salvata — verrà aggiunta in una versione futura.",
            "Log viewer (DX-02)|Clicca '📋 Log' nella status bar in basso per aprire il pannello log (160px). Mostra timestamp e messaggio per ogni azione. Pulsante 🗑️ per pulire, ✕ per chiudere.",
            "Badge counters nav (UI-04)|I nodi Workflow e Richieste CR nella sidebar mostrano automaticamente il conteggio pending tra parentesi (es. '⚙️  Workflow  [3]'). Si aggiorna ogni 30s insieme alla Dashboard."
        ]),
        ["agent"] = ("NovaSCM Agent", "🤖", [
            "Cos'è l'agent|NovaSCM Agent è un Windows Service (.NET Worker Service) che gira in background sui PC gestiti. Si connette al server ogni 30 secondi, scarica i workflow assegnati ed esegue gli step.",
            "Installazione|Da PowerShell come amministratore:\n  iwr http://<SERVER>:9091/agent/install.ps1 | iex\nL'installer crea il servizio Windows 'NovaSCMAgent' con avvio automatico.",
            "Configurazione|File: C:\\ProgramData\\NovaSCMAgent\\agent.json\n  {\n    \"ApiUrl\": \"http://192.168.20.110:9091\",\n    \"ApiKey\": \"chiave-segreta\",\n    \"PollIntervalSeconds\": 30\n  }",
            "Tipi di step supportati|• winget_install — installa pacchetto via winget\n• powershell — esegue script PowerShell\n• cmd / shell — esegue comando shell\n• reg_set — imposta chiave di registro\n• reboot — riavvia il PC\n• wait — attende N secondi\n• file_copy — copia file localmente\n• systemd_service — gestisce servizi (Linux)",
            "Condizioni step|Ogni step può avere una condizione:\n• windows — esegue solo su Windows\n• linux — esegue solo su Linux\n• os=windows / os=linux — alias\n• hostname=NOME-PC — solo su quel PC",
            "Sicurezza (BUG-02)|L'agent usa ProcessStartInfo.ArgumentList per passare i parametri ai comandi — nessuna shell injection possibile. Ogni chiamata API include l'ApiKey per autenticazione.",
            "Log e monitoraggio|Il log dell'agent è in:\n  C:\\ProgramData\\NovaSCMAgent\\agent.log\nPuoi seguirlo in tempo reale con:\n  Get-Content -Wait -Tail 50 agent.log"
        ]),
        ["server"] = ("Server API", "🖧", [
            "Architettura|Il server NovaSCM è un'API Flask + SQLite in esecuzione su Docker (consigliato) o direttamente su un container LXC Proxmox. Porta default: 9091.",
            "Avvio con Docker|Nella cartella server/:\n  docker compose up -d\nL'API è disponibile su http://localhost:9091. Il database è persistente nel volume 'novascm-data'.",
            "Avvio manuale (LXC)|  pip install flask gunicorn\n  NOVASCM_DB=/opt/novascm/cr.db gunicorn -w1 -t4 -b0.0.0.0:9091 api:app",
            "Endpoint principali|• GET/POST /api/cr — lista e crea Change Request\n• PUT /api/cr/<id>/status — cambia stato CR\n• GET /api/cr/<id>/steps — step di una CR\n• POST /api/cr/<id>/step — report step da postinstall\n• GET /api/version — versione per auto-update\n• GET /health — healthcheck Docker",
            "Autenticazione (BUG-01)|Tutte le route (eccetto /health) richiedono l'header:\n  X-Api-Key: tua-chiave-segreta\nConfigurata in docker-compose.yml come NOVASCM_API_KEY. Usa hmac.compare_digest per confronto timing-safe.",
            "Database SQLite|Tabelle principali:\n• cr — Change Request (id, pc_name, domain, status, odj_blob, ...)\n• cr_steps — step eseguiti (cr_id, step_name, status, timestamp)\nWAL mode attivo per evitare lock concorrenti.",
            "Aggiornare il server|  docker compose pull && docker compose up -d\nIl DB persiste nel volume — nessun dato va perso."
        ])
    };

    private void WikiNavList_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (WikiNavList.SelectedItem is not ListBoxItem item) return;
        string key = item.Tag?.ToString() ?? "overview";
        RenderWikiPage(key);
    }

    private void RenderWikiPage(string key)
    {
        WikiContent.Children.Clear();
        if (!WikiData.TryGetValue(key, out var page)) return;

        // Titolo pagina
        WikiContent.Children.Add(new TextBlock
        {
            Text = $"{page.Icon}  {page.Title}",
            FontSize = 24, FontWeight = FontWeights.Bold,
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(30, 58, 138)),
            Margin = new Thickness(0, 0, 0, 4)
        });

        // Linea separatore
        WikiContent.Children.Add(new Border
        {
            Height = 2, Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(219, 234, 254)),
            Margin = new Thickness(0, 0, 0, 20)
        });

        foreach (var sec in page.Sections)
        {
            var parts   = sec.Split('|', 2);
            var secTitle = parts[0];
            var secBody  = parts.Length > 1 ? parts[1] : "";

            // Titolo sezione
            WikiContent.Children.Add(new TextBlock
            {
                Text = secTitle,
                FontSize = 15, FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(30, 64, 175)),
                Margin = new Thickness(0, 16, 0, 6)
            });

            // Contenuto — separa blocchi codice (linee che iniziano con 2+ spazi)
            var lines = secBody.Split('\n');
            var normalLines = new List<string>();

            foreach (var line in lines)
            {
                bool isCode = line.StartsWith("  ") || line.StartsWith("\t");
                if (isCode)
                {
                    // Svuota buffer normale
                    if (normalLines.Count > 0)
                    {
                        AddWikiParagraph(string.Join("\n", normalLines));
                        normalLines.Clear();
                    }
                    AddWikiCodeLine(line.TrimStart());
                }
                else
                {
                    normalLines.Add(line);
                }
            }
            if (normalLines.Count > 0)
                AddWikiParagraph(string.Join("\n", normalLines));
        }
    }

    private void AddWikiParagraph(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        WikiContent.Children.Add(new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(248, 250, 252)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = new TextBlock
            {
                Text = text, TextWrapping = TextWrapping.Wrap,
                FontSize = 12.5, LineHeight = 22,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(51, 65, 85))
            }
        });
    }

    private void AddWikiCodeLine(string code)
    {
        WikiContent.Children.Add(new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(15, 23, 42)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 0, 4),
            Child = new TextBlock
            {
                Text = code, TextWrapping = TextWrapping.Wrap,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 12,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(74, 222, 128))
            }
        });
    }

    // ══════════════════════════════════════════════════════════════════════════
    // WAKE-ON-LAN
    // ══════════════════════════════════════════════════════════════════════════

    private static void SendWakeOnLan(string macAddress)
    {
        // Normalizza MAC — rimuove separatori
        var mac = macAddress.Replace(":", "").Replace("-", "").Replace(".", "");
        if (mac.Length != 12) throw new ArgumentException("MAC non valido");
        var macBytes = Enumerable.Range(0, 6)
            .Select(i => Convert.ToByte(mac.Substring(i * 2, 2), 16))
            .ToArray();
        // Magic packet: 6 byte 0xFF + MAC ripetuto 16 volte
        var packet = new byte[17 * 6];
        for (int i = 0; i < 6; i++) packet[i] = 0xFF;
        for (int i = 1; i <= 16; i++)
            Buffer.BlockCopy(macBytes, 0, packet, i * 6, 6);
        using var udp = new System.Net.Sockets.UdpClient();
        udp.EnableBroadcast = true;
        udp.Send(packet, packet.Length, new System.Net.IPEndPoint(System.Net.IPAddress.Broadcast, 9));
        // Invia anche su porta 7 (alcuni device la richiedono)
        udp.Send(packet, packet.Length, new System.Net.IPEndPoint(System.Net.IPAddress.Broadcast, 7));
    }

    private void MenuWoL_Click(object s, RoutedEventArgs e)
    {
        if (NetGrid.SelectedItem is not DeviceRow dev) return;
        BtnWoL_Execute(dev);
    }

    private void BtnWoL_Click(object s, RoutedEventArgs e)
    {
        if (NetGrid.SelectedItem is not DeviceRow dev)
        { SetStatus("⚠️ Seleziona un device dalla lista"); return; }
        BtnWoL_Execute(dev);
    }

    private void BtnWoL_Execute(DeviceRow dev)
    {
        if (dev.Mac == "—" || string.IsNullOrEmpty(dev.Mac))
        {
            var mac = Microsoft.VisualBasic.Interaction.InputBox(
                $"MAC non disponibile per {dev.Ip}.\nInserisci il MAC address (es: 00:11:22:33:44:55):",
                "Wake-on-LAN", "");
            if (string.IsNullOrWhiteSpace(mac)) return;
            dev = new DeviceRow { Ip = dev.Ip, Mac = mac };
        }
        try
        {
            SendWakeOnLan(dev.Mac);
            SetStatus($"💡 Magic packet inviato a {dev.Mac} ({dev.Ip})");
            ShowToast("Wake-on-LAN", $"Magic packet inviato a {dev.Name} ({dev.Ip})");
        }
        catch (Exception ex) { SetStatus($"❌ WoL fallito: {ex.Message}"); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SSH TERMINAL
    // ══════════════════════════════════════════════════════════════════════════

    private void MenuSsh_Click(object s, RoutedEventArgs e)
    {
        if (NetGrid.SelectedItem is not DeviceRow dev) return;
        OpenSshTerminal(dev);
    }

    private void OpenSshTerminal(DeviceRow dev)
    {
        var user = _config.AdminUser;
        if (string.IsNullOrWhiteSpace(user)) user = "root";
        var target = $"{user}@{dev.Ip}";

        // Prova Windows Terminal, poi PowerShell, poi cmd
        var wtPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\WindowsApps\wt.exe");

        if (File.Exists(wtPath))
        {
            var startInfo = new ProcessStartInfo("wt.exe") { UseShellExecute = true };
            startInfo.ArgumentList.Add("ssh");
            startInfo.ArgumentList.Add(target);
            Process.Start(startInfo);
        }
        else
        {
            var startInfo = new ProcessStartInfo("cmd.exe")
            { UseShellExecute = true, CreateNoWindow = false };
            startInfo.ArgumentList.Add("/k");
            startInfo.ArgumentList.Add("ssh");
            startInfo.ArgumentList.Add(target);
            Process.Start(startInfo);
        }
        SetStatus($"🖥️ Apertura SSH verso {target}");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TRACEROUTE GRAFICO
    // ══════════════════════════════════════════════════════════════════════════

    private void MenuTraceroute_Click(object s, RoutedEventArgs e)
    {
        if (NetGrid.SelectedItem is not DeviceRow dev) return;
        _ = RunTracerouteAsync(dev.Ip, dev.Name != "—" ? dev.Name : dev.Ip);
    }

    private void BtnCloseTraceroute_Click(object s, RoutedEventArgs e)
    {
        TraceroutePanel.Visibility = Visibility.Collapsed;
        NetGrid.Visibility         = Visibility.Visible;
    }

    private async Task RunTracerouteAsync(string ip, string label)
    {
        NetGrid.Visibility         = Visibility.Collapsed;
        TraceroutePanel.Visibility = Visibility.Visible;
        TxtTracerouteTitle.Text    = $"🔀  Traceroute → {label} ({ip})";
        TracerouteStack.Children.Clear();

        AddTracerouteRow("—", "Avvio...", 0, true);
        SetStatus($"🔀 Traceroute verso {ip}...");

        var hops = new List<(int ttl, string hopIp, long ms, string name)>();

        await Task.Run(async () =>
        {
            for (int ttl = 1; ttl <= 30; ttl++)
            {
                using var ping   = new Ping();
                var opts         = new PingOptions(ttl, true);
                long bestMs      = -1;
                string hopIp     = "*";
                string hopName   = "";

                // 3 tentativi per TTL
                for (int t = 0; t < 3; t++)
                {
                    try
                    {
                        var reply = ping.Send(ip, 1500, new byte[32], opts);
                        if (reply.Status == IPStatus.TtlExpired ||
                            reply.Status == IPStatus.Success)
                        {
                            hopIp  = reply.Address?.ToString() ?? "*";
                            bestMs = reply.RoundtripTime;
                            break;
                        }
                    }
                    catch { }
                }

                if (hopIp != "*")
                {
                    try
                    {
                        var entry  = Dns.GetHostEntry(hopIp);
                        hopName    = entry.HostName;
                    }
                    catch { hopName = hopIp; }
                }

                var finalHop = hopIp == ip;
                hops.Add((ttl, hopIp, bestMs, hopName));

                await Dispatcher.InvokeAsync(() =>
                {
                    TracerouteStack.Children.Clear();
                    foreach (var h in hops)
                        AddTracerouteRow(h.hopIp, h.name, h.ms, false, h.hopIp == ip);
                });

                if (finalHop) break;
            }
        });

        SetStatus($"✅ Traceroute completato — {hops.Count} hop");
    }

    private void AddTracerouteRow(string hopIp, string hopName, long ms, bool loading, bool isDestination = false)
    {
        var hopNum = TracerouteStack.Children.Count + 1;
        var msColor = ms < 0    ? "#475569"
                    : ms < 20   ? "#10b981"
                    : ms < 80   ? "#f59e0b"
                    : "#ef4444";
        var msText  = ms < 0 ? "* * *" : $"{ms} ms";

        var border = new Border
        {
            Background      = isDestination
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 16, 185, 129))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 255, 255, 255)),
            BorderBrush     = new System.Windows.Media.SolidColorBrush(
                isDestination ? System.Windows.Media.Color.FromRgb(16, 185, 129)
                              : System.Windows.Media.Color.FromRgb(30, 58, 95)),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
            Margin          = new Thickness(0, 0, 0, 4),
            Padding         = new Thickness(12, 8, 12, 8)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

        var colNum = MakeTb($"{hopNum}", "#64748b", 11);
        var colIp  = MakeTb(hopIp, "#60a5fa", 12, true);
        var colName= MakeTb(loading ? "..." : (hopName.Length > 40 ? hopName[..37] + "…" : hopName),
                            "#94a3b8", 11);
        var colMs  = MakeTb(msText, msColor, 13, true);

        Grid.SetColumn(colNum,  0);
        Grid.SetColumn(colIp,   1);
        Grid.SetColumn(colName, 2);
        Grid.SetColumn(colMs,   3);

        grid.Children.Add(colNum);
        grid.Children.Add(colIp);
        grid.Children.Add(colName);
        grid.Children.Add(colMs);

        border.Child = grid;

        // Linea connettore tra hop
        if (TracerouteStack.Children.Count > 0)
        {
            TracerouteStack.Children.Add(new TextBlock
            {
                Text = "  │", Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(30, 58, 95)),
                FontSize = 10, Margin = new Thickness(0, 0, 0, 0)
            });
        }

        TracerouteStack.Children.Add(border);
    }

    private static TextBlock MakeTb(string text, string hex, double size, bool bold = false)
    {
        var c = System.Windows.Media.ColorConverter.ConvertFromString(hex);
        return new TextBlock
        {
            Text       = text,
            Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)c!),
            FontSize   = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin     = new Thickness(0, 0, 8, 0)
        };
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SPEED TEST
    // ══════════════════════════════════════════════════════════════════════════

    private bool _speedTestRunning = false;

    private async void BtnSpeedTest_Click(object s, RoutedEventArgs e)
    {
        try
        {
        if (_speedTestRunning) return;
        // Mostra pannello speed test
        HideAllNetPanels();
        SpeedTestPanel.Visibility = Visibility.Visible;
        NetGrid.Visibility        = Visibility.Collapsed;
        await RunSpeedTestAsync();
        }
        catch (Exception ex) { App.Log($"[BtnSpeedTest_Click] {ex.Message}"); SetStatus($"❌ {ex.Message}"); }
    }

    private void BtnCloseSpeedTest_Click(object s, RoutedEventArgs e)
    {
        SpeedTestPanel.Visibility = Visibility.Collapsed;
        NetGrid.Visibility        = Visibility.Visible;
    }

    private async Task RunSpeedTestAsync()
    {
        _speedTestRunning = true;
        TxtSpeedStatus.Text = "🔍 Misuro ping...";
        TxtSpeedDown.Text   = "— Mbps";
        TxtSpeedUp.Text     = "— Mbps";
        TxtSpeedPing.Text   = "Ping: — ms";
        DrawSpeedArc(SpeedGaugeDown, 0, "#10b981");
        DrawSpeedArc(SpeedGaugeUp,   0, "#3b82f6");

        using var http = new System.Net.Http.HttpClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        // 1. Ping
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await http.GetAsync("https://www.cloudflare.com/cdn-cgi/trace");
            sw.Stop();
            var pingMs = sw.ElapsedMilliseconds;
            TxtSpeedPing.Text = $"Ping: {pingMs} ms";
        }
        catch { TxtSpeedPing.Text = "Ping: ❌"; }

        // 2. Download test — scarica 25 MB da Cloudflare
        TxtSpeedStatus.Text = "⬇ Test download...";
        double downMbps = 0;
        try
        {
            var url = "https://speed.cloudflare.com/__down?bytes=25000000";
            var sw  = System.Diagnostics.Stopwatch.StartNew();
            var data = await http.GetByteArrayAsync(url);
            sw.Stop();
            var seconds = sw.Elapsed.TotalSeconds;
            downMbps    = (data.Length * 8.0) / (seconds * 1_000_000);
            TxtSpeedDown.Text = $"{downMbps:F1} Mbps";
            DrawSpeedArc(SpeedGaugeDown, Math.Min(downMbps / 1000.0, 1.0), "#10b981");
        }
        catch { TxtSpeedDown.Text = "❌ Errore"; }

        // 3. Upload test — invia 5 MB a Cloudflare
        TxtSpeedStatus.Text = "⬆ Test upload...";
        double upMbps = 0;
        try
        {
            var url     = "https://speed.cloudflare.com/__up";
            var payload = new byte[5_000_000];
            new Random().NextBytes(payload);
            var content = new System.Net.Http.ByteArrayContent(payload);
            var sw      = System.Diagnostics.Stopwatch.StartNew();
            await http.PostAsync(url, content);
            sw.Stop();
            var seconds = sw.Elapsed.TotalSeconds;
            upMbps      = (payload.Length * 8.0) / (seconds * 1_000_000);
            TxtSpeedUp.Text = $"{upMbps:F1} Mbps";
            DrawSpeedArc(SpeedGaugeUp, Math.Min(upMbps / 500.0, 1.0), "#3b82f6");
        }
        catch { TxtSpeedUp.Text = "❌ Errore"; }

        TxtSpeedStatus.Text = downMbps > 0 && upMbps > 0
            ? $"✅ Test completato\n{downMbps:F0}↓  {upMbps:F0}↑ Mbps"
            : "⚠️ Test parzialmente fallito";
        SetStatus($"⚡ Speed test: ↓{downMbps:F0} Mbps  ↑{upMbps:F0} Mbps");
        _speedTestRunning = false;
    }

    private void DrawSpeedArc(System.Windows.Controls.Canvas canvas, double ratio, string colorHex)
    {
        canvas.Children.Clear();
        double w = 140, h = 140, cx = 70, cy = 80, r = 54;
        var gray  = System.Windows.Media.Color.FromRgb(30, 42, 60);
        var color = (System.Windows.Media.Color)
            System.Windows.Media.ColorConverter.ConvertFromString(colorHex)!;

        // Sfondo arco
        DrawSpeedArcSegment(canvas, cx, cy, r, -210, 240, gray, 10);
        // Arco progresso
        if (ratio > 0.001)
            DrawSpeedArcSegment(canvas, cx, cy, r, -210, 240 * ratio, color, 10);

        // Valore percentuale al centro
        var pct = new TextBlock
        {
            Text       = $"{ratio * 100:F0}%",
            Foreground = new System.Windows.Media.SolidColorBrush(color),
            FontSize   = 14, FontWeight = FontWeights.Bold
        };
        pct.Measure(new System.Windows.Size(100, 30));
        System.Windows.Controls.Canvas.SetLeft(pct, cx - pct.DesiredSize.Width / 2);
        System.Windows.Controls.Canvas.SetTop(pct,  cy - 10);
        canvas.Children.Add(pct);
    }

    private static void DrawSpeedArcSegment(System.Windows.Controls.Canvas canvas,
        double cx, double cy, double r, double startDeg, double sweepDeg,
        System.Windows.Media.Color color, double thickness)
    {
        if (Math.Abs(sweepDeg) < 0.5) return;
        double startRad = startDeg * Math.PI / 180;
        double endRad   = (startDeg + sweepDeg) * Math.PI / 180;
        var p1 = new System.Windows.Point(cx + r * Math.Cos(startRad), cy + r * Math.Sin(startRad));
        var p2 = new System.Windows.Point(cx + r * Math.Cos(endRad),   cy + r * Math.Sin(endRad));
        var figure = new System.Windows.Media.PathFigure { StartPoint = p1 };
        figure.Segments.Add(new System.Windows.Media.ArcSegment
        {
            Point          = p2,
            Size           = new System.Windows.Size(r, r),
            IsLargeArc     = Math.Abs(sweepDeg) > 180,
            SweepDirection = System.Windows.Media.SweepDirection.Clockwise
        });
        var geo = new System.Windows.Media.PathGeometry();
        geo.Figures.Add(figure);
        var path = new System.Windows.Shapes.Path
        {
            Data            = geo,
            Stroke          = new System.Windows.Media.SolidColorBrush(color),
            StrokeThickness = thickness,
            StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
            StrokeEndLineCap   = System.Windows.Media.PenLineCap.Round
        };
        canvas.Children.Add(path);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // mDNS SCANNER
    // ══════════════════════════════════════════════════════════════════════════

    private async void BtnMdns_Click(object s, RoutedEventArgs e)
    {
        try
        {
        SetStatus("🔍 Scansione mDNS/Bonjour in corso...");
        var found = await ScanMdnsAsync();

        if (found.Count == 0)
        {
            SetStatus("⚠️ Nessun device mDNS trovato sulla rete locale");
            return;
        }

        int added = 0;
        foreach (var (ip, name) in found)
        {
            if (!_netRows.Any(r => r.Ip == ip))
            {
                _netRows.Add(new DeviceRow
                {
                    Ip         = ip,
                    Name       = name,
                    Status     = "🟢 Online",
                    Icon       = GuessIconFromMdns(name),
                    DeviceType = GuessMdnsType(name)
                });
                added++;
            }
            else
            {
                var existing = _netRows.FirstOrDefault(r => r.Ip == ip);
                if (existing != null && existing.Name == "—")
                    existing.Name = name;
            }
        }

        SetStatus($"✅ mDNS: {found.Count} device trovati, {added} nuovi aggiunti");
        ShowToast("mDNS Scan", $"{found.Count} device Bonjour trovati sulla rete");
        }
        catch (Exception ex) { App.Log($"[BtnMdns_Click] {ex.Message}"); SetStatus($"❌ {ex.Message}"); }
    }

    private static async Task<List<(string ip, string name)>> ScanMdnsAsync()
    {
        var results = new System.Collections.Concurrent.ConcurrentBag<(string, string)>();
        // Servizi mDNS comuni da cercare
        var services = new[]
        {
            "_http._tcp.local", "_https._tcp.local", "_smb._tcp.local",
            "_afpovertcp._tcp.local", "_ipp._tcp.local", "_printer._tcp.local",
            "_airplay._tcp.local", "_googlecast._tcp.local", "_raop._tcp.local",
            "_ssh._tcp.local", "_rdp._tcp.local", "_workstation._tcp.local"
        };

        var mcastEp = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("224.0.0.251"), 5353);

        await Task.Run(() =>
        {
            foreach (var svc in services)
            {
                try
                {
                    using var udp = new System.Net.Sockets.UdpClient();
                    udp.Client.SetSocketOption(
                        System.Net.Sockets.SocketOptionLevel.Socket,
                        System.Net.Sockets.SocketOptionName.ReuseAddress, true);
                    udp.Client.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0));
                    udp.JoinMulticastGroup(System.Net.IPAddress.Parse("224.0.0.251"));
                    udp.Client.ReceiveTimeout = 800;

                    // DNS query per il servizio
                    var query = BuildMdnsQuery(svc);
                    udp.Send(query, query.Length, mcastEp);

                    try
                    {
                        for (int i = 0; i < 5; i++)
                        {
                            var remote = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
                            var data   = udp.Receive(ref remote);
                            var ip     = remote.Address.ToString();
                            var name   = ParseMdnsName(data) ?? svc.Split('.')[0];
                            results.Add((ip, name));
                        }
                    }
                    catch (System.Net.Sockets.SocketException) { } // timeout normale
                }
                catch { }
            }
        });

        return results.DistinctBy(r => r.Item1).ToList();
    }

    private static byte[] BuildMdnsQuery(string serviceName)
    {
        // DNS query minimale per PTR record
        var name  = serviceName.TrimEnd('.');
        var parts = name.Split('.');
        var msg   = new System.Collections.Generic.List<byte>();
        // Header: ID=0, flags=standard query, 1 question
        msg.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
        foreach (var p in parts)
        {
            msg.Add((byte)p.Length);
            msg.AddRange(System.Text.Encoding.ASCII.GetBytes(p));
        }
        msg.Add(0x00); // fine nome
        msg.AddRange(new byte[] { 0x00, 0x0C, 0x00, 0x01 }); // type PTR, class IN
        return msg.ToArray();
    }

    private static string? ParseMdnsName(byte[] data)
    {
        try
        {
            // Salta header (12 byte) e cerca il primo label
            int i = 12;
            var sb = new System.Text.StringBuilder();
            while (i < data.Length && data[i] != 0)
            {
                int len = data[i++];
                if (len > 63 || i + len > data.Length) break;
                if (sb.Length > 0) sb.Append('.');
                sb.Append(System.Text.Encoding.UTF8.GetString(data, i, len));
                i += len;
            }
            var n = sb.ToString();
            return n.EndsWith(".local") ? n[..^6] : n;
        }
        catch { return null; }
    }

    private static string GuessIconFromMdns(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("apple") || n.Contains("mac") || n.Contains("iphone") || n.Contains("ipad"))  return "🍎";
        if (n.Contains("print") || n.Contains("hp") || n.Contains("canon") || n.Contains("epson"))   return "🖨️";
        if (n.Contains("cast") || n.Contains("chromecast") || n.Contains("tv"))                       return "📺";
        if (n.Contains("airplay") || n.Contains("airport"))                                           return "📡";
        if (n.Contains("smb") || n.Contains("server") || n.Contains("nas"))                          return "🗄️";
        return "🔵";
    }

    private static string GuessMdnsType(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("print")) return "Stampante";
        if (n.Contains("cast") || n.Contains("tv")) return "Smart TV / Cast";
        if (n.Contains("nas") || n.Contains("server")) return "NAS / Server";
        if (n.Contains("apple") || n.Contains("mac")) return "Apple";
        return "mDNS Device";
    }

    // ══════════════════════════════════════════════════════════════════════════
    // EXPORT CSV
    // ══════════════════════════════════════════════════════════════════════════

    private void MenuExportCsv_Click(object s, RoutedEventArgs e) => ExportDevicesToCsv();
    private void BtnExportCsv_Click(object s, RoutedEventArgs e)  => ExportDevicesToCsv();

    private void ExportDevicesToCsv()
    {
        if (_netRows.Count == 0) { SetStatus("⚠️ Nessun device da esportare — esegui prima una scansione"); return; }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "Esporta device in CSV",
            FileName   = $"NovaSCM_network_{DateTime.Now:yyyyMMdd_HHmm}.csv",
            Filter     = "CSV (*.csv)|*.csv|Tutti i file (*.*)|*.*",
            DefaultExt = ".csv"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("IP,MAC,Hostname,Tipo,Connessione,Vendor,Certificato,Stato");
            foreach (var d in _netRows)
            {
                sb.AppendLine(
                    $"\"{d.Ip}\",\"{d.Mac}\",\"{d.Name}\",\"{d.DeviceType}\"," +
                    $"\"{d.ConnectionType}\",\"{d.Vendor}\",\"{d.CertStatus}\",\"{d.Status}\"");
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);
            SetStatus($"✅ Esportati {_netRows.Count} device in {Path.GetFileName(dlg.FileName)}");
            // Apri il file
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
        catch (Exception ex) { SetStatus($"❌ Export fallito: {ex.Message}"); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // WINDOWS TOAST NOTIFICATION
    // ══════════════════════════════════════════════════════════════════════════

    private System.Windows.Forms.NotifyIcon? _trayIcon;

    private void EnsureTrayIcon()
    {
        if (_trayIcon != null) return;
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon    = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text    = "NovaSCM"
        };
        _trayIcon.DoubleClick += (_, _) =>
        {
            Show(); WindowState = WindowState.Normal; Activate();
        };
    }

    private void ShowToast(string title, string message)
    {
        try
        {
            EnsureTrayIcon();
            _trayIcon!.BalloonTipTitle   = title;
            _trayIcon!.BalloonTipText    = message;
            _trayIcon!.BalloonTipIcon    = System.Windows.Forms.ToolTipIcon.Info;
            _trayIcon!.ShowBalloonTip(4000);
        }
        catch { /* notifiche non critiche */ }
    }

    // Override della chiusura: rimuove tray icon
    protected override void OnClosed(EventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnClosed(e);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SCRIPT LIBRARY
    // ══════════════════════════════════════════════════════════════════════════

    private static readonly string ScriptDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "PolarisManager", "scripts");

    private record ScriptItem(string Icon, string Name, string Description, string Code, bool BuiltIn = true);

    private readonly List<ScriptItem> _scripts = [];

    private void InitScriptLibrary()
    {
        _scripts.Clear();
        _scripts.AddRange(new[]
        {
            new ScriptItem("🔍", "Info sistema",
                "Informazioni complete su OS, hardware e uptime",
                """
                $os   = Get-CimInstance Win32_OperatingSystem
                $cpu  = Get-CimInstance Win32_Processor | Select -First 1
                $ram  = [math]::Round($os.TotalVisibleMemorySize/1MB, 1)
                $free = [math]::Round($os.FreePhysicalMemory/1MB, 1)
                $up   = (Get-Date) - $os.LastBootUpTime
                Write-Host "=== SISTEMA ===" -ForegroundColor Cyan
                Write-Host "OS:      $($os.Caption) $($os.OSArchitecture)"
                Write-Host "Build:   $($os.BuildNumber)"
                Write-Host "CPU:     $($cpu.Name)"
                Write-Host "RAM:     $ram GB totali, $free GB liberi"
                Write-Host "Uptime:  $($up.Days)g $($up.Hours)h $($up.Minutes)m"
                Write-Host "Host:    $env:COMPUTERNAME"
                Write-Host "Utente:  $env:USERNAME"
                """),

            new ScriptItem("💿", "Spazio disco",
                "Mostra spazio libero su tutti i dischi",
                """
                Get-PSDrive -PSProvider FileSystem | Where {$_.Used -gt 0} | ForEach {
                    $tot  = [math]::Round(($_.Used + $_.Free)/1GB, 1)
                    $free = [math]::Round($_.Free/1GB, 1)
                    $pct  = [math]::Round($_.Used / ($_.Used + $_.Free) * 100)
                    $bar  = '#' * ($pct/5) + '-' * (20 - $pct/5)
                    Write-Host "$($_.Name):\ [$bar] $pct% — $free GB liberi / $tot GB totali"
                }
                """),

            new ScriptItem("🧹", "Pulizia file temporanei",
                "Cancella file temp, cache Windows Update, Prefetch",
                """
                $paths = @("$env:TEMP", "$env:WINDIR\Temp", "$env:WINDIR\Prefetch",
                           "$env:WINDIR\SoftwareDistribution\Download")
                $total = 0
                foreach ($p in $paths) {
                    if (Test-Path $p) {
                        $size = (Get-ChildItem $p -Recurse -ErrorAction SilentlyContinue |
                                 Measure-Object -Property Length -Sum).Sum
                        Remove-Item "$p\*" -Recurse -Force -ErrorAction SilentlyContinue
                        $total += $size
                        Write-Host "Pulito: $p ($([math]::Round($size/1MB, 1)) MB)"
                    }
                }
                Write-Host "`n✅ Totale liberato: $([math]::Round($total/1MB, 1)) MB" -ForegroundColor Green
                """),

            new ScriptItem("🌐", "Diagnostica rete",
                "IP, gateway, DNS, ping, route",
                """
                Write-Host "=== INTERFACCE ===" -ForegroundColor Cyan
                Get-NetIPAddress -AddressFamily IPv4 | Where {$_.PrefixOrigin -ne 'WellKnown'} |
                    Select InterfaceAlias, IPAddress, PrefixLength | Format-Table -AutoSize
                Write-Host "=== GATEWAY ===" -ForegroundColor Cyan
                Get-NetRoute -DestinationPrefix "0.0.0.0/0" | Select NextHop, InterfaceAlias | Format-Table
                Write-Host "=== DNS ===" -ForegroundColor Cyan
                Get-DnsClientServerAddress -AddressFamily IPv4 | Where {$_.ServerAddresses} |
                    Select InterfaceAlias, ServerAddresses | Format-Table -AutoSize
                Write-Host "=== PING GATEWAY ===" -ForegroundColor Cyan
                $gw = (Get-NetRoute -DestinationPrefix "0.0.0.0/0" | Select -First 1).NextHop
                Test-Connection $gw -Count 3 | Select Address, Latency | Format-Table
                """),

            new ScriptItem("⚡", "Flush DNS",
                "Svuota cache DNS e mostra statistiche",
                """
                Write-Host "Cache DNS prima:" -ForegroundColor Yellow
                $count = (Get-DnsClientCache | Measure-Object).Count
                Write-Host "  $count record in cache"
                Clear-DnsClientCache
                Write-Host "✅ Cache DNS svuotata" -ForegroundColor Green
                ipconfig /registerdns 2>$null
                Write-Host "  DNS registrato"
                """),

            new ScriptItem("🔄", "Windows Update",
                "Controlla aggiornamenti disponibili",
                """
                Write-Host "Controllo aggiornamenti Windows..." -ForegroundColor Cyan
                $updates = (New-Object -ComObject Microsoft.Update.Session).CreateUpdateSearcher()
                try {
                    $res = $updates.Search("IsInstalled=0 and Type='Software'")
                    if ($res.Updates.Count -eq 0) {
                        Write-Host "✅ Nessun aggiornamento disponibile" -ForegroundColor Green
                    } else {
                        Write-Host "$($res.Updates.Count) aggiornamenti disponibili:" -ForegroundColor Yellow
                        $res.Updates | ForEach { Write-Host "  - $($_.Title)" }
                    }
                } catch {
                    Write-Host "Apertura Windows Update..." -ForegroundColor Yellow
                    Start-Process "ms-settings:windowsupdate"
                }
                """),

            new ScriptItem("📋", "Software installato",
                "Lista completa software (registry + store)",
                """
                Write-Host "=== SOFTWARE INSTALLATO ===" -ForegroundColor Cyan
                $reg = @(
                    'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
                    'HKLM:\Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
                    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*'
                )
                Get-ItemProperty $reg -ErrorAction SilentlyContinue |
                    Where {$_.DisplayName} |
                    Select DisplayName, DisplayVersion, Publisher |
                    Sort DisplayName |
                    Format-Table -AutoSize
                """),

            new ScriptItem("🔌", "Servizi in esecuzione",
                "Mostra tutti i servizi attivi con PID",
                """
                Get-Service | Where {$_.Status -eq 'Running'} |
                    Sort DisplayName |
                    Select DisplayName, ServiceName, Status |
                    Format-Table -AutoSize
                Write-Host "`nTotale servizi attivi: $((Get-Service | Where {$_.Status -eq 'Running'}).Count)"
                """),

            new ScriptItem("📊", "Processi pesanti",
                "Top 15 processi per uso CPU e RAM",
                """
                Write-Host "=== TOP PROCESSI (RAM) ===" -ForegroundColor Cyan
                Get-Process | Sort WorkingSet64 -Descending | Select -First 15 |
                    Select Name, Id,
                           @{N='RAM (MB)';E={[math]::Round($_.WorkingSet64/1MB,1)}},
                           @{N='CPU (s)'; E={[math]::Round($_.TotalProcessorTime.TotalSeconds,1)}} |
                    Format-Table -AutoSize
                """),

            new ScriptItem("⏱️", "Uptime & boot",
                "Ultimo avvio e statistiche sessione",
                """
                $os   = Get-CimInstance Win32_OperatingSystem
                $boot = $os.LastBootUpTime
                $up   = (Get-Date) - $boot
                Write-Host "Ultimo avvio: $($boot.ToString('dd/MM/yyyy HH:mm:ss'))"
                Write-Host "Uptime:       $($up.Days)g $($up.Hours)h $($up.Minutes)m $($up.Seconds)s"
                Write-Host "Sessione:     $env:USERNAME @ $env:COMPUTERNAME"
                $logon = Get-WinEvent -LogName Security -FilterXPath "*[System[EventID=4624]]" -MaxEvents 1 -ErrorAction SilentlyContinue
                if ($logon) { Write-Host "Ultimo logon: $($logon.TimeCreated)" }
                """),

            new ScriptItem("🛡️", "Antivirus status",
                "Stato Windows Defender e aggiornamento firme",
                """
                try {
                    $av = Get-MpComputerStatus
                    Write-Host "=== WINDOWS DEFENDER ===" -ForegroundColor Cyan
                    Write-Host "Abilitato:         $($av.AntivirusEnabled)"
                    Write-Host "Real-time:         $($av.RealTimeProtectionEnabled)"
                    Write-Host "Firme AV:          $($av.AntivirusSignatureVersion) ($($av.AntivirusSignatureLastUpdated.ToString('dd/MM/yyyy')))"
                    Write-Host "Ultima scansione:  $($av.QuickScanEndTime)"
                    Write-Host "Minacce trovate:   $($av.QuarantineCount) in quarantena"
                } catch {
                    Write-Host "Windows Defender non disponibile o non configurato" -ForegroundColor Yellow
                    Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct |
                        Select displayName, productState | Format-Table
                }
                """),

            new ScriptItem("🔐", "Abilita WinRM",
                "Abilita Windows Remote Management per gestione remota",
                """
                Write-Host "Abilitazione WinRM..." -ForegroundColor Yellow
                Enable-PSRemoting -Force -SkipNetworkProfileCheck
                Set-Item WSMan:\localhost\Client\TrustedHosts -Value "*" -Force
                Set-Service WinRM -StartupType Automatic
                Start-Service WinRM
                netsh advfirewall firewall add rule name="WinRM-HTTP" protocol=TCP dir=in localport=5985 action=allow 2>$null
                Write-Host "✅ WinRM abilitato — porta 5985" -ForegroundColor Green
                Write-Host "   Test: Test-WSMan -ComputerName <IP>"
                """),

            new ScriptItem("🌡️", "Temperatura CPU",
                "Legge temperatura processore via WMI",
                """
                $temp = Get-CimInstance MSAcpi_ThermalZoneTemperature -Namespace "root/wmi" -ErrorAction SilentlyContinue
                if ($temp) {
                    $temp | ForEach {
                        $c = [math]::Round($_.CurrentTemperature / 10 - 273.15, 1)
                        Write-Host "Zona: $($_.InstanceName) → $c °C"
                    }
                } else {
                    Write-Host "Temperatura non disponibile via WMI standard" -ForegroundColor Yellow
                    Write-Host "Installa HWiNFO o usa sensori BIOS per lettura precisa"
                }
                """),

            new ScriptItem("📁", "Cartelle pesanti",
                "Trova le 10 cartelle più grandi nel profilo utente",
                """
                Write-Host "Analisi cartelle in $env:USERPROFILE..." -ForegroundColor Cyan
                Get-ChildItem $env:USERPROFILE -Directory -ErrorAction SilentlyContinue |
                    ForEach {
                        $size = (Get-ChildItem $_.FullName -Recurse -ErrorAction SilentlyContinue |
                                 Measure-Object -Property Length -Sum).Sum
                        [PSCustomObject]@{Cartella=$_.Name; 'Dim (GB)'=[math]::Round($size/1GB,2)}
                    } |
                    Sort 'Dim (GB)' -Descending | Select -First 10 |
                    Format-Table -AutoSize
                """),
        });

        // Aggiungi script personalizzati salvati su disco
        if (Directory.Exists(ScriptDir))
        {
            foreach (var f in Directory.GetFiles(ScriptDir, "*.ps1"))
            {
                var name = Path.GetFileNameWithoutExtension(f);
                var code = File.ReadAllText(f);
                _scripts.Add(new ScriptItem("📝", name, "Script personalizzato", code, BuiltIn: false));
            }
        }

        ScriptList.Items.Clear();
        foreach (var sc in _scripts)
            ScriptList.Items.Add($"{sc.Icon}  {sc.Name}");

        if (ScriptList.Items.Count > 0) ScriptList.SelectedIndex = 0;
    }

    private void ScriptList_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (ScriptList.SelectedIndex < 0 || ScriptList.SelectedIndex >= _scripts.Count) return;
        var sc = _scripts[ScriptList.SelectedIndex];
        TxtScriptName.Text   = $"{sc.Icon}  {sc.Name}";
        TxtScriptDesc.Text   = sc.Description;
        TxtScriptEditor.Text = sc.Code.Trim();
    }

    private async void BtnRunScriptOnPc_Click(object s, RoutedEventArgs e)
    {
        try
        {
        var ip = TxtScriptTarget.Text.Trim();
        if (string.IsNullOrEmpty(ip) && NetGrid.SelectedItem is DeviceRow dev)
            ip = dev.Ip;
        if (string.IsNullOrEmpty(ip))
        { TxtScriptRunStatus.Text = "⚠️ Nessun IP target — seleziona un device in Rete o inserisci IP"; return; }

        var code = TxtScriptEditor.Text;
        if (string.IsNullOrWhiteSpace(code)) return;

        TxtScriptRunStatus.Text = $"⏳ Esecuzione su {ip}...";
        TxtScriptOutput.Text    = "";

        var result = await Task.Run(() =>
        {
            try
            {
                // Crea script temporaneo
                var tmp = Path.GetTempFileName() + ".ps1";
                var wrapped =
                    "$ErrorActionPreference = 'Continue'\n" +
                    $"Invoke-Command -ComputerName '{ip}' -ScriptBlock {{\n" +
                    code + "\n} -ErrorAction Stop";
                File.WriteAllText(tmp, wrapped);
                var psi = new ProcessStartInfo("powershell.exe",
                    $"-NonInteractive -NoProfile -ExecutionPolicy Bypass -File \"{tmp}\"")
                {
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true
                };
                using var proc = Process.Start(psi)!;
                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(30000);
                File.Delete(tmp);
                return string.IsNullOrEmpty(stderr) ? stdout : $"{stdout}\n[ERRORE]\n{stderr}";
            }
            catch (Exception ex) { return $"❌ {ex.Message}"; }
        });

        TxtScriptOutput.Text    = result;
        TxtScriptRunStatus.Text = "✅ Completato";
        }
        catch (Exception ex) { App.Log($"[BtnRunScriptOnPc_Click] {ex.Message}"); SetStatus($"❌ {ex.Message}"); }
    }

    private async void BtnRunScriptLocal_Click(object s, RoutedEventArgs e)
    {
        try
        {
        var code = TxtScriptEditor.Text;
        if (string.IsNullOrWhiteSpace(code)) return;

        TxtScriptRunStatus.Text = "⏳ Esecuzione locale...";
        TxtScriptOutput.Text    = "";

        var result = await Task.Run(() =>
        {
            try
            {
                var tmp = Path.GetTempFileName() + ".ps1";
                File.WriteAllText(tmp, code);
                var psi = new ProcessStartInfo("powershell.exe",
                    $"-NonInteractive -NoProfile -ExecutionPolicy Bypass -File \"{tmp}\"")
                {
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true
                };
                using var proc = Process.Start(psi)!;
                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(30000);
                File.Delete(tmp);
                return string.IsNullOrEmpty(stderr) ? stdout : $"{stdout}\n[ERRORE]\n{stderr}";
            }
            catch (Exception ex) { return $"❌ {ex.Message}"; }
        });

        TxtScriptOutput.Text    = result;
        TxtScriptRunStatus.Text = "✅ Completato";
        }
        catch (Exception ex) { App.Log($"[BtnRunScriptLocal_Click] {ex.Message}"); SetStatus($"❌ {ex.Message}"); }
    }

    private void BtnSaveScript_Click(object s, RoutedEventArgs e)
    {
        if (ScriptList.SelectedIndex < 0) return;
        var sc   = _scripts[ScriptList.SelectedIndex];
        var code = TxtScriptEditor.Text;
        Directory.CreateDirectory(ScriptDir);
        var path = Path.Combine(ScriptDir, $"{sc.Name}.ps1");
        File.WriteAllText(path, code);
        SetStatus($"💾 Script salvato: {Path.GetFileName(path)}");
        TxtScriptRunStatus.Text = "Salvato ✓";
    }

    private void BtnExportScript_Click(object s, RoutedEventArgs e)
    {
        var name = ScriptList.SelectedIndex >= 0 ? _scripts[ScriptList.SelectedIndex].Name : "script";
        var dlg  = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Esporta script PowerShell", FileName = $"{name}.ps1",
            Filter = "PowerShell (*.ps1)|*.ps1|Tutti i file|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, TxtScriptEditor.Text);
        Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
    }

    private void BtnNewScript_Click(object s, RoutedEventArgs e)
    {
        var name = Microsoft.VisualBasic.Interaction.InputBox(
            "Nome del nuovo script:", "Nuovo script", "MioScript");
        if (string.IsNullOrWhiteSpace(name)) return;
        _scripts.Add(new ScriptItem("📝", name, "Script personalizzato",
            "# Il tuo script PowerShell\nWrite-Host 'Hello from NovaSCM!'", BuiltIn: false));
        ScriptList.Items.Add($"📝  {name}");
        ScriptList.SelectedIndex = ScriptList.Items.Count - 1;
    }

    private void BtnDeleteScript_Click(object s, RoutedEventArgs e)
    {
        if (ScriptList.SelectedIndex < 0) return;
        var sc = _scripts[ScriptList.SelectedIndex];
        if (sc.BuiltIn) { SetStatus("⚠️ Gli script built-in non possono essere eliminati"); return; }
        _scripts.RemoveAt(ScriptList.SelectedIndex);
        ScriptList.Items.RemoveAt(ScriptList.SelectedIndex);
        var path = Path.Combine(ScriptDir, $"{sc.Name}.ps1");
        if (File.Exists(path)) File.Delete(path);
    }

    private void BtnClearScriptOutput_Click(object s, RoutedEventArgs e) =>
        TxtScriptOutput.Text = "";

    // ══════════════════════════════════════════════════════════════════════════
    // AUTO-SCAN PROGRAMMATO
    // ══════════════════════════════════════════════════════════════════════════

    private System.Windows.Threading.DispatcherTimer? _autoScanTimer;
    private bool _autoScanActive = false;

    private void BtnAutoScan_Click(object s, RoutedEventArgs e)
    {
        if (_autoScanActive)
        {
            _autoScanTimer?.Stop();
            _autoScanTimer = null;
            _autoScanActive = false;
            BtnAutoScan.Content    = "⏰  Auto";
            BtnAutoScan.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(217, 119, 6)); // amber
            SetStatus("⏹ Auto-scan disattivato");
        }
        else
        {
            var minutes = (CboAutoScanInterval.SelectedIndex) switch
            {
                0 => 5, 1 => 15, 2 => 30, _ => 60
            };
            _autoScanTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(minutes)
            };
            _autoScanTimer.Tick += async (_, _) => await TriggerAutoScanAsync();
            _autoScanTimer.Start();
            _autoScanActive = true;
            BtnAutoScan.Content    = $"⏰  Auto ({minutes}m)";
            BtnAutoScan.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(21, 128, 61)); // green
            SetStatus($"✅ Auto-scan attivo ogni {minutes} minuti");
        }
    }

    private async Task TriggerAutoScanAsync()
    {
        if (_scanCts != null) return; // scansione già in corso
        SetStatus("⏰ Auto-scan avviato...");
        if (!IPAddress.TryParse(TxtScanIp.Text.Trim(), out var baseIp)) return;
        if (!int.TryParse(TxtScanSubnet.Text.Trim(), out int cidr)) return;
        try
        {
            var ips = GetHostsInSubnet(baseIp, cidr);
            await RunScanAsync(ips);
        }
        catch { }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // NOTE & TAG DEVICE
    // ══════════════════════════════════════════════════════════════════════════

    private readonly Dictionary<string, string> _deviceNotes = [];
    private readonly Dictionary<string, string> _deviceTags  = [];

    private void MenuAddNote_Click(object s, RoutedEventArgs e)
    {
        if (NetGrid.SelectedItem is not DeviceRow dev) return;
        var current = _deviceNotes.TryGetValue(dev.Ip, out var n) ? n : "";
        var note = Microsoft.VisualBasic.Interaction.InputBox(
            $"Nota per {dev.Ip} ({dev.Name}):", "Aggiungi nota", current);
        if (note == null) return;
        if (string.IsNullOrWhiteSpace(note))
            _deviceNotes.Remove(dev.Ip);
        else
            _deviceNotes[dev.Ip] = note;
        Database.SaveDeviceNote(dev.Ip, note);
        SetStatus(string.IsNullOrWhiteSpace(note)
            ? $"🗑 Nota rimossa per {dev.Ip}"
            : $"📝 Nota salvata per {dev.Ip}");
    }

    private void MenuTagDevice_Click(object s, RoutedEventArgs e)
    {
        if (NetGrid.SelectedItem is not DeviceRow dev) return;
        var current = _deviceTags.TryGetValue(dev.Ip, out var t) ? t : "";
        var tag = Microsoft.VisualBasic.Interaction.InputBox(
            $"Tag per {dev.Ip} (es: server, stampante, iot, critico):",
            "Tag device", current);
        if (tag == null) return;
        if (string.IsNullOrWhiteSpace(tag))
            _deviceTags.Remove(dev.Ip);
        else
            _deviceTags[dev.Ip] = tag;
        Database.SaveDeviceTag(dev.Ip, tag);
        SetStatus(string.IsNullOrWhiteSpace(tag)
            ? $"🏷️ Tag rimosso per {dev.Ip}"
            : $"🏷️ Tag '{tag}' assegnato a {dev.Ip}");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // HELPER: nascondi tutti i pannelli extra del tab Rete
    // ══════════════════════════════════════════════════════════════════════════

    private void HideAllNetPanels()
    {
        RadarPanel.Visibility      = Visibility.Collapsed;
        MapPanel.Visibility        = Visibility.Collapsed;
        HeatmapPanel.Visibility    = Visibility.Collapsed;
        TraceroutePanel.Visibility = Visibility.Collapsed;
        SpeedTestPanel.Visibility  = Visibility.Collapsed;
        NetGrid.Visibility         = Visibility.Visible;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // NETWORK CHANGE LOG
    // ══════════════════════════════════════════════════════════════════════════

    private List<string> _prevScanIps = new();

    private void ShowChangeLog(List<DeviceRow> current)
    {
        var currentIps = current.Select(d => d.Ip).ToHashSet();
        if (_prevScanIps.Count == 0)
        {
            _prevScanIps = currentIps.ToList();
            return;
        }
        var prev    = _prevScanIps.ToHashSet();
        var added   = currentIps.Except(prev).OrderBy(x => x).ToList();
        var removed = prev.Except(currentIps).OrderBy(x => x).ToList();

        if (added.Count == 0 && removed.Count == 0)
        {
            _prevScanIps = currentIps.ToList();
            return;
        }

        var sb = new System.Text.StringBuilder();
        if (added.Count > 0)
        {
            sb.AppendLine($"🟢  NUOVI ({added.Count}):");
            foreach (var ip in added)
            {
                var dev = current.FirstOrDefault(d => d.Ip == ip);
                sb.AppendLine($"   + {ip,-16} {dev?.Name ?? "",-24} {dev?.Vendor ?? ""}");
            }
        }
        if (removed.Count > 0)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine($"🔴  SPARITI ({removed.Count}):");
            foreach (var ip in removed)
                sb.AppendLine($"   - {ip}");
        }

        TxtChangeLog.Text         = sb.ToString();
        TxtChangeLogTime.Text     = $"— Scan {DateTime.Now:HH:mm:ss}";
        ChangeLogPanel.Visibility = Visibility.Visible;
        _prevScanIps = currentIps.ToList();
    }

    private void BtnCloseChangeLog_Click(object s, RoutedEventArgs e)
        => ChangeLogPanel.Visibility = Visibility.Collapsed;

    // ══════════════════════════════════════════════════════════════════════════
    // REPORT HTML
    // ══════════════════════════════════════════════════════════════════════════

    private void BtnExportHtml_Click(object s, RoutedEventArgs e)
    {
        var devices = _netRows.ToList();
        if (devices.Count == 0)
        {
            MessageBox.Show("Nessun dispositivo. Esegui prima una scansione.", "Report HTML",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title    = "Salva Report HTML",
            Filter   = "Pagina Web|*.html",
            FileName = $"NovaSCM-Report-{DateTime.Now:yyyyMMdd-HHmm}.html"
        };
        if (dlg.ShowDialog() != true) return;
        var html = BuildNetworkReportHtml(devices);
        File.WriteAllText(dlg.FileName, html, System.Text.Encoding.UTF8);
        Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
    }

    private string BuildNetworkReportHtml(List<DeviceRow> devices)
    {
        var ts      = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        var online  = devices.Count(d => d.Status?.Contains("Online") == true);
        var offline = devices.Count - online;
        var rows    = new System.Text.StringBuilder();
        foreach (var d in devices.OrderBy(x =>
        {
            if (System.Net.IPAddress.TryParse(x.Ip, out var ip))
            {
                var b = ip.GetAddressBytes();
                return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
            }
            return 0;
        }))
        {
            var isOnline    = d.Status?.Contains("Online") == true;
            var statusStyle = isOnline ? "color:#16a34a;font-weight:bold" : "color:#dc2626";
            var statusText  = isOnline ? "🟢 Online" : "⬛ Offline";
            rows.AppendLine(
                $"<tr><td>{d.Ip}</td><td>{System.Net.WebUtility.HtmlEncode(d.Name)}</td>" +
                $"<td style='font-family:monospace'>{d.Mac}</td>" +
                $"<td>{System.Net.WebUtility.HtmlEncode(d.Vendor)}</td>" +
                $"<td>{System.Net.WebUtility.HtmlEncode(d.DeviceType)}</td>" +
                $"<td style='{statusStyle}'>{statusText}</td>" +
                $"<td>{System.Net.WebUtility.HtmlEncode(d.ConnectionType)}</td></tr>");
        }
        const string css =
            "*{box-sizing:border-box;margin:0;padding:0}" +
            "body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;background:#f8fafc;color:#1e293b}" +
            "header{background:linear-gradient(135deg,#0d1b3e,#1e3a5f);color:white;padding:24px 32px}" +
            "header h1{font-size:22px;font-weight:700}" +
            "header p{color:#94a3b8;margin-top:4px;font-size:13px}" +
            ".stats{display:flex;gap:16px;padding:16px 32px;background:white;border-bottom:1px solid #e2e8f0}" +
            ".stat{background:#f1f5f9;border-radius:8px;padding:12px 20px;min-width:110px}" +
            ".stat .val{font-size:26px;font-weight:700;color:#0d1b3e}" +
            ".stat .lbl{font-size:11px;color:#64748b;margin-top:2px}" +
            "main{padding:20px 32px}" +
            "input[type=search]{width:100%;padding:9px 14px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;margin-bottom:14px;outline:none}" +
            "table{width:100%;border-collapse:collapse;background:white;border-radius:8px;overflow:hidden;box-shadow:0 1px 4px rgba(0,0,0,.08);font-size:13px}" +
            "th{background:#1e293b;color:white;padding:9px 12px;text-align:left;font-weight:600;cursor:pointer;user-select:none}" +
            "th:hover{background:#334155}" +
            "td{padding:7px 12px;border-bottom:1px solid #f1f5f9}" +
            "tr:hover td{background:#f8fafc}" +
            "footer{text-align:center;padding:18px;color:#94a3b8;font-size:12px}" +
            "footer a{color:#60a5fa}";
        const string js =
            "function filter(){var q=document.getElementById('q').value.toLowerCase();" +
            "document.querySelectorAll('#t tbody tr').forEach(function(r){r.style.display=r.textContent.toLowerCase().indexOf(q)>=0?'':'none'})}" +
            "var sd=1;function sort(c){var tb=document.querySelector('#t tbody');var rs=Array.from(tb.querySelectorAll('tr'));" +
            "rs.sort(function(a,b){var x=a.cells[c].textContent,y=b.cells[c].textContent;return x.localeCompare(y,undefined,{numeric:true})*sd});" +
            "sd*=-1;rs.forEach(function(r){tb.appendChild(r)})}";
        return
            "<!DOCTYPE html><html lang=\"it\"><head><meta charset=\"utf-8\">" +
            $"<title>NovaSCM Report — {ts}</title>" +
            "<style>" + css + "</style></head><body>" +
            "<header><h1>🖥️ NovaSCM — Network Report</h1>" +
            $"<p>Generato il {ts}</p></header>" +
            "<div class=\"stats\">" +
            $"<div class=\"stat\"><div class=\"val\">{devices.Count}</div><div class=\"lbl\">Totale</div></div>" +
            $"<div class=\"stat\"><div class=\"val\" style=\"color:#16a34a\">{online}</div><div class=\"lbl\">Online</div></div>" +
            $"<div class=\"stat\"><div class=\"val\" style=\"color:#dc2626\">{offline}</div><div class=\"lbl\">Offline</div></div>" +
            "</div><main>" +
            "<input type=\"search\" id=\"q\" placeholder=\"🔍  Cerca IP, hostname, MAC, vendor...\" oninput=\"filter()\">" +
            "<table id=\"t\"><thead><tr>" +
            "<th onclick=\"sort(0)\">IP ⇅</th><th onclick=\"sort(1)\">Hostname ⇅</th>" +
            "<th onclick=\"sort(2)\">MAC</th><th onclick=\"sort(3)\">Vendor ⇅</th>" +
            "<th onclick=\"sort(4)\">Tipo ⇅</th><th onclick=\"sort(5)\">Stato ⇅</th>" +
            "<th onclick=\"sort(6)\">Connessione ⇅</th>" +
            "</tr></thead><tbody>" + rows + "</tbody></table></main>" +
            $"<footer>Generato da <b>NovaSCM v{CurrentVersion}</b> • " +
            "<a href=\"https://github.com/ClaudioBecchis/NovaSCM\">github.com/ClaudioBecchis/NovaSCM</a></footer>" +
            "<script>" + js + "</script></body></html>";
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TAB PROXMOX
    // ══════════════════════════════════════════════════════════════════════════

    private record PveNode(string Id, string Name, string Status,
                           double Cpu, long Memory, long MaxMem, int VmCount)
    {
        public string StatusIcon => Status == "online" ? "🟢" : "🔴";
        public string CpuText    => $"CPU: {Cpu * 100:F1} %";
        public string RamText    => $"RAM: {Memory / 1073741824.0:F1} / {MaxMem / 1073741824.0:F1} GB";
        public string GuestText  => $"{VmCount} VM/CT";
    }

    private record PveGuest(int Vmid, string Name, string Type, string Status, string Node,
                            double Cpu, long Mem, long MaxMem, long Disk, long MaxDisk, long Uptime)
    {
        public string TypeLabel   => Type == "qemu" ? "VM" : "CT";
        public string StatusLabel => Status switch
        {
            "running" => "▶ running", "stopped" => "⏹ stopped",
            "paused"  => "⏸ paused", _ => Status
        };
        public string CpuLabel    => Status == "running" ? $"{Cpu * 100:F1} %" : "—";
        public string RamLabel    => MaxMem > 0 ? $"{Mem / 1048576:N0} / {MaxMem / 1048576:N0} MB" : "—";
        public string DiskLabel   => MaxDisk > 0 ? $"{MaxDisk / 1073741824.0:F0} GB" : "—";
        public string UptimeLabel => Uptime > 0 ? FormatUptime(Uptime) : "—";
        static string FormatUptime(long s) =>
            s < 3600   ? $"{s / 60}m" :
            s < 86400  ? $"{s / 3600}h {s % 3600 / 60}m" :
                         $"{s / 86400}d {s % 86400 / 3600}h";
    }

    private HttpClient? _pveHttp;
    private string _pveCookie    = "";
    private string _pveCsrfToken = "";
    private string _pveHost      = "";
    private readonly List<PveGuest> _pveAllGuests = new();

    private void EnsurePveHttp()
    {
        if (_pveHttp != null) return;
        var handler = new HttpClientHandler
        {
            // Accetta solo UntrustedRoot (Proxmox self-signed); blocca scaduti, revocati, hostname errato
            ServerCertificateCustomValidationCallback = (_, _, chain, errors) =>
                errors == System.Net.Security.SslPolicyErrors.None ||
                (errors == System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors &&
                 chain?.ChainStatus.Length == 1 &&
                 chain.ChainStatus[0].Status == System.Security.Cryptography.X509Certificates.X509ChainStatusFlags.UntrustedRoot)
        };
        _pveHttp = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    }

    private async void BtnPveConnect_Click(object s, RoutedEventArgs e)
    {
        EnsurePveHttp();
        _pveHost = TxtPveHost.Text.Trim();
        var user = TxtPveUser.Text.Trim();
        var pass = TxtPvePass.Password;
        TxtPveStatus.Text       = "⏳ Connessione...";
        TxtPveStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 191, 36));
        try
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = user, ["password"] = pass
            });
            var resp = await _pveHttp!.PostAsync(
                $"https://{_pveHost}:8006/api2/json/access/ticket", form);
            resp.EnsureSuccessStatusCode();
            var doc  = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var data = doc.RootElement.GetProperty("data");
            _pveCookie    = data.GetProperty("ticket").GetString()              ?? "";
            _pveCsrfToken = data.GetProperty("CSRFPreventionToken").GetString() ?? "";
            _pveHttp.DefaultRequestHeaders.Remove("CSRFPreventionToken");
            _pveHttp.DefaultRequestHeaders.Add("CSRFPreventionToken", _pveCsrfToken);
            _pveHttp.DefaultRequestHeaders.Remove("Cookie");
            _pveHttp.DefaultRequestHeaders.Add("Cookie", $"PVEAuthCookie={_pveCookie}");
            TxtPveStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 222, 128));
            BtnPveRefresh.IsEnabled = true;
            BtnPveTemp.IsEnabled    = true;
            await PveRefreshAsync();
        }
        catch (Exception ex)
        {
            TxtPveStatus.Text       = $"❌ {ex.Message}";
            TxtPveStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
        }
    }

    private async void BtnPveRefresh_Click(object s, RoutedEventArgs e)
    {
        try { await PveRefreshAsync(); }
        catch (Exception ex) { App.Log($"[BtnPveRefresh_Click] {ex.Message}"); SetStatus($"❌ {ex.Message}"); }
    }

    private async Task PveRefreshAsync()
    {
        if (_pveHttp == null || _pveHost == "") return;
        try
        {
            var nodesJson = await _pveHttp.GetStringAsync(
                $"https://{_pveHost}:8006/api2/json/nodes");
            var nodesDoc = JsonDocument.Parse(nodesJson);
            var nodeList = new List<PveNode>();
            _pveAllGuests.Clear();

            foreach (var n in nodesDoc.RootElement.GetProperty("data").EnumerateArray())
            {
                var nodeId = n.TryGetProperty("node",   out var nid)  ? nid.GetString()  ?? "" : "";
                var status = n.TryGetProperty("status", out var nst)  ? nst.GetString()  ?? "" : "";
                var cpu    = n.TryGetProperty("cpu",    out var nc)   ? nc.GetDouble()        : 0;
                var mem    = n.TryGetProperty("mem",    out var nm)   ? nm.GetInt64()         : 0;
                var maxMem = n.TryGetProperty("maxmem", out var nmm)  ? nmm.GetInt64()        : 0;
                int guestCount = 0;
                foreach (var typeStr in new[] { "qemu", "lxc" })
                {
                    try
                    {
                        var json = await _pveHttp.GetStringAsync(
                            $"https://{_pveHost}:8006/api2/json/nodes/{nodeId}/{typeStr}");
                        foreach (var el in JsonDocument.Parse(json).RootElement
                                 .GetProperty("data").EnumerateArray())
                        {
                            _pveAllGuests.Add(ParsePveGuest(el, nodeId, typeStr));
                            guestCount++;
                        }
                    }
                    catch { }
                }
                nodeList.Add(new PveNode(nodeId, nodeId, status, cpu, mem, maxMem, guestCount));
            }

            LstPveNodes.ItemsSource = nodeList;
            if (nodeList.Count > 0 && LstPveNodes.SelectedIndex < 0)
                LstPveNodes.SelectedIndex = 0;
            PveRefreshGrid();
            TxtPveStatus.Text = $"✅ {nodeList.Count} nodi • {_pveAllGuests.Count} VM/CT";
        }
        catch (Exception ex)
        {
            TxtPveStatus.Text       = $"❌ {ex.Message}";
            TxtPveStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
        }
    }

    private static PveGuest ParsePveGuest(JsonElement el, string node, string type)
    {
        int    vmid    = el.TryGetProperty("vmid",    out var v)   ? v.GetInt32()   : 0;
        string name    = el.TryGetProperty("name",    out var vn)  ? vn.GetString() ?? $"vm{vmid}" : $"vm{vmid}";
        string status  = el.TryGetProperty("status",  out var vs)  ? vs.GetString() ?? ""          : "";
        double cpu     = el.TryGetProperty("cpu",     out var vc)  ? vc.GetDouble()                : 0;
        long   mem     = el.TryGetProperty("mem",     out var vm)  ? vm.GetInt64()                 : 0;
        long   maxMem  = el.TryGetProperty("maxmem",  out var vmm) ? vmm.GetInt64()                : 0;
        long   disk    = el.TryGetProperty("disk",    out var vd)  ? vd.GetInt64()                 : 0;
        long   maxDisk = el.TryGetProperty("maxdisk", out var vmd) ? vmd.GetInt64()                : 0;
        long   uptime  = el.TryGetProperty("uptime",  out var vu)  ? vu.GetInt64()                 : 0;
        return new PveGuest(vmid, name, type, status, node, cpu, mem, maxMem, disk, maxDisk, uptime);
    }

    private void PveRefreshGrid()
    {
        var nodeId = (LstPveNodes.SelectedItem as PveNode)?.Id;
        var filter = TxtPveFilter.Text.Trim().ToLower();
        DgPveGuests.ItemsSource = _pveAllGuests
            .Where(g => nodeId == null || g.Node == nodeId)
            .Where(g => filter == "" ||
                        g.Name.ToLower().Contains(filter) ||
                        g.Vmid.ToString().Contains(filter))
            .OrderBy(g => g.Vmid)
            .ToList();
    }

    private void LstPveNodes_SelectionChanged(object s, SelectionChangedEventArgs e)
        => PveRefreshGrid();

    private void DgPveGuests_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        var g = DgPveGuests.SelectedItem as PveGuest;
        BtnPveStart.IsEnabled   = g?.Status == "stopped";
        BtnPveStop.IsEnabled    = g?.Status == "running";
        BtnPveReboot.IsEnabled  = g?.Status == "running";
        BtnPveSuspend.IsEnabled = g?.Status == "running" && g.Type == "qemu";
        BtnPveConsole.IsEnabled = g?.Status == "running";
    }

    private void TxtPveFilter_TextChanged(object s, TextChangedEventArgs e)
        => PveRefreshGrid();

    private async void BtnPveStart_Click(object s, RoutedEventArgs e)
        => await PveGuestActionAsync("start");
    private async void BtnPveStop_Click(object s, RoutedEventArgs e)
        => await PveGuestActionAsync("stop");
    private async void BtnPveReboot_Click(object s, RoutedEventArgs e)
        => await PveGuestActionAsync("reboot");
    private async void BtnPveSuspend_Click(object s, RoutedEventArgs e)
        => await PveGuestActionAsync("suspend");

    private async Task PveGuestActionAsync(string action)
    {
        var g = DgPveGuests.SelectedItem as PveGuest;
        if (g == null || _pveHttp == null) return;
        try
        {
            var url = $"https://{_pveHost}:8006/api2/json/nodes/{g.Node}/{g.Type}/{g.Vmid}/status/{action}";
            (await _pveHttp.PostAsync(url, new StringContent(""))).EnsureSuccessStatusCode();
            TxtPveStatus.Text = $"✅ {action} → {g.Name}";
            await Task.Delay(2000);
            await PveRefreshAsync();
        }
        catch (Exception ex) { TxtPveStatus.Text = $"❌ {ex.Message}"; }
    }

    private void BtnPveConsole_Click(object s, RoutedEventArgs e)
    {
        var g = DgPveGuests.SelectedItem as PveGuest;
        if (g == null) return;
        var url = $"https://{_pveHost}:8006/?console=kvm&novnc=1&vmid={g.Vmid}" +
                  $"&vmname={Uri.EscapeDataString(g.Name)}&node={g.Node}&resize=off&cmd=";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    // ── Temperature Proxmox ───────────────────────────────────────────────────

    private record PveTempReading(string Label, double Celsius)
    {
        public string TempText  => $"{Celsius:F1}°C";
        public System.Windows.Media.Brush TempColor =>
            Celsius >= 80 ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68,  68))  :
            Celsius >= 65 ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 191, 36))  :
                            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(74,  222, 128));
    }

    private async void BtnPveTemp_Click(object s, RoutedEventArgs e)
    {
        TxtPveStatus.Text = "🌡️ Lettura temperature...";
        try
        {
            var keyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".ssh", "id_ed25519");
            // SEC-03: ArgumentList evita injection da _pveHost (valore UI)
            var psi = new ProcessStartInfo("ssh.exe")
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true
            };
            psi.ArgumentList.Add("-i");              psi.ArgumentList.Add(keyPath);
            psi.ArgumentList.Add("-o");              psi.ArgumentList.Add("StrictHostKeyChecking=accept-new");
            psi.ArgumentList.Add("-o");              psi.ArgumentList.Add("ConnectTimeout=5");
            psi.ArgumentList.Add($"root@{_pveHost}");
            psi.ArgumentList.Add("sensors -j 2>/dev/null");
            var proc = Process.Start(psi)!;
            var json = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var readings = new List<PveTempReading>();

            if (!string.IsNullOrWhiteSpace(json) && json.TrimStart().StartsWith("{"))
            {
                // Parse sensors -j output
                var doc = JsonDocument.Parse(json);
                foreach (var chip in doc.RootElement.EnumerateObject())
                {
                    foreach (var feature in chip.Value.EnumerateObject())
                    {
                        if (feature.Value.ValueKind != JsonValueKind.Object) continue;
                        foreach (var sub in feature.Value.EnumerateObject())
                        {
                            if (sub.Name.EndsWith("_input") &&
                                sub.Value.ValueKind == JsonValueKind.Number)
                            {
                                var temp = sub.Value.GetDouble();
                                if (temp > 0 && temp < 150)
                                {
                                    var label = feature.Name
                                        .Replace("Package id", "CPU Pkg")
                                        .Replace("Composite", "NVMe")
                                        .Replace("temp", "T");
                                    readings.Add(new PveTempReading(label, temp));
                                }
                            }
                        }
                    }
                }
            }

            if (readings.Count == 0)
            {
                // Fallback: thermal_zone sysfs
                // SEC-03: ArgumentList evita injection da _pveHost (valore UI)
                var psi2 = new ProcessStartInfo("ssh.exe")
                {
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow         = true
                };
                psi2.ArgumentList.Add("-i");              psi2.ArgumentList.Add(keyPath);
                psi2.ArgumentList.Add("-o");              psi2.ArgumentList.Add("StrictHostKeyChecking=accept-new");
                psi2.ArgumentList.Add("-o");              psi2.ArgumentList.Add("ConnectTimeout=5");
                psi2.ArgumentList.Add($"root@{_pveHost}");
                // SEC-03b: comando POSIX-compatibile (evita bash process substitution)
                psi2.ArgumentList.Add("for z in /sys/class/thermal/thermal_zone*; do printf '%s\\t%s\\n' \"$(cat $z/type)\" \"$(cat $z/temp)\"; done");
                var proc2  = Process.Start(psi2)!;
                var output = await proc2.StandardOutput.ReadToEndAsync();
                await proc2.WaitForExitAsync();
                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split('\t');
                    if (parts.Length == 2 && double.TryParse(parts[1].Trim(), out var raw))
                        readings.Add(new PveTempReading(parts[0].Trim(), raw / 1000.0));
                }
            }

            if (readings.Count > 0)
            {
                PveTempItems.ItemsSource  = readings;
                PveTempPanel.Visibility   = Visibility.Visible;
                TxtPveStatus.Text         = $"✅ {readings.Count} sensori letti";
            }
            else
            {
                TxtPveStatus.Text = "⚠️ Nessun sensore trovato (installa lm-sensors)";
            }
        }
        catch (Exception ex)
        {
            TxtPveStatus.Text = $"❌ SSH: {ex.Message}";
        }
    }

    private void BtnClosePveTemp_Click(object s, RoutedEventArgs e)
        => PveTempPanel.Visibility = Visibility.Collapsed;
}
