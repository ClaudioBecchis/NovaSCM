// NovaSCM v1.8.2
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PolarisManager;

// ── Log entry ──────────────────────────────────────────────────────────────
public class LogEntry
{
    public string Text     { get; set; } = "";
    public System.Windows.Media.Brush ForeColor { get; set; } =
        new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 130, 160, 210));
}

// ── Modello step ────────────────────────────────────────────────────────────
public class OsdStep : INotifyPropertyChanged
{
    private string _status = "pending";
    private double _elapsedSec = 0;
    private double _estSec = 0;
    private bool   _isLast = false;

    public string StepKey { get; set; } = "";
    public string Label   { get; set; } = "";
    public string Tipo    { get; set; } = "";

    public bool IsLast
    {
        get => _isLast;
        set { _isLast = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConnectorVisible)); }
    }

    public double EstSec
    {
        get => _estSec;
        set { _estSec = value; OnPropertyChanged(); OnPropertyChanged(nameof(EstStr)); OnPropertyChanged(nameof(EstVisible)); }
    }

    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CircleIcon));
            OnPropertyChanged(nameof(CircleIconColor));
            OnPropertyChanged(nameof(TextColor));
            OnPropertyChanged(nameof(Weight));
            OnPropertyChanged(nameof(TipoColor));
            OnPropertyChanged(nameof(TimeColor));
            OnPropertyChanged(nameof(EstStr));
            OnPropertyChanged(nameof(EstVisible));
        }
    }

    public double ElapsedSec
    {
        get => _elapsedSec;
        set { _elapsedSec = value; OnPropertyChanged(); OnPropertyChanged(nameof(TimeStr)); }
    }

    // ── Computed ────────────────────────────────────────────────────────────
    public string CircleIcon => Status switch
    {
        "done"    => "✓",
        "running" => "▶",
        "error"   => "✕",
        "skip"    => "—",
        _         => ""
    };

    public string CircleIconColor => Status switch
    {
        "done"    => "#00D97E",
        "running" => "#4D9FFF",
        "error"   => "#FF4757",
        _         => "#3C5078"
    };

    public string TextColor => Status switch
    {
        "done"    => "#506080",
        "running" => "#E8F0FF",
        "error"   => "#FF4757",
        "skip"    => "#3C5078",
        _         => "#829696"
    };

    public string Weight => Status == "running" ? "Medium" : "Normal";

    public string TipoColor => Status switch
    {
        "running" => "#3A6FA8",
        "error"   => "#A83A3A",
        _         => "#283C5A"
    };

    public string TimeStr
    {
        get
        {
            if (_elapsedSec <= 0) return Status is "done" ? "✓" : "";
            var s = (int)_elapsedSec;
            return s >= 60 ? $"{s / 60}m {s % 60:00}s" : $"{s}s";
        }
    }

    public string TimeColor => Status switch
    {
        "done"    => "#00D97E",
        "running" => "#4D9FFF",
        "error"   => "#FF4757",
        _         => "#283C5A"
    };

    public string EstStr
    {
        get
        {
            if (Status != "pending" || _estSec <= 0) return "";
            var s = (int)_estSec;
            return s >= 60 ? $"~{s / 60}m{s % 60:00}s" : $"~{s}s";
        }
    }

    public Visibility EstVisible =>
        Status == "pending" && _estSec > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ConnectorVisible => _isLast ? Visibility.Collapsed : Visibility.Visible;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

// ── OsdWindow ───────────────────────────────────────────────────────────────
public partial class OsdWindow : Window
{
    private readonly string  _pcName;
    private readonly string  _apiUrl;
    private readonly DispatcherTimer _pollTimer    = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _elapsedTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _clockTimer   = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DateTime _startTime = DateTime.Now;
    private readonly ObservableCollection<OsdStep>  _steps    = new();
    private readonly ObservableCollection<LogEntry> _logLines = new();

    // HW info (popolata da demo o da API)
    private string _hwCpu  = "";
    private string _hwRam  = "";
    private string _hwDisk = "";
    private string _hwMac  = "";
    private string _hwIp   = "";

