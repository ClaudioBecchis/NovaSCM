// NovaSCM v1.5.0
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace PolarisManager;

// ── Modello step per la lista ──────────────────────────────────────────────
public class OsdStep : INotifyPropertyChanged
{
    private string _status = "pending";

    public string StepKey { get; set; } = "";

    public string Label { get; set; } = "";

    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Icon));
            OnPropertyChanged(nameof(IconColor));
            OnPropertyChanged(nameof(TextColor));
            OnPropertyChanged(nameof(Weight));
        }
    }

    private string _sub = "";
    public string Sub
    {
        get => _sub;
        set { _sub = value; OnPropertyChanged(); OnPropertyChanged(nameof(SubVisible)); }
    }

    public string Icon => Status switch
    {
        "done"    => "✓",
        "running" => "▶",
        "error"   => "✗",
        _         => "○"
    };

    public string IconColor => Status switch
    {
        "done"    => "#10b981",
        "running" => "#f59e0b",
        "error"   => "#ef4444",
        _         => "#1e3a5f"
    };

    public string TextColor => Status switch
    {
        "pending" => "#374151",
        "done"    => "#9ca3af",
        _         => "#ffffff"
    };

    public string Weight => Status == "running" ? "SemiBold" : "Normal";

    public Visibility SubVisible =>
        string.IsNullOrEmpty(_sub) ? Visibility.Collapsed : Visibility.Visible;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

// ── OsdWindow ──────────────────────────────────────────────────────────────
public partial class OsdWindow : Window
{
    private readonly string  _pcName;
    private readonly string  _apiUrl;
    private readonly DispatcherTimer _pollTimer   = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _elapsedTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DateTime _startTime = DateTime.Now;
    private readonly ObservableCollection<OsdStep> _steps = new();

    // Step predefiniti (ordinati)
    private static readonly (string Key, string Label)[] KnownSteps =
    {
        ("postinstall_start", "Avvio post-installazione"),
        ("rename_pc",         "Rinomina computer"),
        ("winget_install",    "Installazione winget"),
        ("agent_install",     "Agente NovaSCM"),
        ("checkin",           "Registrazione completata"),
    };

    public OsdWindow(string pcName, string apiUrl)
    {
        InitializeComponent();
        _pcName = pcName;
        _apiUrl = apiUrl.TrimEnd('/');

        TxtOsdPcName.Text = pcName;
        OsdStepList.ItemsSource = _steps;

        // Popola step predefiniti
        foreach (var (key, label) in KnownSteps)
            _steps.Add(new OsdStep { StepKey = key, Label = label });

        _pollTimer.Tick   += OnPoll;
        _elapsedTimer.Tick += OnElapsed;
        _pollTimer.Start();
        _elapsedTimer.Start();

        // Prima poll immediata
        _ = PollAsync();
    }

    // ── polling API ────────────────────────────────────────────────────────
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
            string? currentLabel = null;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var key    = el.TryGetProperty("step_name", out var sk) ? sk.GetString() ?? "" : "";
                var status = el.TryGetProperty("status",    out var ss) ? ss.GetString() ?? "" : "done";

                // Cerca step predefinito
                var existing = _steps.FirstOrDefault(x => x.StepKey == key);
                if (existing == null)
                {
                    // Step dinamico (es. install_Mozilla.Firefox)
                    var label = key.StartsWith("install_")
                        ? "⬛ " + key[8..]
                        : key.Replace("_", " ");

                    // Inserisce prima di agent_install / checkin
                    var insertBefore = _steps.FirstOrDefault(x =>
                        x.StepKey == "agent_install" || x.StepKey == "checkin");
                    var idx = insertBefore != null ? _steps.IndexOf(insertBefore) : _steps.Count;
                    existing = new OsdStep { StepKey = key, Label = label };
                    _steps.Insert(idx, existing);
                }

                existing.Status = status;
                total++;
                if (status == "done")  done++;
                if (status == "running") currentLabel = existing.Label;
            }

            // Aggiorna progresso
            if (total > 0)
            {
                var pct = (int)(done * 100.0 / total);
                OsdProgress.Value     = pct;
                TxtOsdPercent.Text    = $"{pct}%";
                TxtOsdCurrentStep.Text = currentLabel ?? (done == total ? "✅  Configurazione completata!" : "In corso...");
            }

            // Completato?
            if (_steps.All(x => x.Status == "done") && _steps.Count > 0)
                ShowCompleted();
        }
        catch { /* server non ancora raggiungibile: normale all'avvio */ }
    }

    private bool _completed = false;
    private void ShowCompleted()
    {
        if (_completed) return;
        _completed = true;
        _pollTimer.Stop();

        TxtOsdIcon.Text         = "✓";
        TxtOsdCurrentStep.Text  = "Configurazione completata!";
        TxtOsdSubStep.Text      = "Il computer verrà riavviato automaticamente.";
        OsdProgress.Value       = 100;
        TxtOsdPercent.Text      = "100%";

        // Countdown riavvio (già gestito dal postinstall.ps1 con shutdown /r)
        // Qui chiudiamo la finestra dopo 15s
        var countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        int secs = 15;
        countdown.Tick += (_, _) =>
        {
            secs--;
            TxtOsdSubStep.Text = $"Il computer verrà riavviato in {secs} secondi.";
            if (secs <= 0) { countdown.Stop(); Application.Current.Shutdown(); }
        };
        countdown.Start();
    }

    // ── timer elapsed ──────────────────────────────────────────────────────
    private void OnElapsed(object? s, EventArgs e)
    {
        var elapsed = DateTime.Now - _startTime;
        TxtOsdElapsed.Text = $"Tempo trascorso: {(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
    }

    // ── nessuna chiusura accidentale (solo Alt+F4 con Ctrl+Shift per admin) ─
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_completed) e.Cancel = true;
        base.OnClosing(e);
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Ctrl+Shift+Esc = chiudi forzato (per debug)
        if (e.Key == System.Windows.Input.Key.Escape &&
            System.Windows.Input.Keyboard.Modifiers.HasFlag(
                System.Windows.Input.ModifierKeys.Control |
                System.Windows.Input.ModifierKeys.Shift))
        {
            _completed = true;
            Close();
        }
    }
}
