using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
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

record CertRow(string Icon, string Name, string Mac, string Created, string Expires, string Status);
record AppQueueRow(string Pc, string Ip, string Mac, string Apps, string Status);
record AppCatRow(string Category, string Items);
record OpsiRow(string Name, string Version, string Status, string Updated);
record PcRow(string Icon, string Name, string Ip, string Os, string Cpu, string Ram, string Status, string Agent);

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

    public MainWindow()
    {
        InitializeComponent();
        // Forza render del primo tab al caricamento
        Dispatcher.BeginInvoke(() =>
        {
            MainTabs.SelectedIndex = 0;
            UpdateNavState(0);
        }, System.Windows.Threading.DispatcherPriority.Render);
        bool firstRun = !File.Exists(ConfigPath);
        LoadConfig();
        NetGrid.ItemsSource     = _netRows;
        LstPackages.ItemsSource = _deployPackages;
        LoadDemoData();
        RefreshProfiles();
        InitCrTab();
        InitWorkflowTab();
        _ = LoadOuiDatabaseAsync();
        TxtAboutVersion.Text = $"v{CurrentVersion} ";
        // Controlla aggiornamenti in background 3s dopo l'avvio
        Dispatcher.BeginInvoke(async () => await CheckForUpdatesAsync(silent: true),
                               System.Windows.Threading.DispatcherPriority.Background);

        if (firstRun)
        {
            // Prima esecuzione: apre tab Impostazioni con messaggio di benvenuto
            Dispatcher.BeginInvoke(() =>
            {
                MainTabs.SelectedIndex = MainTabs.Items.Count - 2; // tab Impostazioni
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
        ApplyConfigToUI();
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
        TxtNovaSCMApiUrl.Text = _config.NovaSCMApiUrl;
    }

    private void SaveConfig()
    {
        _config.CertportalUrl = TxtCertportalUrl.Text.Trim();
        _config.UnifiUrl      = TxtUnifiUrl.Text.Trim();
        _config.UnifiUser     = TxtUnifiUser.Text.Trim();
        _config.UnifiPass     = TxtUnifiPass.Password;
        _config.Ssid          = TxtSsid.Text.Trim();
        _config.RadiusIp      = TxtRadiusIp.Text.Trim();
        _config.CertDays      = TxtCertDays.Text.Trim();
        _config.OrgName       = TxtOrgName.Text.Trim();
        _config.Domain        = TxtDomain.Text.Trim();
        _config.ScanNetwork   = TxtScanIp.Text.Trim();
        _config.ScanSubnet    = TxtScanSubnet.Text.Trim();
        _config.ScanNetworks  = TxtScanNetworks.Text;
        _config.AdminUser     = TxtAdminUser.Text.Trim();
        _config.AdminPass     = TxtAdminPass.Password;
        _config.NovaSCMApiUrl = TxtNovaSCMApiUrl.Text.Trim();
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

    private async Task RunScanAsync(List<IPAddress> ips)
    {
        _netRows.Clear();
        BtnScan.IsEnabled    = false;
        BtnScanAll.IsEnabled = false;
        BtnStop.IsEnabled    = true;
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

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
                            Dispatcher.Invoke(() => { _netRows.Add(row); found++; });
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
                                        Dispatcher.Invoke(() =>
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
                        Dispatcher.Invoke(() =>
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

            var p = Process.Start(new ProcessStartInfo("arp", $"-a {ip}")
            {
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                UseShellExecute = false
            });
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

            _ouiDb = ParseOuiFile(OuiDbPath);
            App.Log($"[OUI] Database caricato: {_ouiDb.Count} record");
        }
        catch (Exception ex) { App.Log($"[OUI] Errore: {ex.Message}"); }
    }

    private static Dictionary<string, string> ParseOuiFile(string path)
    {
        var lines  = File.ReadAllLines(path);
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
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
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
    private void LoadDemoData()
    {
        CertGrid.ItemsSource = new ObservableCollection<CertRow>
        {
            new("💻","PC-OFFICE-01","AA:BB:CC:11:22:33","2026-01-15","2036-01-15","✅ Attivo"),
            new("💻","PC-OFFICE-02","AA:BB:CC:11:22:34","2026-01-15","2036-01-15","✅ Attivo"),
            new("📱","Smartphone-1","AA:BB:CC:11:22:35","2026-02-01","2036-02-01","✅ Attivo"),
            new("💻","LAPTOP-01",   "AA:BB:CC:11:22:36","2026-03-01","2036-03-01","✅ Attivo"),
            new("💻","PC-VECCHIO",  "AA:BB:CC:11:22:37","2025-06-01","2035-06-01","⏸ Revocato"),
        };

        AppQueueGrid.ItemsSource = new ObservableCollection<AppQueueRow>
        {
            new("💻 pc-office-01","192.168.1.101","AA:BB:CC:11:22:33","VLC, Firefox", "⏳ In installazione"),
            new("💻 pc-office-02","192.168.1.102","AA:BB:CC:11:22:34","—",            "✅ Aggiornato"),
            new("💻 laptop-01",   "192.168.1.103","AA:BB:CC:11:22:36","7-Zip",        "⏳ In attesa"),
        };

        AppCatalog.ItemsSource = new[]
        {
            new AppCatRow("🌐 Browser",  "Firefox   │   Chrome   │   Brave   │   Edge"),
            new AppCatRow("📄 Office",   "LibreOffice   │   OnlyOffice   │   Notepad++"),
            new AppCatRow("🎬 Media",    "VLC   │   Spotify   │   MPC-HC   │   Kodi"),
            new AppCatRow("🔧 Utility",  "7-Zip   │   WinRAR   │   Everything   │   TreeSize"),
            new AppCatRow("💻 Dev",      "VS Code   │   Git   │   Python   │   Node.js"),
            new AppCatRow("⭐ Mie App",  "Pioneer MCACC   │   Custom App 1"),
        };

        OpsiGrid.ItemsSource = new ObservableCollection<OpsiRow>
        {
            new("firefox",       "132.0", "✅ OK",       "2026-03-01"),
            new("vlc",           "3.0.21","✅ OK",       "2026-02-28"),
            new("pioneer-mcacc", "1.0.0", "✅ OK",       "2026-03-04"),
            new("chrome",        "124.0", "⚠️ Aggiorna","2026-01-15"),
            new("7zip",          "24.08", "✅ OK",       "2026-03-02"),
        };

        PcGrid.ItemsSource = new ObservableCollection<PcRow>
        {
            new("💻","PC-OFFICE-01","192.168.1.101","Win 11 Pro", "12%","8.2/32 GB","🟢 Online","✅"),
            new("💻","PC-OFFICE-02","192.168.1.102","Win 11 Pro", "4%", "4.1/32 GB","🟢 Online","✅"),
            new("💻","LAPTOP-01",   "192.168.1.103","Win 11 Home","8%", "6.3/16 GB","🟢 Online","✅"),
            new("💻","PC-VECCHIO",  "—",            "Win 10 Pro", "—",  "—",        "🔴 Offline","⚠️"),
        };
    }

    // ── Handler pulsanti ──────────────────────────────────────────────────────
    private async void BtnRegisterDevices_Click(object s, RoutedEventArgs e)
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
                            }
                            else
                            {
                                existing.Status = "🟢 Online";
                                SetStatus($"🟢 Tornato online: {ip}");
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
            Process.Start("mstsc", $"/v:{p.Ip}");
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

    private void BtnSaveSettings_Click(object s, RoutedEventArgs e)
    {
        SaveConfig();
        TxtSettingsStatus.Text = "✅ Impostazioni salvate";
        TxtSettingsStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
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
                ? System.Windows.Media.Brushes.LightGreen
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
        "Rete", "Certificati", "Applicazioni", "OPSI",
        "PC", "Deploy OS", "Workflow", "Richieste",
        "Impostazioni", "About"
    ];

    private readonly Button[] _navBtns = [];

    private void Nav_Click(object s, RoutedEventArgs e)
    {
        if (s is Button btn && int.TryParse(btn.Tag?.ToString(), out int idx))
            MainTabs.SelectedIndex = idx;
    }

    private void UpdateNavState(int idx)
    {
        var btns = new[]
        {
            NavRete, NavCert, NavApp, NavOpsi, NavPc,
            NavDeploy, NavWorkflow, NavRichieste, NavImpostazioni, NavAbout
        };
        var active   = FindResource("NavSideBtnActive") as System.Windows.Style;
        var inactive = FindResource("NavSideBtn")       as System.Windows.Style;
        for (int i = 0; i < btns.Length; i++)
            btns[i].Style = i == idx ? active : inactive;

        TxtNavSection.Text = idx >= 0 && idx < _navSections.Length
            ? _navSections[idx] : "";
    }

    private const string CurrentVersion = "1.0.6";
    private string? _updateDownloadUrl;

    // ── Controlla aggiornamenti dal server NovaSCM ────────────────────────────

    private string? GetUpdateBaseUrl()
    {
        if (string.IsNullOrEmpty(_config.NovaSCMApiUrl)) return null;
        // Da "http://host:port/api/cr" → "http://host:port"
        var uri = new Uri(_config.NovaSCMApiUrl);
        return $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }

    private async Task CheckForUpdatesAsync(bool silent = true)
    {
        var baseUrl = GetUpdateBaseUrl();
        if (baseUrl == null) return;

        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var json = await http.GetStringAsync($"{baseUrl}/api/version");
            var doc  = JsonDocument.Parse(json);
            var serverVer = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
            var dlUrl     = doc.RootElement.TryGetProperty("url",     out var u) ? u.GetString() ?? "" : "";
            var notes     = doc.RootElement.TryGetProperty("notes",   out var n) ? n.GetString() ?? "" : "";

            bool hasUpdate = string.Compare(serverVer, CurrentVersion,
                                            StringComparison.OrdinalIgnoreCase) > 0;

            if (hasUpdate && !string.IsNullOrEmpty(dlUrl))
            {
                _updateDownloadUrl = dlUrl;
                TxtUpdateBanner.Text    = $"🔄  NovaSCM v{serverVer} disponibile" +
                                          (string.IsNullOrEmpty(notes) ? "" : $"  —  {notes}");
                UpdateBanner.Visibility = Visibility.Visible;

                // Toast notification
                var toast = new UpdateToast(serverVer, notes, () => BtnInstallUpdate_Click(this, new RoutedEventArgs()));
                toast.Show();

                if (!silent)
                {
                    TxtUpdateStatus.Text       = $"🆕  Nuova versione v{serverVer} disponibile — clicca il banner in alto o il toast!";
                    TxtUpdateStatus.Foreground = System.Windows.Media.Brushes.Orange;
                }
            }
            else if (!silent)
            {
                TxtUpdateStatus.Text       = $"✅  Sei aggiornato (v{CurrentVersion})";
                TxtUpdateStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61));
            }
        }
        catch
        {
            if (!silent)
            {
                TxtUpdateStatus.Text       = $"⚠️  Impossibile contattare il server (v{CurrentVersion} installata)";
                TxtUpdateStatus.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }
    }

    private async void BtnCheckUpdate_Click(object s, RoutedEventArgs e)
    {
        TxtUpdateStatus.Text       = "⏳  Controllo in corso...";
        TxtUpdateStatus.Foreground = System.Windows.Media.Brushes.Gray;

        if (string.IsNullOrEmpty(_config.NovaSCMApiUrl))
        {
            TxtUpdateStatus.Text       = "⚙️  Configura l'URL API NovaSCM nelle Impostazioni per ricevere aggiornamenti automatici";
            TxtUpdateStatus.Foreground = System.Windows.Media.Brushes.Gold;
            return;
        }

        await CheckForUpdatesAsync(silent: false);
    }

    // ── Auto-update: scarica e sostituisce l'exe ──────────────────────────────

    private async void BtnInstallUpdate_Click(object s, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_updateDownloadUrl)) return;

        BtnInstallUpdate.IsEnabled = false;
        BtnInstallUpdate.Content   = "⏳  Download...";

        try
        {
            var tmpExe    = Path.Combine(Path.GetTempPath(), "NovaSCM_update.exe");
            var currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;

            // Scarica nuovo exe
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            var bytes = await http.GetByteArrayAsync(_updateDownloadUrl);
            await File.WriteAllBytesAsync(tmpExe, bytes);

            // Script bat: aspetta uscita processo, copia con retry, rilancia
            var batPath = Path.Combine(Path.GetTempPath(), "novascm_updater.bat");
            var batContent = $"@echo off\r\n" +
                             $"ping 127.0.0.1 -n 4 > nul\r\n" +
                             $":retry\r\n" +
                             $"copy /Y \"{tmpExe}\" \"{currentExe}\" > nul 2>&1\r\n" +
                             $"if errorlevel 1 ( ping 127.0.0.1 -n 3 > nul & goto retry )\r\n" +
                             $"start \"\" \"{currentExe}\"\r\n" +
                             $"del \"%~f0\"\r\n";
            await File.WriteAllTextAsync(batPath, batContent, System.Text.Encoding.ASCII);

            Process.Start(new ProcessStartInfo("cmd.exe", $"/C \"{batPath}\"")
            {
                UseShellExecute = true,
                WindowStyle     = ProcessWindowStyle.Hidden
            });

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
        TxtDeployStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
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
        TxtDeployStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
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
            TxtDeployStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
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

    class CrRow
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

    private void InitCrTab()
    {
        LstCrPackages.ItemsSource = _crPackages;
    }

    private async void MainTabs_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (e.Source != MainTabs) return;
        UpdateNavState(MainTabs.SelectedIndex);
        if (MainTabs.SelectedItem is not TabItem tab) return;
        var header = tab.Header?.ToString() ?? "";
        if (header.Contains("Richieste"))
            await LoadCrListAsync();
        else if (header.Contains("Workflow"))
        {
            await LoadWorkflowsAsync();
            await LoadWorkflowAssignmentsAsync();
        }
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
        TxtCrStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
    }

    private async void BtnCrCreate_Click(object s, RoutedEventArgs e)
    {
        var pcName = TxtCrPcName.Text.Trim().ToUpper();
        var domain = TxtCrDomain.Text.Trim();
        if (string.IsNullOrEmpty(pcName) || string.IsNullOrEmpty(domain))
        {
            TxtCrStatus.Text       = "⚠️  Nome PC e Dominio sono obbligatori";
            TxtCrStatus.Foreground = System.Windows.Media.Brushes.Orange;
            return;
        }

        var body = System.Text.Json.JsonSerializer.Serialize(new
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

        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var resp = await http.PostAsync(CrApiBase,
                new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json"));
            var json = await resp.Content.ReadAsStringAsync();
            if (resp.IsSuccessStatusCode)
            {
                TxtCrStatus.Text       = $"✅  CR creato per {pcName}";
                TxtCrStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
                TxtCrPcName.Clear();
                await LoadCrListAsync();
            }
            else
            {
                var err = System.Text.Json.JsonDocument.Parse(json).RootElement
                          .TryGetProperty("error", out var ep) ? ep.GetString() : json;
                TxtCrStatus.Text       = $"❌  {err}";
                TxtCrStatus.Foreground = System.Windows.Media.Brushes.Tomato;
            }
        }
        catch (Exception ex)
        {
            TxtCrStatus.Text       = $"❌  Errore connessione: {ex.Message}";
            TxtCrStatus.Foreground = System.Windows.Media.Brushes.Tomato;
        }
    }

    private async void BtnCrRefresh_Click(object s, RoutedEventArgs e)
        => await LoadCrListAsync();

    private async Task LoadCrListAsync()
    {
        if (string.IsNullOrEmpty(CrApiBase))
        {
            TxtCrStatus.Text       = "⚙️  Configura l'URL API NovaSCM nelle Impostazioni";
            TxtCrStatus.Foreground = System.Windows.Media.Brushes.Gold;
            return;
        }
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var json = await http.GetStringAsync(CrApiBase);
            var doc  = System.Text.Json.JsonDocument.Parse(json);
            var rows = new List<CrRow>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                rows.Add(new CrRow
                {
                    Id           = el.TryGetProperty("id",            out var i)  ? i.GetInt32()              : 0,
                    PcName       = el.TryGetProperty("pc_name",       out var p)  ? p.GetString() ?? ""       : "",
                    Domain       = el.TryGetProperty("domain",        out var d)  ? d.GetString() ?? ""       : "",
                    Ou           = el.TryGetProperty("ou",            out var o)  ? o.GetString() ?? ""       : "",
                    AssignedUser = el.TryGetProperty("assigned_user", out var u)  ? u.GetString() ?? ""       : "",
                    Status       = el.TryGetProperty("status",        out var st) ? st.GetString() ?? ""      : "",
                    CreatedAt    = el.TryGetProperty("created_at",    out var ca) ? ca.GetString() ?? ""      : "",
                    Notes        = el.TryGetProperty("notes",         out var n)  ? n.GetString() ?? ""       : "",
                    LastSeen     = el.TryGetProperty("last_seen",     out var ls) ? ls.GetString() ?? ""      : "",
                });
            }
            CrGrid.ItemsSource = rows;
            TxtCrStatus.Text       = $"🔄  {rows.Count} richieste";
            TxtCrStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
        }
        catch (Exception ex)
        {
            TxtCrStatus.Text       = $"❌  {ex.Message}";
            TxtCrStatus.Foreground = System.Windows.Media.Brushes.Tomato;
        }
    }

    private void CrGrid_SelectionChanged(object s, SelectionChangedEventArgs e) { }

    private async void MenuCrDebug_Click(object s, RoutedEventArgs e)
    {
        var cr = GetSelectedCr();
        if (cr == null) return;
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var json = await http.GetStringAsync($"{CrApiBase}/{cr.Id}");
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
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var body = System.Text.Json.JsonSerializer.Serialize(new { status });
            await http.PutAsync($"{CrApiBase}/{cr.Id}/status",
                new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json"));
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
        var cr = GetSelectedCr();
        if (cr == null) return;
        if (MessageBox.Show($"Eliminare CR per '{cr.PcName}'?", "Conferma",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            await http.DeleteAsync($"{CrApiBase}/{cr.Id}");
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
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var xml = await http.GetStringAsync($"{CrApiBase}/by-name/{cr.PcName}/autounattend.xml");
            File.WriteAllText(dlg.FileName, xml, System.Text.Encoding.UTF8);
            TxtCrStatus.Text       = $"💾  Salvato: {dlg.FileName}";
            TxtCrStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
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
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var xml = await http.GetStringAsync($"{CrApiBase}/by-name/{cr.PcName}/autounattend.xml");
            File.WriteAllText(Path.Combine(folder, "autounattend.xml"), xml, System.Text.Encoding.UTF8);

            // 2. Genera postinstall.ps1 dalla CR
            var crJson = await http.GetStringAsync($"{CrApiBase}/{cr.Id}");
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
            TxtCrStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
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

        TxtDeployStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
        TxtDeployStatus.Text =
            $"✅  File generati — {cfg.WinEdition} · {cfg.WingetPackages.Count} software · " +
            $"{(cfg.IncludeAgent ? "agente incluso" : "senza agente")}\n" +
            "💡  Per USB: copia autounattend.xml + postinstall.ps1 nella radice della chiavetta insieme all'ISO Windows.";
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
        TxtDeployStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
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

        TxtDeployStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
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
                    var args = $"-i \"{keyPath}\" -o StrictHostKeyChecking=no " +
                               $"\"{src}\" root@{pxeIp}:{pxePath}{file}";
                    using var proc = Process.Start(new ProcessStartInfo("scp", args)
                    {
                        UseShellExecute        = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        CreateNoWindow         = true,
                    })!;
                    proc.WaitForExit(30_000);
                    if (proc.ExitCode != 0)
                        throw new Exception($"scp {file} fallito (exit {proc.ExitCode}): " +
                                            proc.StandardError.ReadToEnd());
                }
            });

            TxtDeployStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
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
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var json = await http.GetStringAsync($"{WfApiBase}/api/workflows");
            var doc  = System.Text.Json.JsonDocument.Parse(json);

            var prevId = _selectedWfId;
            _wfRows.Clear();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                _wfRows.Add(new WfRow
                {
                    Id          = el.TryGetProperty("id",          out var i)  ? i.GetInt32()        : 0,
                    Nome        = el.TryGetProperty("nome",        out var n)  ? n.GetString() ?? "" : "",
                    Descrizione = el.TryGetProperty("descrizione", out var d)  ? d.GetString() ?? "" : "",
                    Versione    = el.TryGetProperty("versione",    out var v)  ? v.GetInt32()        : 1,
                    StepCount   = el.TryGetProperty("steps",       out var st) ? st.GetArrayLength() : 0,
                });
            }

            // Riseleziona il workflow precedente se ancora presente
            if (prevId > 0)
            {
                var found = _wfRows.FirstOrDefault(w => w.Id == prevId);
                if (found != null) LstWorkflows.SelectedItem = found;
            }

            TxtWfStatus.Text = $"✅  {_wfRows.Count} workflow";
            TxtWfStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
        }
        catch (Exception ex)
        {
            TxtWfStatus.Text = $"❌  {ex.Message}";
            TxtWfStatus.Foreground = System.Windows.Media.Brushes.Tomato;
        }
    }

    private async Task LoadWorkflowStepsAsync(int wfId)
    {
        _wfStepRows.Clear();
        if (string.IsNullOrEmpty(WfApiBase) || wfId <= 0) return;
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var json = await http.GetStringAsync($"{WfApiBase}/api/workflows/{wfId}");
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
    }

    private async Task LoadWorkflowAssignmentsAsync()
    {
        if (string.IsNullOrEmpty(WfApiBase)) return;
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var json = await http.GetStringAsync($"{WfApiBase}/api/pc-workflows");
            var doc  = System.Text.Json.JsonDocument.Parse(json);
            _wfAssignRows.Clear();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var status   = el.TryGetProperty("status",       out var st) ? st.GetString() ?? "" : "";
                var progress = status == "completed" ? 100 : 0;
                _wfAssignRows.Add(new WfAssignRow
                {
                    Id           = el.TryGetProperty("id",            out var i)  ? i.GetInt32()         : 0,
                    PcName       = el.TryGetProperty("pc_name",       out var p)  ? p.GetString() ?? ""  : "",
                    WorkflowNome = el.TryGetProperty("workflow_nome", out var wn) ? wn.GetString() ?? "" : "",
                    WorkflowId   = el.TryGetProperty("workflow_id",   out var wi) ? wi.GetInt32()        : 0,
                    Status       = status,
                    Progress     = progress,
                    AssignedAt   = el.TryGetProperty("assigned_at",   out var a)  ? a.GetString() ?? ""  : "",
                    LastSeen     = el.TryGetProperty("last_seen",     out var ls) ? ls.GetString() ?? "" : "",
                });
            }
        }
        catch { }
    }

    private async void LstWorkflows_SelectionChanged(object s, SelectionChangedEventArgs e)
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

    private async void BtnWfNew_Click(object s, RoutedEventArgs e)
    {
        var win = new WfNameWindow("Nuovo Workflow", "", "");
        if (win.ShowDialog() != true || string.IsNullOrEmpty(win.WfNome)) return;
        if (string.IsNullOrEmpty(WfApiBase)) return;
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var body = System.Text.Json.JsonSerializer.Serialize(new { nome = win.WfNome, descrizione = win.WfDesc });
            var resp = await http.PostAsync($"{WfApiBase}/api/workflows",
                new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json"));
            if (resp.IsSuccessStatusCode)
            {
                await LoadWorkflowsAsync();
                TxtWfStatus.Text = $"✅  Workflow '{win.WfNome}' creato";
                TxtWfStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
            }
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
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var body = System.Text.Json.JsonSerializer.Serialize(new { nome = win.WfNome, descrizione = win.WfDesc });
            var resp = await http.PutAsync($"{WfApiBase}/api/workflows/{wf.Id}",
                new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json"));
            if (resp.IsSuccessStatusCode) await LoadWorkflowsAsync();
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
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            await http.DeleteAsync($"{WfApiBase}/api/workflows/{wf.Id}");
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
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var body = System.Text.Json.JsonSerializer.Serialize(new
            {
                ordine    = win.Result.Ordine,
                nome      = win.Result.Nome,
                tipo      = win.Result.Tipo,
                parametri = win.Result.Parametri,
                platform  = win.Result.Platform,
                su_errore = win.Result.SuErrore,
            });
            var resp = await http.PostAsync($"{WfApiBase}/api/workflows/{wf.Id}/steps",
                new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json"));
            if (resp.IsSuccessStatusCode)
            {
                await LoadWorkflowStepsAsync(wf.Id);
                // Aggiorna il conteggio step nella lista
                wf.StepCount = _wfStepRows.Count;
            }
            else
            {
                var err = await resp.Content.ReadAsStringAsync();
                MessageBox.Show($"Errore: {err}", "NovaSCM", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "NovaSCM", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnWfEditStep_Click(object s, RoutedEventArgs e)
    {
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
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var body = System.Text.Json.JsonSerializer.Serialize(new
            {
                ordine    = win.Result.Ordine,
                nome      = win.Result.Nome,
                tipo      = win.Result.Tipo,
                parametri = win.Result.Parametri,
                platform  = win.Result.Platform,
                su_errore = win.Result.SuErrore,
            });
            var resp = await http.PutAsync($"{WfApiBase}/api/workflows/{wf.Id}/steps/{step.Id}",
                new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json"));
            if (resp.IsSuccessStatusCode)
                await LoadWorkflowStepsAsync(wf.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "NovaSCM", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnWfStepUp_Click(object s, RoutedEventArgs e)
    {
        if (GridWfSteps.SelectedItem is not WfStepRow step) return;
        if (LstWorkflows.SelectedItem is not WfRow wf) return;
        var prev = _wfStepRows.Where(st => st.Ordine < step.Ordine).OrderByDescending(st => st.Ordine).FirstOrDefault();
        if (prev == null) return;
        await SwapStepOrdineAsync(wf.Id, step, prev);
    }

    private async void BtnWfStepDown_Click(object s, RoutedEventArgs e)
    {
        if (GridWfSteps.SelectedItem is not WfStepRow step) return;
        if (LstWorkflows.SelectedItem is not WfRow wf) return;
        var next = _wfStepRows.Where(st => st.Ordine > step.Ordine).OrderBy(st => st.Ordine).FirstOrDefault();
        if (next == null) return;
        await SwapStepOrdineAsync(wf.Id, step, next);
    }

    private async Task SwapStepOrdineAsync(int wfId, WfStepRow a, WfStepRow b)
    {
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // Usa ordine temporaneo 9999 per evitare il constraint UNIQUE(workflow_id, ordine)
        string Put(WfStepRow st, int ord) => System.Text.Json.JsonSerializer.Serialize(new
            { ordine = ord, nome = st.Nome, tipo = st.Tipo, parametri = st.Parametri, platform = st.Platform, su_errore = st.SuErrore });
        var sc = new System.Net.Http.StringContent("", System.Text.Encoding.UTF8, "application/json");
        sc = new(Put(a, 9999), System.Text.Encoding.UTF8, "application/json");
        await http.PutAsync($"{WfApiBase}/api/workflows/{wfId}/steps/{a.Id}", sc);
        sc = new(Put(b, a.Ordine), System.Text.Encoding.UTF8, "application/json");
        await http.PutAsync($"{WfApiBase}/api/workflows/{wfId}/steps/{b.Id}", sc);
        sc = new(Put(a, b.Ordine), System.Text.Encoding.UTF8, "application/json");
        await http.PutAsync($"{WfApiBase}/api/workflows/{wfId}/steps/{a.Id}", sc);
        await LoadWorkflowStepsAsync(wfId);
    }

    private async void BtnWfDeleteStep_Click(object s, RoutedEventArgs e)
    {
        if (GridWfSteps.SelectedItem is not WfStepRow step)
        {
            MessageBox.Show("Seleziona uno step.", "NovaSCM", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (LstWorkflows.SelectedItem is not WfRow wf) return;
        if (MessageBox.Show($"Eliminare lo step \"{step.Nome}\"?", "Conferma",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        await http.DeleteAsync($"{WfApiBase}/api/workflows/{wf.Id}/steps/{step.Id}");
        await LoadWorkflowStepsAsync(wf.Id);
        wf.StepCount = _wfStepRows.Count;
    }

    private async void BtnWfAssign_Click(object s, RoutedEventArgs e)
    {
        var win = new WfAssignWindow(_wfRows.ToList());
        if (win.ShowDialog() != true || string.IsNullOrEmpty(win.PcName) || win.WorkflowId <= 0) return;
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var body = System.Text.Json.JsonSerializer.Serialize(new
                { pc_name = win.PcName.ToUpperInvariant(), workflow_id = win.WorkflowId });
            var resp = await http.PostAsync($"{WfApiBase}/api/pc-workflows",
                new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json"));
            if (resp.IsSuccessStatusCode)
                await LoadWorkflowAssignmentsAsync();
            else
                MessageBox.Show("Errore: workflow già assegnato a questo PC?", "NovaSCM",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
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
        if (GridWfAssignments.SelectedItem is not WfAssignRow assign)
        {
            MessageBox.Show("Seleziona un'assegnazione.", "NovaSCM", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"Eliminare l'assegnazione {assign.PcName} → {assign.WorkflowNome}?",
            "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        await http.DeleteAsync($"{WfApiBase}/api/pc-workflows/{assign.Id}");
        await LoadWorkflowAssignmentsAsync();
    }

    private async void BtnWfRefreshAssign_Click(object s, RoutedEventArgs e)
        => await LoadWorkflowAssignmentsAsync();
}