    // ── Step demo (25 step — stessa lista HTML) ──────────────────────────────
    private static readonly (string Key, string Label, string Tipo, double EstSec)[] KnownSteps =
    {
        ("disk_partition",     "Partizionamento disco",          "ps_script",      18),
        ("disk_format",        "Formattazione partizioni",       "ps_script",       9),
        ("windows_install",    "Installazione Windows 11",       "windows_update", 280),
        ("oobe_setup",         "Prima configurazione OOBE",      "ps_script",      38),
        ("drv_chipset",        "Driver chipset",                 "winget_install",  22),
        ("drv_nic",            "Driver scheda di rete",          "winget_install",  15),
        ("drv_audio",          "Driver audio",                   "winget_install",  11),
        ("drv_gpu",            "Driver grafica",                 "winget_install",  27),
        ("wu_critical",        "Windows Update — critico",       "windows_update", 210),
        ("wu_cumulative",      "Windows Update — cumulativo",    "windows_update", 195),
        ("vcredist",           "Microsoft Visual C++ 2022",      "winget_install",  20),
        ("dotnet",             ".NET Runtime 8",                 "winget_install",  24),
        ("security_agent",     "Agente sicurezza aziendale",     "winget_install",  32),
        ("firewall_policy",    "Configurazione policy firewall", "ps_script",        9),
        ("domain_join",        "Join dominio Active Directory",  "ps_script",       30),
        ("gpo_sync",           "Sincronizzazione GPO",           "ps_script",       13),
        ("cert_enroll",        "Registrazione certificato",      "ps_script",       11),
        ("office365",          "Office 365 ProPlus",             "winget_install", 100),
        ("outlook_cfg",        "Configurazione Outlook",         "reg_set",          7),
        ("teams",              "Teams client",                   "winget_install",  32),
        ("onedrive_cfg",       "OneDrive — configurazione",      "reg_set",          6),
        ("default_profile",    "Profilo utente predefinito",     "reg_set",          5),
        ("agent_install",      "Agente NovaSCM",                 "ps_script",        9),
        ("cleanup",            "Pulizia file temporanei",        "shell_script",     6),
        ("final_reboot",       "Riavvio finale",                 "reboot",           4),
    };

    public OsdWindow(string pcName, string apiUrl)
    {
        InitializeComponent();
        _pcName = pcName;
        _apiUrl = apiUrl.TrimEnd('/');

        TxtOsdPcName.Text = pcName;
        TxtOsdWfName.Text = "POST-INSTALL";
        OsdStepList.ItemsSource = _steps;
        LogLines.ItemsSource    = _logLines;

        foreach (var (key, label, tipo, est) in KnownSteps)
            _steps.Add(new OsdStep { StepKey = key, Label = label, Tipo = tipo, EstSec = est });

        RefreshIsLast();
        UpdateRing(0);

        _pollTimer.Tick    += OnPoll;
        _elapsedTimer.Tick += OnElapsed;
        _clockTimer.Tick   += OnClock;
        _elapsedTimer.Start();
        _clockTimer.Start();

        if (string.IsNullOrEmpty(_apiUrl))
            InitDemoData();
        else
        {
            _pollTimer.Start();
            _ = PollAsync();
        }
    }

