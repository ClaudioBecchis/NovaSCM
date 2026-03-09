using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace NovaSCMDeployScreen
{
    // ══════════════════════════════════════════════════════════
    //  MODELLI
    // ══════════════════════════════════════════════════════════
    public enum StepStatus { Future, Active, Done, Error, Skip }

    public class StepViewModel : INotifyPropertyChanged
    {
        private StepStatus _status = StepStatus.Future;
        private double     _elapsed;
        private string     _timeLabel = "";

        public int    Id      { get; set; }
        public int    StepNum { get; set; }
        public string Nome    { get; set; } = "";
        public string Tipo    { get; set; } = "";

        public StepStatus Status
        {
            get => _status;
            set { _status = value; OnChanged(nameof(Status)); UpdateTimeLabel(); }
        }

        public double Elapsed
        {
            get => _elapsed;
            set { _elapsed = value; OnChanged(nameof(Elapsed)); UpdateTimeLabel(); }
        }

        public string TimeLabel
        {
            get => _timeLabel;
            private set { _timeLabel = value; OnChanged(nameof(TimeLabel)); }
        }

        private void UpdateTimeLabel()
        {
            TimeLabel = Status switch
            {
                StepStatus.Done   => _elapsed > 0 ? FmtSec(_elapsed) : "✓",
                StepStatus.Active => _elapsed > 0 ? FmtSec(_elapsed) : "0s",
                StepStatus.Error  => "ERR",
                StepStatus.Skip   => "skip",
                _                 => ""
            };
        }

        public static string FmtSec(double s)
        {
            int sec = (int)Math.Round(s);
            if (sec < 60) return $"{sec}s";
            return $"{sec / 60}m {(sec % 60):D2}s";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string n) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    // JSON response da /api/pc-workflows/{id}
    public class PcWorkflowResponse
    {
        [JsonPropertyName("id")]          public int    Id         { get; set; }
        [JsonPropertyName("pc_name")]     public string PcName     { get; set; } = "";
        [JsonPropertyName("status")]      public string Status     { get; set; } = "";
        [JsonPropertyName("steps")]       public List<StepResponse>? Steps { get; set; }
    }
    public class StepResponse
    {
        [JsonPropertyName("step_id")]     public int    StepId     { get; set; }
        [JsonPropertyName("ordine")]      public int    Ordine     { get; set; }
        [JsonPropertyName("nome")]        public string Nome       { get; set; } = "";
        [JsonPropertyName("tipo")]        public string Tipo       { get; set; } = "";
        [JsonPropertyName("status")]      public string Status     { get; set; } = "";
        [JsonPropertyName("elapsed_sec")] public double ElapsedSec { get; set; }
    }

    // ══════════════════════════════════════════════════════════
    //  CONFIGURAZIONE (da args o defaults)
    // ══════════════════════════════════════════════════════════
    public class Config
    {
        public string Hostname { get; set; } = "WKS-MKTG-042";
        public string Domain   { get; set; } = "polariscore.local";
        public string WfName   { get; set; } = "Deploy Base Win 11";
        public string Server   { get; set; } = "http://192.168.20.110:9091";
        public string ApiKey   { get; set; } = "";
        public string PwId     { get; set; } = "1";
        public string Version  { get; set; } = "1.8.0";
        public bool   Demo     { get; set; } = false;

        // Parsing da riga di comando:
        //   NovaSCMDeployScreen.exe hostname=PC-01 domain=corp.local wf="Deploy Base" \
        //                            server=http://192.168.20.110:9091 key=APIKEY pw_id=14 demo=1
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
                    case "demo":     c.Demo     = parts[1] == "1" || parts[1].ToLower() == "true"; break;
                }
            }
            return c;
        }
    }

    // ══════════════════════════════════════════════════════════
    //  MAIN WINDOW
    // ══════════════════════════════════════════════════════════
    public partial class MainWindow : Window
    {
        private readonly Config   _cfg;
        private readonly ObservableCollection<StepViewModel> _steps = new();
        private readonly DispatcherTimer _clockTimer  = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer _pollTimer   = new() { Interval = TimeSpan.FromSeconds(3) };
        private readonly DispatcherTimer _elapsTimer  = new() { Interval = TimeSpan.FromMilliseconds(200) };
        private readonly HttpClient      _http        = new() { Timeout = TimeSpan.FromSeconds(5) };
        private DateTime  _startTime;
        private int       _activeIdx  = -1;
        private double    _barWidth   = 0;

        // Demo
        private int    _demoIdx     = 0;
        private double _demoElapsed = 0;
        private DispatcherTimer? _demoTimer;
        private static readonly (string nome, string tipo, double dur)[] DEMO_STEPS =
        {
            ("Partizionamento disco",          "ps_script",        16),
            ("Formattazione partizioni",        "ps_script",         8),
            ("Installazione Windows 11",        "windows_update",  260),
            ("Prima configurazione OOBE",       "ps_script",        35),
            ("Driver chipset",                  "winget_install",   20),
            ("Driver scheda di rete",           "winget_install",   14),
            ("Driver audio",                    "winget_install",   10),
            ("Driver grafica",                  "winget_install",   25),
            ("Windows Update — critico",        "windows_update",  200),
            ("Windows Update — cumulativo",     "windows_update",  180),
            ("Microsoft Visual C++ 2022",       "winget_install",   18),
            (".NET Runtime 8",                  "winget_install",   22),
            ("Agente sicurezza aziendale",      "winget_install",   30),
            ("Configurazione policy firewall",  "ps_script",         8),
            ("Join dominio Active Directory",   "ps_script",        28),
            ("Sincronizzazione GPO",            "ps_script",        12),
            ("Registrazione certificato",       "ps_script",        10),
            ("Office 365 ProPlus",              "winget_install",   95),
            ("Configurazione Outlook",          "reg_set",           6),
            ("Teams client",                    "winget_install",   30),
            ("OneDrive — configurazione",       "reg_set",           5),
            ("Profilo utente predefinito",      "reg_set",           4),
            ("Agente NovaSCM",                  "ps_script",         8),
            ("Pulizia file temporanei",         "shell_script",      5),
            ("Riavvio finale",                  "reboot",            3),
        };

        public MainWindow()
        {
            _cfg = Config.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray());
            InitializeComponent();
            InitUI();

            StepList.ItemsSource = _steps;

            _clockTimer.Tick  += (_, _) => UpdateClock();
            _clockTimer.Start();

            if (_cfg.Demo) StartDemo();
            else           StartPolling();
        }

        // ─── INIT UI ───────────────────────────────────────────
        private void InitUI()
        {
            TxtHostname.Text    = _cfg.Hostname;
            TxtDomain.Text      = _cfg.Domain;
            TxtWfName.Text      = _cfg.WfName;
            FooterVer.Text      = _cfg.Version;
            FooterServer.Text   = _cfg.Server.Replace("http://","").Replace("https://","");
            OverlayDoneHost.Text = _cfg.Hostname;
            _http.DefaultRequestHeaders.Clear();
            if (!string.IsNullOrEmpty(_cfg.ApiKey))
                _http.DefaultRequestHeaders.Add("X-Api-Key", _cfg.ApiKey);
            UpdateClock();
        }

        // ─── OROLOGIO ──────────────────────────────────────────
        private void UpdateClock() =>
            TxtClock.Text = DateTime.Now.ToString("HH:mm:ss");

        // ─── ELAPSED TIMER (aggiorna contatore step attivo) ────
        private void StartElapsTimer()
        {
            _elapsTimer.Tick -= ElapsTimerTick;
            _elapsTimer.Tick += ElapsTimerTick;
            _elapsTimer.Start();
        }
        private void ElapsTimerTick(object? s, EventArgs e)
        {
            if (_activeIdx < 0 || _activeIdx >= _steps.Count) return;
            var step = _steps[_activeIdx];
            if (step.Status != StepStatus.Active) return;
            step.Elapsed += 0.2;
            CurBoxTime.Text = StepViewModel.FmtSec(step.Elapsed);
            StatEla.Text    = FmtElapsed(DateTime.Now - _startTime);
        }

        // ═══════════════════════════════════════════════════════
        //  DEMO
        // ═══════════════════════════════════════════════════════
        private void StartDemo()
        {
            _startTime = DateTime.Now;
            _steps.Clear();
            for (int i = 0; i < DEMO_STEPS.Length; i++)
            {
                var (nome, tipo, _) = DEMO_STEPS[i];
                _steps.Add(new StepViewModel
                {
                    Id = i + 1, StepNum = i + 1, Nome = nome, Tipo = tipo,
                    Status = StepStatus.Future
                });
            }
            TxtStepCount.Text = $"{_steps.Count} step";
            UpdateStatsUI();
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
                ColorStepRow(idx, StepStatus.Active);
            });

            StartElapsTimer();

            double durSec = Math.Max(1.5, DEMO_STEPS[idx].dur * 0.045);
            _demoTimer?.Stop();
            _demoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _demoTimer.Tick += (_, _) =>
            {
                _demoElapsed += 0.12;
                if (_demoElapsed >= durSec)
                {
                    _demoTimer.Stop();
                    _elapsTimer.Stop();
                    Dispatcher.Invoke(() =>
                    {
                        _steps[idx].Status  = StepStatus.Done;
                        _steps[idx].Elapsed = _demoElapsed;
                        UpdateStatsUI();
                        ColorStepRow(idx, StepStatus.Done);
                    });
                    Task.Delay(220).ContinueWith(_ => RunDemoStep(idx + 1));
                }
            };
            _demoTimer.Start();
        }

        // ═══════════════════════════════════════════════════════
        //  POLLING REALE
        // ═══════════════════════════════════════════════════════
        private void StartPolling()
        {
            _startTime = DateTime.Now;
            StartElapsTimer();
            _ = FetchStatusAsync();
            _pollTimer.Tick += async (_, _) => await FetchStatusAsync();
            _pollTimer.Start();
        }

        private async Task FetchStatusAsync()
        {
            try
            {
                var url  = $"{_cfg.Server}/api/pc-workflows/{_cfg.PwId}";
                var json = await _http.GetStringAsync(url);
                var data = JsonSerializer.Deserialize<PcWorkflowResponse>(json);
                if (data == null) return;
                Dispatcher.Invoke(() => ApplyServerState(data));
            }
            catch { /* server non raggiungibile, continua */ }
        }

        private void ApplyServerState(PcWorkflowResponse data)
        {
            if (data.Steps == null || data.Steps.Count == 0) return;

            // Prima volta: costruisci lista
            if (_steps.Count == 0)
            {
                foreach (var s in data.Steps.OrderBy(x => x.Ordine))
                    _steps.Add(new StepViewModel
                    {
                        Id = s.StepId, StepNum = s.Ordine, Nome = s.Nome, Tipo = s.Tipo
                    });
                TxtStepCount.Text = $"{_steps.Count} step";
            }

            // Aggiorna stati
            foreach (var srv in data.Steps)
            {
                var vm = _steps.FirstOrDefault(x => x.Id == srv.StepId);
                if (vm == null) continue;
                var newStatus = srv.Status switch
                {
                    "done"    => StepStatus.Done,
                    "running" => StepStatus.Active,
                    "error"   => StepStatus.Error,
                    "skipped" => StepStatus.Skip,
                    _         => StepStatus.Future,
                };
                if (vm.Status != newStatus) { vm.Status = newStatus; ColorStepRow(_steps.IndexOf(vm), newStatus); }
                if (srv.ElapsedSec > 0) vm.Elapsed = srv.ElapsedSec;
            }

            _activeIdx = _steps.ToList().FindIndex(x => x.Status == StepStatus.Active);
            var active = _activeIdx >= 0 ? _steps[_activeIdx] : null;
            if (active != null) UpdateCurBox(active);
            UpdateStatsUI();
            ScrollToActive();

            if (data.Status == "completed") { _pollTimer.Stop(); _elapsTimer.Stop(); ShowDone(); }
            if (data.Status == "failed")    { _pollTimer.Stop(); _elapsTimer.Stop(); ShowError(_steps.FirstOrDefault(x => x.Status == StepStatus.Error)); }
        }

        // ═══════════════════════════════════════════════════════
        //  AGGIORNA UI
        // ═══════════════════════════════════════════════════════
        private void UpdateStatsUI()
        {
            int done  = _steps.Count(s => s.Status == StepStatus.Done || s.Status == StepStatus.Skip);
            int total = _steps.Count;
            int pct   = total > 0 ? (done * 100 / total) : 0;

            StatDone.Text = done.ToString();
            StatRem.Text  = Math.Max(0, total - done).ToString();
            StatTot.Text  = total.ToString();
            StatEla.Text  = FmtElapsed(DateTime.Now - _startTime);
            TxtBarLabel.Text = $"step {done} / {total}";
            TxtPct.Text      = $"{pct}%";

            // Ring circolare
            DrawRing(pct);

            // Barra lineare — calcoliamo la larghezza dalla BarFill parent
            var parentWidth = ((System.Windows.Controls.Border)BarFill.Parent).ActualWidth;
            if (parentWidth > 0)
            {
                _barWidth = parentWidth * pct / 100.0;
                BarFill.Width = _barWidth;
                // colore barra
                BarFill.Background = pct >= 100
                    ? new SolidColorBrush(Color.FromRgb(0, 217, 126))
                    : new LinearGradientBrush(
                        Color.FromRgb(42, 111, 255),
                        Color.FromRgb(126, 200, 255), 0);
            }
        }

        private void DrawRing(int pct)
        {
            const double cx = 50, cy = 50, r = 40;
            double angle   = pct / 100.0 * 360.0;
            if (angle >= 360) angle = 359.99;
            double rad     = (angle - 90) * Math.PI / 180.0;
            double x       = cx + r * Math.Cos(rad);
            double y       = cy + r * Math.Sin(rad);
            bool   large   = angle > 180;

            var brush = pct >= 100
                ? new SolidColorBrush(Color.FromRgb(0, 217, 126))
                : new SolidColorBrush(Color.FromRgb(77, 159, 255));

            if (pct == 0)
            {
                RingArc.Data = null;
                return;
            }

            RingArc.Data = new PathGeometry(new[]
            {
                new PathFigure(
                    new Point(cx, cy - r),
                    new PathSegment[]
                    {
                        new ArcSegment(
                            new Point(x, y),
                            new Size(r, r),
                            0,
                            large,
                            SweepDirection.Clockwise,
                            true)
                    },
                    false)
            });
            RingArc.Stroke          = brush;
            RingArc.StrokeThickness = 5;
        }

        private void UpdateCurBox(StepViewModel step)
        {
            CurBoxName.Text = step.Nome;
            CurBoxTipo.Text = step.Tipo;
            TxtCurName.Text = step.Nome;
            TxtCurTipo.Text = step.Tipo;
        }

        private void ScrollToActive()
        {
            if (_activeIdx < 0 || _activeIdx >= _steps.Count) return;
            // Scroll ListBox al passo attivo
            if (StepList.ItemContainerGenerator.ContainerFromIndex(_activeIdx) is FrameworkElement el)
                el.BringIntoView();
        }

        private void ColorStepRow(int idx, StepStatus status)
        {
            // Cambia colori del cerchio direttamente sull'elemento generato
            if (StepList.ItemContainerGenerator.ContainerFromIndex(idx) is not FrameworkElement container)
                return;

            var circle = FindChild<System.Windows.Controls.Border>(container, "Circle");
            var ctext  = FindChild<System.Windows.Controls.TextBlock>(container, "CircleText");
            var sname  = FindChild<System.Windows.Controls.TextBlock>(container, "StepName");
            var conn   = FindChild<System.Windows.Shapes.Rectangle>(container, "Connector");

            if (circle == null) return;

            switch (status)
            {
                case StepStatus.Done:
                    circle.Background   = new SolidColorBrush(Color.FromArgb(40, 0, 217, 126));
                    circle.BorderBrush  = new SolidColorBrush(Color.FromRgb(0, 217, 126));
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
                case StepStatus.Skip:
                    if (sname != null) sname.TextDecorations = TextDecorations.Strikethrough;
                    break;
            }
        }

        // ═══════════════════════════════════════════════════════
        //  SCHERMATE FINALI
        // ═══════════════════════════════════════════════════════
        private void ShowDone()
        {
            // Porta la barra al 100%
            BarFill.Width      = ((System.Windows.Controls.Border)BarFill.Parent).ActualWidth;
            BarFill.Background = new SolidColorBrush(Color.FromRgb(0, 217, 126));
            TxtPct.Text        = "100%";
            DrawRing(100);

            int done = _steps.Count(s => s.Status == StepStatus.Done || s.Status == StepStatus.Skip);
            OverlayDoneDetail.Text =
                $"{done} step eseguiti · durata totale {FmtElapsed(DateTime.Now - _startTime)}";

            FadeIn(OverlayDone);

            // Countdown riavvio
            int n = 30;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            t.Tick += (_, _) =>
            {
                RebootCount.Text = (--n).ToString();
                if (n <= 0) t.Stop();
            };
            t.Start();
        }

        private void ShowError(StepViewModel? step)
        {
            OverlayErrStep.Text   = step != null ? $"Errore in: {step.Nome}" : "Step sconosciuto";
            OverlayErrDetail.Text = $"Contattare il supporto IT · NovaSCM v{_cfg.Version}";
            FadeIn(OverlayErr);
        }

        private static void FadeIn(UIElement el)
        {
            el.Visibility = Visibility.Visible;
            var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(600));
            el.BeginAnimation(OpacityProperty, anim);
        }

        // ═══════════════════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════════════════
        private static string FmtElapsed(TimeSpan ts)
        {
            int s = (int)ts.TotalSeconds;
            if (s < 60) return $"{s}s";
            return $"{s / 60}m {(s % 60):D2}s";
        }

        // Trova un elemento figlio per nome
        private static T? FindChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T fe && fe.Name == name) return fe;
                var result = FindChild<T>(child, name);
                if (result != null) return result;
            }
            return null;
        }

        // Aggiorna colori quando i container diventano disponibili
        private void StepList_Loaded(object sender, RoutedEventArgs e)
        {
            StepList.ItemContainerGenerator.StatusChanged += (_, _) =>
            {
                if (StepList.ItemContainerGenerator.Status ==
                    System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                {
                    for (int i = 0; i < _steps.Count; i++)
                        ColorStepRow(i, _steps[i].Status);
                }
            };
        }
    }
}
