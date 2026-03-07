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
                            Dispatcher.Invoke(() => { _netRows.Add(row); found++; AddRadarBlip(row); TxtRadarStatus.Text = $"● SCANNING — {found} FOUND"; });
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
        StopRadar(found);

        // Salva risultati scansione nel DB
        foreach (var d in _netRows)
            Database.UpsertDevice(d);

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
                    var reply = await ping.SendPingAsync(ip, 1000);
                    ms = reply.Status == System.Net.NetworkInformation.IPStatus.Success
                        ? reply.RoundtripTime : -1;
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
        TxtPingLast.Text = latestMs >= 0 ? $"{latestMs} ms" : "timeout";
        TxtPingLast.Foreground = new System.Windows.Media.SolidColorBrush(lineColor);
        if (valid.Count > 0)
        {
            TxtPingAvg.Text = $"avg: {valid.Average():0} ms";
            TxtPingMin.Text = $"min: {valid.Min():0} ms";
            TxtPingMax.Text = $"max: {valid.Max():0} ms";
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

        // Pulse ring
        var pulse = new System.Windows.Shapes.Ellipse
        {
            Width = 18, Height = 18,
            Stroke = new System.Windows.Media.SolidColorBrush(col),
            StrokeThickness = 1.5, Opacity = 0.8,
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            RenderTransform = new System.Windows.Media.ScaleTransform(1, 1)
        };
        Canvas.SetLeft(pulse, bx - 9); Canvas.SetTop(pulse, by - 9);
        var sx = new System.Windows.Media.Animation.DoubleAnimation(1, 2.8,
            new Duration(TimeSpan.FromSeconds(1.8))) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
        ((System.Windows.Media.ScaleTransform)pulse.RenderTransform).BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, sx);
        ((System.Windows.Media.ScaleTransform)pulse.RenderTransform).BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
            new System.Windows.Media.Animation.DoubleAnimation(1, 2.8, new Duration(TimeSpan.FromSeconds(1.8))) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
        pulse.BeginAnimation(UIElement.OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0.8, 0,
                new Duration(TimeSpan.FromSeconds(1.8))) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
        _radarBlipLayer.Children.Add(pulse);

        // Core dot
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 8, Height = 8, Opacity = 0,
            Fill = new System.Windows.Media.SolidColorBrush(col),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = col, BlurRadius = 10, ShadowDepth = 0
            }
        };
        Canvas.SetLeft(dot, bx - 4); Canvas.SetTop(dot, by - 4);
        dot.BeginAnimation(UIElement.OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 1,
                new Duration(TimeSpan.FromSeconds(0.4))));
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
                new Duration(TimeSpan.FromSeconds(0.7))));
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
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
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

    private async void BtnSccmRefresh_Click(object s, RoutedEventArgs e) =>
        await LoadSccmSection(_sccmCurrentSection);

    private async void SccmNavTree_SelectedItemChanged(object s, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem item && item.Tag is string tag)
            await LoadSccmSection(tag);
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
        "Rete", "Certificati", "Applicazioni", "OPSI",
        "PC", "Deploy OS", "Workflow", "Richieste",
        "SCCM", "Impostazioni", "About"
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
        MainTabs.SelectedIndex = 9;
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
        else
        {
            StopMatrixRain();
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
        => await LoadCrListAsync();

    private async Task LoadCrListAsync()
    {
        // Prova prima il server API; se non disponibile usa il DB locale
        if (!string.IsNullOrEmpty(CrApiBase))
        {
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var json = await http.GetStringAsync(CrApiBase);
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

        TxtDeployStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61));
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
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var json = await http.GetStringAsync($"{WfApiBase}/api/workflows");
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
                TxtWfStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61));
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

    private void StartGauges()
    {
        try { _cpuCounter = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total"); }
        catch { _cpuCounter = null; }

        _gaugeTimer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromSeconds(1.5) };
        _gaugeTimer.Tick += (_, _) => UpdateGauges();
        _gaugeTimer.Start();
        UpdateGauges();
    }

    private void StopGauges()
    {
        _gaugeTimer?.Stop();
        _gaugeTimer = null;
    }

    private void UpdateGauges()
    {
        float cpu = 0;
        try { _cpuCounter?.NextValue(); cpu = _cpuCounter?.NextValue() ?? 0; }
        catch { }

        var mem   = GC.GetGCMemoryInfo();
        float ram = mem.TotalAvailableMemoryBytes > 0
            ? (float)(1.0 - (double)mem.MemoryLoadBytes / mem.TotalAvailableMemoryBytes) * 100
            : 0;
        // Simpler: use Environment working set as rough proxy
        long usedBytes  = Environment.WorkingSet;
        float ramUsed   = (float)(usedBytes / (1024.0 * 1024.0));

        // Disk
        float disk = 0;
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            disk = (float)(1.0 - (double)drive.AvailableFreeSpace / drive.TotalSize) * 100;
        }
        catch { }

        // Network (send rough counter)
        float net = 0;
        try
        {
            var netIfs = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            long totalBytes = netIfs.Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                                    .Sum(n => n.GetIPStatistics().BytesSent + n.GetIPStatistics().BytesReceived);
            net = Math.Min(100, (float)(totalBytes / 1e9 % 100));
        }
        catch { }

        DrawArcGauge(GaugeCpu,  cpu,  100, "CPU",  "#3b82f6", $"{cpu:F0}%");
        DrawArcGauge(GaugeRam,  Math.Min(100, ramUsed / 40 * 100), 100, "RAM",  "#10b981", $"{ramUsed:F0} MB");
        DrawArcGauge(GaugeDisk, disk, 100, "Disco","#f59e0b", $"{disk:F0}%");
        DrawArcGauge(GaugeNet,  net,  100, "NET",  "#a78bfa", "● " + (net > 10 ? "Attivo" : "Idle"));
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
            "Cos'è NovaSCM|NovaSCM è uno strumento open source per la gestione di reti e fleet di PC. Combina scansione di rete, gestione certificati WiFi EAP-TLS, deploy Windows automatizzato, OPSI, SCCM e workflow di automazione in un'unica interfaccia.",
            "Requisiti|• Windows 10/11 (64-bit)\n• .NET 9.0 Runtime\n• Accesso alla rete da gestire\n• (Opzionale) Server NovaSCM per workflow e change requests",
            "Architettura|NovaSCM funziona in modalità offline-first: tutti i dati vengono salvati in un database SQLite locale (%APPDATA%\\PolarisManager\\novascm.db) e sincronizzati con il server quando disponibile.",
            "Primo avvio|Al primo avvio, vai nel tab ⚙️ Impostazioni e configura almeno l'URL del server NovaSCM (se disponibile). Puoi usare l'app anche senza server — le funzionalità offline restano disponibili."
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
            "La scansione è lenta|Aumenta la parallelizzazione in ⚙️ Impostazioni. Su subnet /24 sono normali 15-30 secondi. Reti con firewall aggressivo possono richiedere più tempo.",
            "Come aggiorno NovaSCM?|Clicca 🔄 Controlla aggiornamenti nel tab ℹ️ About, oppure scarica l'ultima versione da GitHub.",
            "Il Matrix Rain si attiva?|Sì! Apri il tab ℹ️ About per vederlo. Esiste anche un Easter Egg nascosto... cerca il codice Konami. 😏"
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
            Process.Start(new ProcessStartInfo("wt.exe", $"ssh {target}") { UseShellExecute = true });
        }
        else
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/k ssh {target}")
            { UseShellExecute = true, CreateNoWindow = false });
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

        await Task.Run(() =>
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

                Dispatcher.Invoke(() =>
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
        if (_speedTestRunning) return;
        // Mostra pannello speed test
        HideAllNetPanels();
        SpeedTestPanel.Visibility = Visibility.Visible;
        NetGrid.Visibility        = Visibility.Collapsed;
        await RunSpeedTestAsync();
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
}