    // ── Demo ─────────────────────────────────────────────────────────────────
    private void InitDemoData()
    {
        // Imposta alcuni step come done/running/pending
        var demoStates = new (string key, string status, double elapsed)[]
        {
            ("disk_partition",  "done",    16),
            ("disk_format",     "done",     8),
            ("windows_install", "done",   263),
            ("oobe_setup",      "done",    35),
            ("drv_chipset",     "done",    20),
            ("drv_nic",         "done",    13),
            ("drv_audio",       "running", 63),
        };

        foreach (var (key, status, elapsed) in demoStates)
        {
            var step = _steps.FirstOrDefault(x => x.StepKey == key);
            if (step == null) continue;
            step.Status     = status;
            step.ElapsedSec = elapsed;
            if (status != "pending") step.EstSec = 0; // nasconde stima su step già avviati
        }

        int done  = _steps.Count(x => x.Status is "done" or "skip");
        int total = _steps.Count;
        var pct   = (int)(done * 100.0 / total);

        OsdProgress.Value  = pct;
        UpdateRing(pct);
        TxtBarSteps.Text   = $"step {done} / {total}";
        TxtStepsCount.Text = $"{total} step";
        TxtStatDone.Text   = done.ToString();
        TxtStatRem.Text    = Math.Max(0, total - done).ToString();

        var running = _steps.FirstOrDefault(x => x.Status == "running");
        if (running != null)
        {
            TxtCurStepName.Text = running.Label;
            TxtCurStepTipo.Text = running.Tipo;
            TxtCurBoxName.Text  = running.Label;
            TxtCurBoxTipo.Text  = running.Tipo;
            TxtCurBoxTime.Text  = FormatSec(running.ElapsedSec);
        }
        CurSpinnerIcon.Text = "▶";
        CurSpinnerIcon.Foreground = MkBrush("#4D9FFF");

        // ETA: somma stime step futuri
        var remSec = _steps.Where(x => x.Status == "pending").Sum(x => x.EstSec);
        TxtStatEta.Text = remSec > 0 ? FormatSec(remSec) : "—";

        // Mostra HW dopo 2 s (simulazione)
        var hwTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        hwTimer.Tick += (_, _) =>
        {
            hwTimer.Stop();
            SetHardwareInfo("Intel Core i5-12400", "16 GB DDR4",
                            "Samsung SSD 980 500GB", "00:1A:2B:3C:4D:5E", "192.168.10.42");
        };
        hwTimer.Start();

        // Simula alcune righe di log
        AddLog("OUTPUT — drv_audio", LogColor.Info);
        AddLog("[INFO] Avvio rilevamento driver audio…");
        AddLog("[INFO] Cerca dispositivo audio compatibile…");
        AddLog("[INFO] winget install Realtek.AudioDriver --silent");
        AddLog("[INFO] Scaricamento in corso (32 MB)…");
    }

    // ── Hardware ─────────────────────────────────────────────────────────────
    private void SetHardwareInfo(string cpu, string ram, string disk, string mac, string ip)
    {
        _hwCpu = cpu; _hwRam = ram; _hwDisk = disk; _hwMac = mac; _hwIp = ip;
        TxtHwCpu.Text  = cpu;
        TxtHwRam.Text  = ram;
        TxtHwDisk.Text = disk;
        TxtHwMac.Text  = mac;
        TxtHwIp.Text   = ip;
    }

    // ── Log ──────────────────────────────────────────────────────────────────
    private enum LogColor { Default, Ok, Error, Warn, Info }

