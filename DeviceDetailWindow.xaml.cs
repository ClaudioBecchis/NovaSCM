// NovaSCM v1.4.0
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;

namespace PolarisManager;

// ── Modello porta aperta ──────────────────────────────────────────────────────
class OpenPort
{
    public int    Port           { get; set; }
    public string PortDisplay    => $"🟢 {Port}";
    public string ServiceName    { get; set; } = "";
    public string ActionLabel    { get; set; } = "";
    public string ActionType     { get; set; } = ""; // "rdp" | "ssh" | "web" | ""
    public Visibility ActionVisibility =>
        string.IsNullOrEmpty(ActionType) ? Visibility.Collapsed : Visibility.Visible;
    public Style? ActionStyle    { get; set; }
}

public partial class DeviceDetailWindow : Window
{
    public event Action<IEnumerable<int>>? PortsScanned;
    private readonly DeviceRow _device;
    private readonly string    _certportalUrl;
    private CancellationTokenSource? _cts;
    private readonly ObservableCollection<OpenPort> _ports = [];

    // Stili caricati sul thread UI nel costruttore
    private Style? _styleRdp, _styleSsh, _styleWeb;

    // Porte comuni da scansionare con nome servizio
    private static readonly (int Port, string Name)[] CommonPorts =
    [
        (21,   "FTP"),
        (22,   "SSH"),
        (23,   "Telnet"),
        (25,   "SMTP"),
        (53,   "DNS"),
        (80,   "HTTP"),
        (110,  "POP3"),
        (135,  "RPC"),
        (139,  "NetBIOS"),
        (143,  "IMAP"),
        (443,  "HTTPS"),
        (445,  "SMB"),
        (1883, "MQTT"),
        (3306, "MySQL"),
        (3389, "RDP"),
        (5000, "Flask/Dev"),
        (5380, "Technitium"),
        (5432, "PostgreSQL"),
        (8000, "HTTP Alt"),
        (8006, "Proxmox"),
        (8080, "HTTP Alt"),
        (8096, "Jellyfin"),
        (8123, "Home Assistant"),
        (8443, "HTTPS Alt"),
        (9000, "Portainer"),
        (9090, "Certportal"),
        (9100, "Node Exporter"),
        (9443, "Portainer HTTPS"),
    ];

    public DeviceDetailWindow(DeviceRow device, string certportalUrl)
    {
        InitializeComponent();
        _device        = device;
        _certportalUrl = certportalUrl;

        // Header
        TxtDeviceName.Text = device.Name != "—" ? $"🖥️  {device.Name}" : $"🖥️  {device.Ip}";
        TxtIp.Text         = device.Ip;
        TxtMac.Text        = device.Mac;
        TxtVendor.Text     = device.Vendor != "—" ? device.Vendor : "—";
        TxtStatus.Text     = device.Status.Contains("Online") ? "🟢" : "🔴";

        // Carica stili sul thread UI
        _styleRdp = (Style)FindResource("BtnRdp");
        _styleSsh  = (Style)FindResource("BtnSsh");
        _styleWeb  = (Style)FindResource("BtnWeb");

        PortList.ItemsSource = _ports;
        PortProgress.Maximum = CommonPorts.Length;

        Loaded += async (_, _) => await ScanPortsAsync();
        Closed += (_, _) => _cts?.Cancel();
    }

    private async Task ScanPortsAsync()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        int done = 0, found = 0;

        TxtPortStatus.Text = $"0/{CommonPorts.Length}";
        TxtNoPorte.Visibility = Visibility.Visible;

        var semaphore = new SemaphoreSlim(30);

        var tasks = CommonPorts.Select(async entry =>
        {
            if (token.IsCancellationRequested) return;
            await semaphore.WaitAsync(token).ConfigureAwait(false);
            try
            {
                bool open = await IsPortOpenAsync(_device.Ip, entry.Port, 600).ConfigureAwait(false);
                Interlocked.Increment(ref done);

                if (open)
                {
                    Interlocked.Increment(ref found);
                    var port = BuildOpenPort(entry.Port, entry.Name);
                    Dispatcher.Invoke(() =>
                    {
                        _ports.Add(port);
                        // Ordina per numero porta
                        var sorted = _ports.OrderBy(p => p.Port).ToList();
                        _ports.Clear();
                        foreach (var p in sorted) _ports.Add(p);
                        TxtNoPorte.Visibility = Visibility.Collapsed;
                    });
                    App.Log($"  Porta aperta: {_device.Ip}:{entry.Port} ({entry.Name})");
                }

                var d2 = done; var f2 = found;
                Dispatcher.Invoke(() =>
                {
                    PortProgress.Value = d2;
                    TxtPortStatus.Text = $"{d2}/{CommonPorts.Length} — {f2} aperte";
                });
            }
            catch (OperationCanceledException) { }
            catch { Interlocked.Increment(ref done); }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks);

        if (!token.IsCancellationRequested)
        {
            var openList = _ports.Select(p => p.Port).ToList();
            PortsScanned?.Invoke(openList);
            Dispatcher.Invoke(() =>
            {
                TxtPortStatus.Text = $"Completata — {found} porte aperte";
                TxtSummary.Text    = found == 0
                    ? "Nessuna porta aperta rilevata."
                    : $"{found} porte aperte su {CommonPorts.Length} scansionate.";
                if (found == 0) TxtNoPorte.Text = "Nessuna porta aperta trovata.";
            });
        }
    }

