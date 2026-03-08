// NovaSCM v1.6.0
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PolarisManager;

// ── TsStep (INotifyPropertyChanged per aggiornamenti live) ────────────────────

class TsStep : INotifyPropertyChanged
{
    private string _status = "pending";  // pending | running | done | error

    public string StepKey { get; set; } = "";   // chiave DB (es. "install_Mozilla.Firefox")
    public string Pass    { get; set; } = "";
    public string Icon    { get; set; } = "";
    public string Label   { get; set; } = "";
    public string Detail  { get; set; } = "";

    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPC();
            OnPC(nameof(Pointer));
            OnPC(nameof(Check));
            OnPC(nameof(RowColor));
        }
    }

    public string Pointer  => _status == "running" ? "=>>" : "";
    public string Check    => _status == "done"  ? "✓" : _status == "error" ? "✗" : "";
    public string RowColor => _status switch
    {
        "running" => "#fbbf24",
        "done"    => "#10b981",
        "error"   => "#f87171",
        "pending" => "#64748b",
        _         => "#ccd6f6"
    };

    public Visibility DetailVisibility =>
        string.IsNullOrEmpty(Detail) ? Visibility.Collapsed : Visibility.Visible;

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPC([System.Runtime.CompilerServices.CallerMemberName] string? p = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

// ── TsVariable ─────────────────────────────────────────────────────────────────

class TsVariable
{
    public string Name  { get; set; } = "";
    public string Value { get; set; } = "";
}

// ── CrDebugWindow ──────────────────────────────────────────────────────────────

public partial class CrDebugWindow : Window
{
    private readonly string  _crApiBase;
    private readonly int     _crId;
    private readonly string  _pcName;

    private readonly ObservableCollection<TsStep>     _steps = [];
    private readonly ObservableCollection<TsVariable> _vars  = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };

    public CrDebugWindow(JsonElement crData, string crApiBase)
    {
        InitializeComponent();
        _crApiBase = crApiBase;
        _crId      = crData.TryGetProperty("id",      out var i) ? i.GetInt32()    : 0;
        _pcName    = crData.TryGetProperty("pc_name", out var p) ? p.GetString() ?? "" : "";

        LstSteps.ItemsSource  = _steps;
        GridVars.ItemsSource  = _vars;

        BuildStepList(crData);
        BuildVars(crData);
        UpdateHeader(crData);

        _timer.Tick += async (_, _) => await RefreshStepsAsync();
        _timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }

    // ── Costruisce la lista step dalla definizione del CR ──────────────────────

    private void BuildStepList(JsonElement d)
    {
        _steps.Clear();

        var domain   = d.TryGetProperty("domain",  out var dv) ? dv.GetString() ?? "" : "";
        var dcIp     = d.TryGetProperty("dc_ip",   out var di) ? di.GetString() ?? "" : "";
        var joinUser = d.TryGetProperty("join_user",out var ju) ? ju.GetString() ?? "" : "";
        var pcName   = d.TryGetProperty("pc_name", out var pn) ? pn.GetString() ?? "" : "";

        var software = new List<string>();
        if (d.TryGetProperty("software", out var sw) && sw.ValueKind == JsonValueKind.Array)
            foreach (var s in sw.EnumerateArray())
                if (s.GetString() is string pkg) software.Add(pkg);

        void Add(string key, string pass, string icon, string label, string detail = "") =>
            _steps.Add(new TsStep { StepKey = key, Pass = pass, Icon = icon,
                                    Label = label, Detail = detail, Status = "pending" });

        // ── Pass windowsPE (eseguiti da autounattend, non tracciabili via PS1) ──
        Add("wpe_disk",  "windowsPE", "💿", "Disk Configuration",   "EFI + MSR + Windows (UEFI GPT)");
        Add("wpe_image", "windowsPE", "🪟", "Apply OS Image",        "Windows 11 Pro");

        // ── Pass specialize ────────────────────────────────────────────────────
        Add("spec_name", "specialize", "🖥️", "Computer Name",        pcName);
        Add("spec_tz",   "specialize", "🕒", "Time Zone",            "W. Europe Standard Time");
        if (!string.IsNullOrEmpty(dcIp))
            Add("spec_dns", "specialize", "📡", "Set-DnsClientServerAddress", dcIp);
        if (!string.IsNullOrEmpty(domain))
            Add("spec_join", "specialize", "🔐", "Add-Computer (domain join)",
                $"{domain} / {joinUser}");

        // ── Pass oobeSystem ────────────────────────────────────────────────────
        Add("oobe_skip",     "oobeSystem", "🔕", "Skip OOBE / EULA");
        Add("oobe_autologon","oobeSystem", "🔑", "AutoLogon",        "Administrator (1 avvio)");

        // ── Post-install (tracciati da Report-Step) ────────────────────────────
        Add("postinstall_start", "FirstLogon", "📜", "Run postinstall.ps1");
        Add("rename_pc",         "postinstall", "✏️", "Rename-Computer",
            pcName.Contains("*") || pcName.Contains("{") ? "template {MAC6}" : pcName);

        if (software.Count == 0)
            Add("no_software", "postinstall", "📦", "Nessun software configurato");
        else
            foreach (var pkg in software)
                Add($"install_{pkg}", "postinstall", "📦", $"winget install {pkg}");

        Add("agent_install", "postinstall", "🔒", "Installa agente NovaSCM");
        Add("checkin",       "postinstall", "📡", "Check-in NovaSCM");
        Add("reboot",        "postinstall", "🔄", "Riavvio finale");
    }

    // ── Aggiorna stati step dal server ─────────────────────────────────────────

    private async Task RefreshStepsAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

            // Recupera step dal DB
            var stepsJson = await http.GetStringAsync($"{_crApiBase}/{_crId}/steps");
            var stepsDoc  = JsonDocument.Parse(stepsJson);

            // Mappa step_name → status
            var dbSteps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in stepsDoc.RootElement.EnumerateArray())
            {
                var key = el.TryGetProperty("step_name", out var k) ? k.GetString() ?? "" : "";
                var sta = el.TryGetProperty("status",    out var s) ? s.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(key)) dbSteps[key] = sta;
            }

            // Aggiorna TsStep (UI thread già garantito da DispatcherTimer)
            bool foundRunning = false;
            foreach (var step in _steps)
            {
                if (dbSteps.TryGetValue(step.StepKey, out var sta))
                {
                    step.Status = sta;
                    if (sta == "running") foundRunning = true;
                }
                else if (!foundRunning)
                {
                    // Tutti gli step windowsPE/specialize/oobeSystem si considerano
                    // completati se postinstall_start è in DB
                    bool preStepDone = dbSteps.ContainsKey("postinstall_start");
                    if (preStepDone && step.StepKey.StartsWith("wpe_") ||
                        preStepDone && step.StepKey.StartsWith("spec_") ||
                        preStepDone && step.StepKey.StartsWith("oobe_"))
                        step.Status = "done";
                }
            }

            // Recupera metadata CR aggiornato
            var crJson = await http.GetStringAsync($"{_crApiBase}/{_crId}");
            UpdateHeader(JsonDocument.Parse(crJson).RootElement);
        }
        catch { /* Ignora errori di rete — riprova al prossimo tick */ }
    }

    // ── Header CR ─────────────────────────────────────────────────────────────

    private void UpdateHeader(JsonElement d)
    {
        string Get(string k) => d.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";

        var status   = Get("status");
        var lastSeen = Get("last_seen");
        var domain   = Get("domain");

        TxtCrTitle.Text  = $"CR #{_crId} — {_pcName}";
        TxtCrDomain.Text = string.IsNullOrEmpty(domain) ? "(workgroup)" : domain;
        TxtCrStatus.Text = status;
        TxtCrStatus.Foreground = status switch
        {
            "completed"   => System.Windows.Media.Brushes.LightGreen,
            "in_progress" => System.Windows.Media.Brushes.Gold,
            _             => System.Windows.Media.Brushes.CornflowerBlue,
        };
        TxtLastSeen.Text = string.IsNullOrEmpty(lastSeen)
            ? "— mai —" : lastSeen.Replace("T", " ")[..Math.Min(19, lastSeen.Length)];
    }

    // ── Variabili (pannello inferiore) ─────────────────────────────────────────

    private void BuildVars(JsonElement d)
    {
        _vars.Clear();
        string Get(string k) => d.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";

        var software = new List<string>();
        if (d.TryGetProperty("software", out var sw) && sw.ValueKind == JsonValueKind.Array)
            foreach (var s in sw.EnumerateArray())
                if (s.GetString() is string pkg) software.Add(pkg);

        void V(string n, string v) => _vars.Add(new TsVariable { Name = n, Value = v });

        V("OSDComputerName",          Get("pc_name"));
        V("OSDDomainName",            Get("domain"));
        V("OSDOU",                    Get("ou"));
        V("OSDDCAddress",             Get("dc_ip"));
        V("OSDJoinAccount",           Get("join_user"));
        V("OSDAssignedTo",            Get("assigned_user"));
        V("TSDebugMode",              "true");
        V("SMSTSAssignedSiteCode",    "NVS");
        V("CRStatus",                 Get("status"));
        V("CRCreatedAt",              Get("created_at"));
        V("CRLastCheckin",            Get("last_seen").Replace("T", " "));
        V("CRNotes",                  Get("notes"));
        V("OSDSoftwareCount",         software.Count.ToString());
        for (int si = 0; si < software.Count; si++)
            V($"OSDSoftware[{si}]", software[si]);
    }

    // ── Handler toolbar ────────────────────────────────────────────────────────

    private async void BtnRefresh_Click(object s, RoutedEventArgs e)
        => await RefreshStepsAsync();

    private async void BtnDownloadXml_Click(object s, RoutedEventArgs e)
    {
        try
        {
            var dlg = new System.Windows.Forms.SaveFileDialog
            {
                FileName = $"autounattend_{_pcName}.xml",
                Filter   = "XML files|*.xml",
                Title    = "Salva autounattend.xml"
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var xml = await http.GetStringAsync($"{_crApiBase}/by-name/{_pcName}/autounattend.xml");
            File.WriteAllText(dlg.FileName, xml, System.Text.Encoding.UTF8);
            MessageBox.Show($"Salvato: {dlg.FileName}", "NovaSCM");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "NovaSCM",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void BtnCheckinNow_Click(object s, RoutedEventArgs e)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var body = JsonSerializer.Serialize(new { hostname = _pcName, @event = "manual_debug" });
            await http.PostAsync($"{_crApiBase}/by-name/{_pcName}/checkin",
                new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
            await RefreshStepsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore check-in: {ex.Message}", "NovaSCM",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnClose_Click(object s, RoutedEventArgs e) => Close();

    private void LstSteps_SelectionChanged(object s, SelectionChangedEventArgs e) { }
}
