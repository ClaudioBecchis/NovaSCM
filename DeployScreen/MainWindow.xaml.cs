using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NovaSCMDeployScreen
{
    // ══════════════════════════════════════════════════════
    //  MODELLI
    // ══════════════════════════════════════════════════════
    public enum StepStatus { Future, Active, Done, Error, Skip }

    public class StepViewModel : INotifyPropertyChanged
    {
        private StepStatus _status = StepStatus.Future;
        private double     _elapsed;
        private double     _estSec;

        public int    Id      { get; set; }
        public int    StepNum { get; set; }
        public string Nome    { get; set; } = "";
        public string Tipo    { get; set; } = "";

        public double EstSec
        {
            get => _estSec;
            set { _estSec = value; OnChanged(nameof(EstLabel)); }
        }

        public StepStatus Status
        {
            get => _status;
            set { _status = value; OnChanged(nameof(Status)); OnChanged(nameof(TimeLabel)); OnChanged(nameof(EstLabel)); }
        }

        public double Elapsed
        {
            get => _elapsed;
            set { _elapsed = value; OnChanged(nameof(Elapsed)); OnChanged(nameof(TimeLabel)); }
        }

        public string TimeLabel => Status switch
        {
            StepStatus.Done   => _elapsed > 0 ? FmtSec(_elapsed) : "✓",
            StepStatus.Active => _elapsed > 0 ? FmtSec(_elapsed) : "0s",
            StepStatus.Error  => "ERR",
            StepStatus.Skip   => "skip",
            _                 => ""
        };

        public string EstLabel => Status == StepStatus.Future && _estSec > 0
            ? $"~{FmtSec(_estSec)}" : "";

        public static string FmtSec(double s)
        {
            int sec = (int)Math.Round(s);
            return sec < 60 ? $"{sec}s" : $"{sec / 60}m {(sec % 60):D2}s";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string n) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class LogLine
    {
        public string Text  { get; set; } = "";
        public Brush  Color { get; set; } = Brushes.White;
    }

    public class HardwareInfo
    {
        [JsonPropertyName("cpu")]  public string Cpu  { get; set; } = "";
        [JsonPropertyName("ram")]  public string Ram  { get; set; } = "";
        [JsonPropertyName("disk")] public string Disk { get; set; } = "";
        [JsonPropertyName("mac")]  public string Mac  { get; set; } = "";
        [JsonPropertyName("ip")]   public string Ip   { get; set; } = "";
    }

    public class PcWorkflowResponse
    {
        [JsonPropertyName("id")]          public int    Id     { get; set; }
        [JsonPropertyName("pc_name")]     public string PcName { get; set; } = "";
        [JsonPropertyName("status")]      public string Status { get; set; } = "";
        [JsonPropertyName("hardware")]    public HardwareInfo? Hardware { get; set; }
        [JsonPropertyName("screenshot")]  public string? ScreenshotB64 { get; set; }
        [JsonPropertyName("steps")]       public System.Collections.Generic.List<StepResponse>? Steps { get; set; }
    }

    public class StepResponse
    {
        [JsonPropertyName("step_id")]     public int    StepId     { get; set; }
        [JsonPropertyName("ordine")]      public int    Ordine     { get; set; }
        [JsonPropertyName("nome")]        public string Nome       { get; set; } = "";
        [JsonPropertyName("tipo")]        public string Tipo       { get; set; } = "";
        [JsonPropertyName("status")]      public string Status     { get; set; } = "";
        [JsonPropertyName("elapsed_sec")] public double ElapsedSec { get; set; }
        [JsonPropertyName("est_sec")]     public double EstSec     { get; set; }
        [JsonPropertyName("log")]         public string? Log       { get; set; }
    }

    public class Config
    {
        public string Hostname { get; set; } = "WKS-MKTG-042";
        public string Domain   { get; set; } = "polariscore.local";
        public string WfName   { get; set; } = "Deploy Base Win 11";
        public string Server   { get; set; } = "http://192.168.20.110:9091";
        public string ApiKey   { get; set; } = "";
        public string PwId     { get; set; } = "1";
        public string Version  { get; set; } = "1.8.1";
        public bool   Demo     { get; set; } = false;

        public static Config Parse(string[] args)
        {
            var c = new Config();
            foreach (var a in args)
            {
                var parts = a.Split('=', 2);
                if (parts.Length != 2) continue;
                switch (parts[0].ToLower())
                {
                    case "hostname": c.Hostname = parts[1]; break;
                    case "domain":   c.Domain   = parts[1]; break;
                    case "wf":       c.WfName   = parts[1]; break;
                    case "server":   c.Server   = parts[1]; break;
                    case "key":      c.ApiKey   = parts[1]; break;
                    case "pw_id":    c.PwId     = parts[1]; break;
                    case "ver":      c.Version  = parts[1]; break;
                    case "demo":     c.Demo     = parts[1] is "1" or "true"; break;
                }
            }
            return c;
        }
    }

    // ══════════════════════════════════════════════════════
    //  MAIN WINDOW
    // ══════════════════════════════════════════════════════
    public partial class MainWindow : Window
    {
        private readonly Config   _cfg;
        private readonly ObservableCollection<StepViewModel> _steps = new();
        private readonly ObservableCollection<LogLine>       _logs  = new();
        private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer _pollTimer  = new() { Interval = TimeSpan.FromSeconds(3) };
        private readonly DispatcherTimer _elaTimer   = new() { Interval = TimeSpan.FromMilliseconds(200) };
        private readonly HttpClient      _http       = new() { Timeout = TimeSpan.FromSeconds(5) };
        private DateTime  _startTime;
        private int       _activeIdx = -1;
        private HardwareInfo? _hw;

        // ── Demo ──
        private int    _demoIdx;
        private double _demoElapsed;
        private DispatcherTimer? _demoTimer;

        private static readonly (string nome, string tipo, double dur, double est)[] DEMO_STEPS =
        {
            ("Partizionamento disco",          "ps_script",       16,  18),
            ("Formattazione partizioni",        "ps_script",        8,   9),
            ("Installazione Windows 11",        "windows_update",  260, 280),
            ("Prima configurazione OOBE",       "ps_script",        35,  38),
            ("Driver chipset",                  "winget_install",   20,  22),
            ("Driver scheda di rete",           "winget_install",   14,  15),
            ("Driver audio",                    "winget_install",   10,  11),
            ("Driver grafica",                  "winget_install",   25,  27),
            ("Windows Update — critico",        "windows_update",  200, 210),
            ("Windows Update — cumulativo",     "windows_update",  180, 195),
            ("Microsoft Visual C++ 2022",       "winget_install",   18,  20),
            (".NET Runtime 8",                  "winget_install",   22,  24),
            ("Agente sicurezza aziendale",      "winget_install",   30,  32),
            ("Configurazione policy firewall",  "ps_script",         8,   9),
            ("Join dominio Active Directory",   "ps_script",         28,  30),
            ("Sincronizzazione GPO",            "ps_script",         12,  13),
            ("Registrazione certificato",       "ps_script",         10,  11),
            ("Office 365 ProPlus",              "winget_install",    95, 100),
            ("Configurazione Outlook",          "reg_set",            6,   7),
            ("Teams client",                    "winget_install",    30,  32),
            ("OneDrive — configurazione",       "reg_set",            5,   6),
            ("Profilo utente predefinito",      "reg_set",            4,   5),
            ("Agente NovaSCM",                  "ps_script",          8,   9),
            ("Pulizia file temporanei",         "shell_script",       5,   6),
            ("Riavvio finale",                  "reboot",             3,   4),
        };

        private static readonly System.Collections.Generic.Dictionary<int, string[]> DEMO_LOGS = new()
        {
            [1]  = new[] { "[INFO] Avvio partizionamento…", "[INFO] Disco rilevato: Samsung SSD 980 500GB", "[INFO] Creazione EFI 500MB", "[INFO] Creazione C:\\ 450GB", "[OK] Partizionamento completato" },
            [3]  = new[] { "[INFO] Avvio setup.exe /unattend", "[INFO] Copia file Windows…", "[INFO] Installazione feature…", "[WARN] Riavvio richiesto", "[OK] Windows 11 Pro 23H2 installato" },
            [15] = new[] { "[INFO] Add-Computer -DomainName polariscore.local", "[INFO] Autenticazione in corso…", "[INFO] Oggetto computer creato in OU=Workstations", "[OK] Join dominio completato" },
            [18] = new[] { "[INFO] Download Office 365 ProPlus (2.1 GB)…", "[INFO] 45% — 180 MB/s", "[INFO] 100% completato", "[INFO] Installazione componenti…", "[OK] Microsoft 365 Apps installato" },
        };

        public MainWindow()
        {
            _cfg = Config.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray());
            InitializeComponent();
            StepList.ItemsSource = _steps;
            LogList.ItemsSource  = _logs;
            InitUI();
            _clockTimer.Tick += (_, _) => UpdateClock();
            _clockTimer.Start();
            if (_cfg.Demo) StartDemo();
            else           StartPolling();
        }

        private void InitUI()
        {
            TxtHostname.Text      = _cfg.Hostname;
            TxtDomain.Text        = _cfg.Domain;
            TxtWfName.Text        = _cfg.WfName;
            FooterVer.Text        = _cfg.Version;
            FooterServer.Text     = _cfg.Server.Replace("http://","").Replace("https://","");
            OverlayDoneHost.Text  = _cfg.Hostname;
            if (!string.IsNullOrEmpty(_cfg.ApiKey))
                _http.DefaultRequestHeaders.Add("X-Api-Key", _cfg.ApiKey);
            UpdateClock();
        }

        private void UpdateClock() =>
            TxtClock.Text = DateTime.Now.ToString("HH:mm:ss");

        // ── ELAPSED TICKER ──
        private void StartElapsTimer()
        {
            _elaTimer.Tick -= ElaTimerTick;
            _elaTimer.Tick += ElaTimerTick;
            _elaTimer.Start();
        }
        private void ElaTimerTick(object? s, EventArgs e)
        {
            StatEla.Text = FmtElapsed(DateTime.Now - _startTime);
            if (_activeIdx >= 0 && _activeIdx < _steps.Count)
            {
                var st = _steps[_activeIdx];
                if (st.Status == StepStatus.Active)
                {
                    st.Elapsed += 0.2;
                    CurBoxTime.Text = StepViewModel.FmtSec(st.Elapsed);
                    UpdateEta();
                }
            }
        }

        // ══════════════════════════════════════════════════════
        //  HARDWARE
        // ══════════════════════════════════════════════════════
        private void ApplyHardware(HardwareInfo hw)
        {
            _hw = hw;
            HwCpu.Text  = hw.Cpu;
            HwRam.Text  = hw.Ram;
            HwDisk.Text = hw.Disk;
            HwMac.Text  = hw.Mac;
            HwIp.Text   = hw.Ip;
        }

        // ══════════════════════════════════════════════════════
        //  DEMO
        // ══════════════════════════════════════════════════════
        private void StartDemo()
        {
            _startTime = DateTime.Now;
            _steps.Clear();
            for (int i = 0; i < DEMO_STEPS.Length; i++)
            {
                var (nome, tipo, _, est) = DEMO_STEPS[i];
                _steps.Add(new StepViewModel { Id = i+1, StepNum = i+1, Nome = nome, Tipo = tipo, EstSec = est });
            }
            TxtStepCount.Text = $"{_steps.Count} step";
            UpdateStatsUI();
            StartElapsTimer();
            // hw simulata dopo 2s
            Task.Delay(2000).ContinueWith(_ => Dispatcher.Invoke(() =>
                ApplyHardware(new HardwareInfo
                {
                    Cpu = "Intel Core i5-12400", Ram = "16 GB DDR4",
                    Disk = "Samsung SSD 980 500GB", Mac = "00:1A:2B:3C:4D:5E", Ip = "192.168.10.42"
                })));
            RunDemoStep(0);
        }

        private void RunDemoStep(int idx)
        {
            if (idx >= _steps.Count) { Dispatcher.Invoke(() => ShowDone()); return; }
            _demoIdx     = idx;
            _demoElapsed = 0;
            Dispatcher.Invoke(() =>
            {
                _steps[idx].Status  = StepStatus.Active;
                _steps[idx].Elapsed = 0;
                _activeIdx = idx;
                UpdateStatsUI();
                UpdateCurBox(_steps[idx]);
                ScrollToActive();
                Dispatcher.InvokeAsync(() => ColorStepRow(idx, StepStatus.Active), DispatcherPriority.Loaded);
                // log
                _logs.Clear();
                LogStepName.Text   = _steps[idx].Nome;
                LogLinesCount.Text = "0 righe";
                var lines = DEMO_LOGS.TryGetValue(idx + 1, out var l) ? l
                    : new[] { $"[INFO] Avvio {_steps[idx].Nome}…", "[INFO] Esecuzione in corso…" };
                int li = 0;
                void AddNext()
                {
                    if (li < lines.Length)
                    {
                        var line = lines[li++];
                        AddLogLine(line);
                        Task.Delay((int)(280 + new Random().NextDouble() * 400))
                            .ContinueWith(_ => Dispatcher.Invoke(AddNext));
                    }
                }
                Task.Delay(300).ContinueWith(_ => Dispatcher.Invoke(AddNext));
            });

            double durSec = Math.Max(1.2, DEMO_STEPS[idx].dur * 0.042);
            _demoTimer?.Stop();
            _demoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _demoTimer.Tick += (_, _) =>
            {
                _demoElapsed += 0.12;
                if (_demoElapsed >= durSec)
                {
                    _demoTimer.Stop();
                    Dispatcher.Invoke(() =>
                    {
                        AddLogLine($"[OK] {_steps[idx].Nome} completato in {StepViewModel.FmtSec(_demoElapsed)}", "ok");
                        _steps[idx].Status  = StepStatus.Done;
                        _steps[idx].Elapsed = _demoElapsed;
                        UpdateStatsUI();
                        ColorStepRow(idx, StepStatus.Done);
                    });
                    Task.Delay(220).ContinueWith(_ => Dispatcher.Invoke(() => RunDemoStep(idx + 1)));
                }
            };
            _demoTimer.Start();
        }

        // ══════════════════════════════════════════════════════
        //  POLLING REALE
        // ══════════════════════════════════════════════════════
        private void StartPolling()
        {
            _startTime = DateTime.Now;
            StartElapsTimer();
            _ = FetchAsync();
            _pollTimer.Tick += async (_, _) => await FetchAsync();
            _pollTimer.Start();
        }

        private async Task FetchAsync()
        {
            try
            {
                var json = await _http.GetStringAsync($"{_cfg.Server}/api/pc-workflows/{_cfg.PwId}");
                var data = JsonSerializer.Deserialize<PcWorkflowResponse>(json);
                if (data == null) return;
                Dispatcher.Invoke(() => ApplyServerState(data));
            }
            catch { }
        }

        private void ApplyServerState(PcWorkflowResponse data)
        {
            if (data.Hardware != null) ApplyHardware(data.Hardware);

            if (data.Steps == null || data.Steps.Count == 0) return;
            if (_steps.Count == 0)
            {
                foreach (var s in data.Steps.OrderBy(x => x.Ordine))
                    _steps.Add(new StepViewModel
                    {
                        Id = s.StepId, StepNum = s.Ordine, Nome = s.Nome,
                        Tipo = s.Tipo, EstSec = s.EstSec
                    });
                TxtStepCount.Text = $"{_steps.Count} step";
            }

            foreach (var srv in data.Steps)
            {
                var vm = _steps.FirstOrDefault(x => x.Id == srv.StepId);
                if (vm == null) continue;
                var ns = srv.Status switch
                {
                    "done"    => StepStatus.Done,
                    "running" => StepStatus.Active,
                    "error"   => StepStatus.Error,
                    "skipped" => StepStatus.Skip,
                    _         => StepStatus.Future,
                };
                if (vm.Status != ns) { vm.Status = ns; ColorStepRow(_steps.IndexOf(vm), ns); }
                if (srv.ElapsedSec > 0) vm.Elapsed = srv.ElapsedSec;
                // log dell'ultimo step attivo
                if (ns == StepStatus.Active && !string.IsNullOrEmpty(srv.Log))
                    AppendServerLog(srv.Log, srv.Nome);
            }

            _activeIdx = _steps.ToList().FindIndex(x => x.Status == StepStatus.Active);
            if (_activeIdx >= 0) { UpdateCurBox(_steps[_activeIdx]); ScrollToActive(); }
            UpdateStatsUI();

            if (data.Status == "completed")
            {
                _pollTimer.Stop(); _elaTimer.Stop();
                if (!string.IsNullOrEmpty(data.ScreenshotB64)) ShowScreenshot(data.ScreenshotB64);
                ShowDone();
            }
            if (data.Status == "failed")
            {
                _pollTimer.Stop(); _elaTimer.Stop();
                ShowError(_steps.FirstOrDefault(x => x.Status == StepStatus.Error));
            }
        }

        // ══════════════════════════════════════════════════════
        //  LOG
        // ══════════════════════════════════════════════════════
        private void AddLogLine(string text, string cls = "")
        {
            var color = cls switch
            {
                "ok"   => new SolidColorBrush(Color.FromRgb(0, 217, 126)),
                "err"  => new SolidColorBrush(Color.FromRgb(255, 71, 87)),
                "warn" => new SolidColorBrush(Color.FromRgb(245, 166, 35)),
                "info" => new SolidColorBrush(Color.FromArgb(180, 77, 159, 255)),
                _      => new SolidColorBrush(Color.FromArgb(190, 130, 160, 210)),
            };
            if (text.StartsWith("[OK]"))   color = new SolidColorBrush(Color.FromRgb(0, 217, 126));
            if (text.StartsWith("[ERR]"))  color = new SolidColorBrush(Color.FromRgb(255, 71, 87));
            if (text.StartsWith("[WARN]")) color = new SolidColorBrush(Color.FromRgb(245, 166, 35));
            if (text.StartsWith("[INFO]")) color = new SolidColorBrush(Color.FromArgb(180, 77, 159, 255));

            _logs.Add(new LogLine { Text = text, Color = color });
            LogLinesCount.Text = $"{_logs.Count} {(_logs.Count == 1 ? "riga" : "righe")}";
            Dispatcher.InvokeAsync(() => LogScroller.ScrollToEnd(), DispatcherPriority.Background);
        }

        private void AppendServerLog(string rawLog, string stepName)
        {
            _logs.Clear();
            LogStepName.Text = stepName;
            foreach (var line in rawLog.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                AddLogLine(line.TrimEnd());
        }

        // ══════════════════════════════════════════════════════
        //  UI UPDATE
        // ══════════════════════════════════════════════════════
        private void UpdateStatsUI()
        {
            int done  = _steps.Count(s => s.Status is StepStatus.Done or StepStatus.Skip);
            int total = _steps.Count;
            int pct   = total > 0 ? done * 100 / total : 0;

            StatDone.Text      = done.ToString();
            StatRem.Text       = Math.Max(0, total - done).ToString();
            TxtBarLabel.Text   = $"step {done} / {total}";
            TxtPct.Text        = $"{pct}%";
            StatEla.Text       = FmtElapsed(DateTime.Now - _startTime);

            DrawRing(pct);

            var parentWidth = ((System.Windows.Controls.Border)BarFill.Parent).ActualWidth;
            if (parentWidth > 0)
            {
                BarFill.Width = parentWidth * pct / 100.0;
                BarFill.Background = pct >= 100
                    ? new SolidColorBrush(Color.FromRgb(0, 217, 126))
                    : (Brush)new LinearGradientBrush(
                        Color.FromRgb(42, 111, 255),
                        Color.FromRgb(126, 200, 255), 0);
            }
            UpdateEta();
        }

        private void UpdateEta()
        {
            double rem = _steps
                .Where(s => s.Status == StepStatus.Future)
                .Sum(s => s.EstSec > 0 ? s.EstSec : 10);
            var active = _activeIdx >= 0 && _activeIdx < _steps.Count ? _steps[_activeIdx] : null;
            if (active != null && active.EstSec > 0)
                rem += Math.Max(0, active.EstSec - active.Elapsed);
            StatEta.Text = rem > 0 ? StepViewModel.FmtSec(rem) : "—";
        }

        private void DrawRing(int pct)
        {
            const double cx = 45, cy = 45, r = 40;
            double angle = pct / 100.0 * 360.0;
            if (angle >= 360) angle = 359.99;
            double rad = (angle - 90) * Math.PI / 180.0;
            double x = cx + r * Math.Cos(rad), y = cy + r * Math.Sin(rad);

            if (pct == 0) { RingArc.Data = null; return; }

            var brush = pct >= 100
                ? (Brush)new SolidColorBrush(Color.FromRgb(0, 217, 126))
                : new SolidColorBrush(Color.FromRgb(77, 159, 255));

            RingArc.Data = new PathGeometry(new[]
            {
                new PathFigure(new Point(cx, cy - r), new PathSegment[]
                {
                    new ArcSegment(new Point(x, y), new Size(r, r), 0,
                        angle > 180, SweepDirection.Clockwise, true)
                }, false)
            });
            RingArc.Stroke = brush;
            RingArc.StrokeThickness = 5;
        }

        private void UpdateCurBox(StepViewModel s)
        {
            CurBoxName.Text = s.Nome;
            CurBoxTipo.Text = s.Tipo;
            TxtCurName.Text = s.Nome;
            TxtCurTipo.Text = s.Tipo;
        }

        private void ScrollToActive()
        {
            if (_activeIdx < 0 || _activeIdx >= _steps.Count) return;
            if (StepList.ItemContainerGenerator.ContainerFromIndex(_activeIdx) is FrameworkElement el)
                el.BringIntoView();
        }

        private void ColorStepRow(int idx, StepStatus status)
        {
            if (StepList.ItemContainerGenerator.ContainerFromIndex(idx) is not FrameworkElement c) return;
            var circle = FindChild<System.Windows.Controls.Border>(c, "Circle");
            var ctext  = FindChild<System.Windows.Controls.TextBlock>(c, "CircleText");
            var sname  = FindChild<System.Windows.Controls.TextBlock>(c, "StepName");
            var conn   = FindChild<System.Windows.Shapes.Rectangle>(c, "Connector");
            if (circle == null) return;
            switch (status)
            {
                case StepStatus.Done:
                    circle.Background  = new SolidColorBrush(Color.FromArgb(40, 0, 217, 126));
                    circle.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 217, 126));
                    if (ctext != null) { ctext.Text = "✓"; ctext.Foreground = new SolidColorBrush(Color.FromRgb(0, 217, 126)); }
                    if (sname != null) sname.Foreground = new SolidColorBrush(Color.FromRgb(82, 96, 128));
                    if (conn  != null) conn.Fill = new SolidColorBrush(Color.FromArgb(64, 0, 217, 126));
                    break;
                case StepStatus.Active:
                    circle.Background  = new SolidColorBrush(Color.FromArgb(40, 77, 159, 255));
                    circle.BorderBrush = new SolidColorBrush(Color.FromRgb(77, 159, 255));
                    if (ctext != null) { ctext.Text = "▶"; ctext.Foreground = new SolidColorBrush(Color.FromRgb(77, 159, 255)); }
                    if (sname != null) { sname.Foreground = new SolidColorBrush(Color.FromRgb(232, 240, 255)); sname.FontWeight = FontWeights.Medium; }
                    break;
                case StepStatus.Error:
                    circle.Background  = new SolidColorBrush(Color.FromArgb(40, 255, 71, 87));
                    circle.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 71, 87));
                    if (ctext != null) { ctext.Text = "✕"; ctext.Foreground = new SolidColorBrush(Color.FromRgb(255, 71, 87)); }
                    if (sname != null) sname.Foreground = new SolidColorBrush(Color.FromRgb(255, 71, 87));
                    break;
            }
        }

        // ══════════════════════════════════════════════════════
        //  SCHERMATE FINALI
        // ══════════════════════════════════════════════════════
        private void ShowDone()
        {
            BarFill.Width = ((System.Windows.Controls.Border)BarFill.Parent).ActualWidth;
            BarFill.Background = new SolidColorBrush(Color.FromRgb(0, 217, 126));
            TxtPct.Text = "100%"; DrawRing(100);

            int done = _steps.Count(s => s.Status is StepStatus.Done or StepStatus.Skip);
            OverlayDoneDetail.Text = $"{done} step · durata {FmtElapsed(DateTime.Now - _startTime)}";

            // HW chips
            if (_hw != null)
            {
                void AddChip(string emoji, string val)
                {
                    var b = new System.Windows.Controls.Border
                    {
                        CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 4, 8, 4),
                        Background = new SolidColorBrush(Color.FromArgb(20, 77, 159, 255)),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(80, 40, 55, 90)),
                        BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 6, 6)
                    };
                    var sp = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                    sp.Children.Add(new System.Windows.Controls.TextBlock { Text = emoji + " ", FontSize = 10 });
                    sp.Children.Add(new System.Windows.Controls.TextBlock
                    {
                        Text = val, FontFamily = new FontFamily("Consolas"),
                        FontSize = 9, FontWeight = FontWeights.Medium,
                        Foreground = new SolidColorBrush(Color.FromRgb(232, 240, 255))
                    });
                    b.Child = sp;
                    OverlayHwPanel.Children.Add(b);
                }
                AddChip("🖥", _hw.Cpu);
                AddChip("💾", _hw.Ram);
                AddChip("💿", _hw.Disk);
                AddChip("🌐", $"{_hw.Mac} · {_hw.Ip}");
            }

            FadeIn(OverlayDone);
            SsTime.Text = DateTime.Now.ToString("HH:mm:ss");

            int n = 30;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            t.Tick += (_, _) => { RebootCount.Text = (--n).ToString(); if (n <= 0) t.Stop(); };
            t.Start();
        }

        private void ShowScreenshot(string b64)
        {
            try
            {
                var bytes = Convert.FromBase64String(b64);
                var bmp = new BitmapImage();
                using var ms = new System.IO.MemoryStream(bytes);
                bmp.BeginInit(); bmp.StreamSource = ms; bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit();
                SsPlaceholder.Visibility = Visibility.Collapsed;
                SsImage.Source           = bmp;
                SsImage.Visibility       = Visibility.Visible;
            }
            catch { SsStatus.Text = "Screenshot non disponibile"; }
        }

        private void ShowError(StepViewModel? s)
        {
            OverlayErrStep.Text   = s != null ? $"Errore in: {s.Nome}" : "Step sconosciuto";
            OverlayErrDetail.Text = $"Contattare il supporto IT · NovaSCM v{_cfg.Version}";
            FadeIn(OverlayErr);
        }

        private static void FadeIn(UIElement el)
        {
            el.Visibility = Visibility.Visible;
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(600)));
        }

        // ══════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════
        private static string FmtElapsed(TimeSpan ts)
        {
            int s = (int)ts.TotalSeconds;
            return s < 60 ? $"{s}s" : $"{s / 60}m {(s % 60):D2}s";
        }

        private static T? FindChild<T>(DependencyObject p, string name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(p); i++)
            {
                var c = VisualTreeHelper.GetChild(p, i);
                if (c is T fe && fe.Name == name) return fe;
                var r = FindChild<T>(c, name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