    private static async Task<bool> IsPortOpenAsync(string ip, int port, int timeoutMs)
    {
        try
        {
            using var tcp = new TcpClient();
            var cts = new CancellationTokenSource(timeoutMs);
            await tcp.ConnectAsync(ip, port, cts.Token);
            return true;
        }
        catch { return false; }
    }

    private OpenPort BuildOpenPort(int port, string name)
    {
        var p = new OpenPort { Port = port, ServiceName = name };

        switch (port)
        {
            case 3389:
                p.ActionLabel = "🖥️  Connetti RDP";
                p.ActionType  = "rdp";
                p.ActionStyle = _styleRdp;
                break;
            case 22:
                p.ActionLabel = "🔒  Connetti SSH";
                p.ActionType  = "ssh";
                p.ActionStyle = _styleSsh;
                break;
            case 80:
                p.ActionLabel = "🌐  Apri HTTP";
                p.ActionType  = "web";
                p.ActionStyle = _styleWeb;
                p.ServiceName  = $"HTTP  →  http://{_device.Ip}";
                break;
            case 443:
                p.ActionLabel = "🌐  Apri HTTPS";
                p.ActionType  = "web";
                p.ActionStyle = _styleWeb;
                p.ServiceName  = $"HTTPS  →  https://{_device.Ip}";
                break;
            case 8006:
                p.ActionLabel = "🌐  Apri Proxmox";
                p.ActionType  = "web";
                p.ActionStyle = _styleWeb;
                p.ServiceName  = $"Proxmox  →  https://{_device.Ip}:8006";
                break;
            case 8123:
                p.ActionLabel = "🌐  Apri HA";
                p.ActionType  = "web";
                p.ActionStyle = _styleWeb;
                p.ServiceName  = $"Home Assistant  →  http://{_device.Ip}:8123";
                break;
            case 9090:
                p.ActionLabel = "🌐  Apri";
                p.ActionType  = "web";
                p.ActionStyle = _styleWeb;
                p.ServiceName  = $"Certportal  →  http://{_device.Ip}:9090";
                break;
            case 8096:
                p.ActionLabel = "🌐  Apri Jellyfin";
                p.ActionType  = "web";
                p.ActionStyle = _styleWeb;
                p.ServiceName  = $"Jellyfin  →  http://{_device.Ip}:8096";
                break;
            case 9000:
                p.ActionLabel = "🌐  Apri Portainer";
                p.ActionType  = "web";
                p.ActionStyle = _styleWeb;
                p.ServiceName  = $"Portainer  →  http://{_device.Ip}:9000";
                break;
        }

        return p;
    }

    private void PortAction_Click(object s, RoutedEventArgs e)
    {
        if (s is not Button btn || btn.Tag is not OpenPort port) return;

        switch (port.ActionType)
        {
            case "rdp":
                Process.Start("mstsc", $"/v:{_device.Ip}");
                App.Log($"RDP → {_device.Ip}");
                break;

            case "ssh":
                // Prova Windows Terminal, poi fallback a cmd
                var sshCmd = $"ssh {_device.Ip}";
                try
                {
                    Process.Start(new ProcessStartInfo("wt.exe", $"-- ssh {_device.Ip}")
                        { UseShellExecute = true });
                }
                catch
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo("cmd.exe", $"/k ssh {_device.Ip}")
                            { UseShellExecute = true });
                    }
                    catch (Exception ex) { MessageBox.Show($"Errore SSH: {ex.Message}"); }
                }
                App.Log($"SSH → {_device.Ip}");
                break;

            case "web":
                var url = port.Port switch
                {
                    443  => $"https://{_device.Ip}",
                    8006 => $"https://{_device.Ip}:8006",
                    _    => $"http://{_device.Ip}:{port.Port}",
                };
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                App.Log($"Browser → {url}");
                break;
        }
    }

    private void BtnGenCert_Click(object s, RoutedEventArgs e)
    {
        if (_device.Mac == "—")
        {
            MessageBox.Show("MAC non ancora risolto. Attendi e riprova.",
                "MAC mancante", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        MessageBox.Show($"Generazione certificato per {_device.Mac}...\n(Demo — collega il certportal nelle Impostazioni)",
            "Genera Cert", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnRegister_Click(object s, RoutedEventArgs e)
    {
        if (_device.Mac == "—")
        {
            MessageBox.Show("MAC non ancora risolto. Attendi e riprova.",
                "MAC mancante", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        MessageBox.Show($"Registrazione {_device.Mac} ({_device.Ip})...\n(Demo — collega il certportal nelle Impostazioni)",
            "Registra", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnClose_Click(object s, RoutedEventArgs e) => Close();
}