    private void AddLog(string text, LogColor color = LogColor.Default)
    {
        var brush = color switch
        {
            LogColor.Ok    => MkBrush("#00D97E"),
            LogColor.Error => MkBrush("#FF4757"),
            LogColor.Warn  => MkBrush("#F5A623"),
            LogColor.Info  => MkBrush("#4D9FFF"),
            _              => new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 130, 160, 210))
        };
        _logLines.Add(new LogEntry { Text = text, ForeColor = brush });
        TxtLogLines.Text = _logLines.Count == 1 ? "1 riga" : $"{_logLines.Count} righe";
        LogScrollViewer.ScrollToEnd();
    }

    private void ClearLog(string stepName)
    {
        _logLines.Clear();
        TxtLogStepName.Text = stepName;
        TxtLogLines.Text    = "0 righe";
    }

    // ── Polling ───────────────────────────────────────────────────────────────
    private async void OnPoll(object? s, EventArgs e) => await PollAsync();

    private async Task PollAsync()
    {
        if (string.IsNullOrEmpty(_apiUrl)) return;
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var json = await http.GetStringAsync($"{_apiUrl}/by-name/{_pcName}/steps");
            var doc  = System.Text.Json.JsonDocument.Parse(json);

            int done = 0, total = 0;
            string? currentLabel = null, currentTipo = null;
            double  curElapsed   = 0;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var key     = el.TryGetProperty("step_name",   out var sk) ? sk.GetString() ?? "" : "";
                var status  = el.TryGetProperty("status",      out var ss) ? ss.GetString() ?? "" : "done";
                var elapsed = el.TryGetProperty("elapsed_sec", out var es) ? es.GetDouble()      : 0;

                var existing = _steps.FirstOrDefault(x => x.StepKey == key);
                if (existing == null)
                {
                    var tipo  = key.StartsWith("install_") ? "winget_install"
                              : key.StartsWith("rename")   ? "ps_script"
                              : key.StartsWith("join")     ? "ps_script"
                              : "ps_script";
                    var label = key.StartsWith("install_") ? key[8..] : key.Replace("_", " ");
                    var insertBefore = _steps.FirstOrDefault(x =>
                        x.StepKey == "agent_install" || x.StepKey == "final_reboot");
                    var idx = insertBefore != null ? _steps.IndexOf(insertBefore) : _steps.Count;
                    existing = new OsdStep { StepKey = key, Label = label, Tipo = tipo };
                    _steps.Insert(idx, existing);
                    RefreshIsLast();
                }

                existing.Status     = status;
                existing.ElapsedSec = elapsed;
                if (status is "done" or "skip" or "running") existing.EstSec = 0;
                total++;
                if (status is "done" or "skip") done++;
                if (status == "running") { currentLabel = existing.Label; currentTipo = existing.Tipo; curElapsed = elapsed; }
            }

            if (total > 0)
            {
                var pct = (int)(done * 100.0 / total);
                OsdProgress.Value  = pct;
                UpdateRing(pct);
                TxtBarSteps.Text   = $"step {done} / {total}";
                TxtStepsCount.Text = $"{total} step";
                TxtStatDone.Text   = done.ToString();
                TxtStatRem.Text    = Math.Max(0, total - done).ToString();

                TxtCurStepName.Text = currentLabel ?? (done == total ? "Tutti gli step completati" : "In corso…");
                TxtCurStepTipo.Text = currentTipo  ?? "";
                TxtCurBoxName.Text  = currentLabel ?? (done == total ? "Completato" : "In attesa…");
                TxtCurBoxTipo.Text  = currentTipo  ?? "";
                TxtCurBoxTime.Text  = curElapsed > 0 ? FormatSec(curElapsed) : "—";
                CurSpinnerIcon.Text = done == total ? "✓" : "▶";
                CurSpinnerIcon.Foreground = done == total ? MkBrush("#00D97E") : MkBrush("#4D9FFF");
            }

            var runningStep = _steps.FirstOrDefault(x => x.Status == "running");
            if (runningStep != null)
            {
                var container = OsdStepList.ItemContainerGenerator.ContainerFromItem(runningStep) as FrameworkElement;
                container?.BringIntoView();
            }

            if (_steps.Any() && _steps.All(x => x.Status is "done" or "skip"))
                ShowCompleted();
        }
        catch { }
    }

    // ── Ring arc ─────────────────────────────────────────────────────────────
    private void UpdateRing(double pct)
    {
        TxtRingPct.Text = $"{(int)pct}%";
        if (pct <= 0) { RingArcPath.Data = null; return; }

        const double cx = 45, cy = 45, r = 40;
        var angle  = Math.Min(pct / 100.0 * 360.0, 359.9);
        var endRad = (angle - 90) * Math.PI / 180;
        var endX   = cx + r * Math.Cos(endRad);
        var endY   = cy + r * Math.Sin(endRad);
        var large  = angle >= 180;

        var color = pct >= 100 ? "#00D97E" : "#4D9FFF";
        RingArcPath.Stroke = MkBrush(color);
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        RingArcPath.Data = Geometry.Parse(
            $"M {cx.ToString("F2", ic)},{(cy - r).ToString("F2", ic)} A {r},{r} 0 {(large ? 1 : 0)},1 {endX.ToString("F2", ic)},{endY.ToString("F2", ic)}");
    }

    // ── Completato ───────────────────────────────────────────────────────────
    private bool _completed = false;
    private void ShowCompleted()
    {
        if (_completed) return;
        _completed = true;
        _pollTimer.Stop();

        UpdateRing(100);
        OsdProgress.Value  = 100;
        TxtCurStepName.Text = "Configurazione completata";
        TxtCurStepTipo.Text = "—";
        TxtCurBoxName.Text  = "Tutti gli step eseguiti con successo";
        TxtCurBoxTipo.Text  = "—";
        TxtCurBoxTime.Text  = "";
        CurSpinnerIcon.Text = "✓";
        CurSpinnerIcon.Foreground = MkBrush("#00D97E");
        TxtOsdWfName.Text   = "COMPLETATO";

        // Overlay
        var elapsed = DateTime.Now - _startTime;
        TxtOvHost.Text   = _pcName;
        TxtOvDetail.Text = $"{_steps.Count} step · durata {FormatSec(elapsed.TotalSeconds)}";

        // HW chips
        void AddChip(string text)
        {
            var tb = new TextBlock
            {
                Text       = text,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize   = 9,
                Foreground = MkBrush("#829696"),
            };
            var border = new Border
            {
                Padding         = new Thickness(8, 4, 8, 4),
                CornerRadius    = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush     = MkBrush("#1E2A45"),
                Margin          = new Thickness(3, 3, 3, 3),
                Child           = tb,
            };
            border.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(12, 77, 159, 255));
            OvHwChips.Children.Add(border);
        }
        if (_hwCpu  != "") AddChip("🖥 " + _hwCpu);
        if (_hwRam  != "") AddChip("💾 " + _hwRam);
        if (_hwDisk != "") AddChip("💿 " + _hwDisk);
        if (_hwMac  != "") AddChip("🌐 " + _hwMac + (_hwIp != "" ? " · " + _hwIp : ""));

        OvDone.Visibility = Visibility.Visible;

        // Countdown riavvio
        int secs = 30;
        var countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        countdown.Tick += (_, _) =>
        {
            secs--;
            TxtRebootN.Text = secs.ToString();
            if (secs <= 0) { countdown.Stop(); Application.Current.Shutdown(); }
        };
        countdown.Start();

        // Simula acquisizione screenshot (in produzione: API call)
        var ssTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        ssTimer.Tick += (_, _) =>
        {
            ssTimer.Stop();
            TxtSsStatus.Text = "Screenshot acquisito ✓";
            TxtSsTime.Text   = DateTime.Now.ToString("HH:mm:ss");
        };
        ssTimer.Start();
    }

    // ── Timers ───────────────────────────────────────────────────────────────
    private void OnElapsed(object? s, EventArgs e)
    {
        var el = DateTime.Now - _startTime;
        TxtStatEla.Text    = el.TotalMinutes >= 1
            ? $"{(int)el.TotalMinutes}m{el.Seconds:00}"
            : $"{(int)el.TotalSeconds}s";
        TxtOsdElapsed.Text = $"{(int)el.TotalMinutes:00}:{el.Seconds:00}";
    }

    private void OnClock(object? s, EventArgs e)
        => TxtOsdClock.Text = DateTime.Now.ToString("HH:mm:ss");

    // ── Helpers ──────────────────────────────────────────────────────────────
    private void RefreshIsLast()
    {
        for (int i = 0; i < _steps.Count; i++)
            _steps[i].IsLast = (i == _steps.Count - 1);
    }

    private static string FormatSec(double s)
    {
        var sec = (int)s;
        return sec >= 60 ? $"{sec / 60}m {sec % 60:00}s" : $"{sec}s";
    }

    private static SolidColorBrush MkBrush(string hex)
        => new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));

    // ── Input ────────────────────────────────────────────────────────────────
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_completed) e.Cancel = true;
        base.OnClosing(e);
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Ctrl+Shift+Esc → chiudi
        if (e.Key == System.Windows.Input.Key.Escape &&
            System.Windows.Input.Keyboard.Modifiers.HasFlag(
                System.Windows.Input.ModifierKeys.Control |
                System.Windows.Input.ModifierKeys.Shift))
        {
            _completed = true;
            Close();
        }
        // Esc semplice → minimizza (per accedere al desktop senza chiudere)
        else if (e.Key == System.Windows.Input.Key.Escape &&
                 System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.None)
        {
            WindowState = System.Windows.WindowState.Minimized;
        }
    }
}
