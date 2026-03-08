// NovaSCM v1.4.0
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfBrush   = System.Windows.Media.SolidColorBrush;

namespace PolarisManager;

class WfDetailStepRow
{
    public int    Ordine   { get; set; }
    public string Nome     { get; set; } = "";
    public string Tipo     { get; set; } = "";
    public string Status   { get; set; } = "pending";
    public string Output   { get; set; } = "";
    public System.Windows.Media.SolidColorBrush StatusColor => Status switch
    {
        "done"    => WpfBrushes.LimeGreen,
        "running" => WpfBrushes.DodgerBlue,
        "error"   => WpfBrushes.Tomato,
        "skipped" => WpfBrushes.SlateGray,
        _         => WpfBrushes.DimGray,
    };
}

public partial class WfAssignDetailWindow : Window
{
    private readonly int    _pwId;
    private readonly string _apiBase;
    private readonly ObservableCollection<WfDetailStepRow> _stepRows = [];
    private readonly DispatcherTimer _timer;

    public WfAssignDetailWindow(int pwId, string pcName, string wfNome, string apiBase)
    {
        InitializeComponent();
        _pwId    = pwId;
        _apiBase = apiBase;

        TxtTitle.Text    = $"{pcName}  —  {wfNome}";
        TxtSubtitle.Text = $"Assegnazione #{pwId}";

        GridSteps.ItemsSource = _stepRows;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        Closed += (_, _) => _timer.Stop();

        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var json = await http.GetStringAsync($"{_apiBase}/api/pc-workflows/{_pwId}");
            var doc  = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            var status   = root.TryGetProperty("status",   out var st) ? st.GetString() ?? "" : "";
            var lastSeen = root.TryGetProperty("last_seen", out var ls) ? ls.GetString() ?? "" : null;

            TxtStatus.Text = status.ToUpperInvariant();
            TxtStatus.Foreground = status switch
            {
                "running"   => WpfBrushes.DodgerBlue,
                "completed" => WpfBrushes.LimeGreen,
                "failed"    => WpfBrushes.Tomato,
                _           => WpfBrushes.SlateGray,
            };

            TxtLastSeen.Text = lastSeen != null
                ? $"Last seen: {FormatAgo(lastSeen)}"
                : "";

            _stepRows.Clear();
            int done = 0, total = 0;
            if (root.TryGetProperty("steps", out var stepsEl))
            {
                foreach (var el in stepsEl.EnumerateArray())
                {
                    var stepStatus = el.TryGetProperty("status", out var ss) ? ss.GetString() ?? "pending" : "pending";
                    var output     = el.TryGetProperty("output", out var ou) ? ou.GetString() ?? ""        : "";
                    // Tronca output lungo
                    if (output.Length > 120) output = output[..120] + "…";

                    _stepRows.Add(new WfDetailStepRow
                    {
                        Ordine = el.TryGetProperty("ordine", out var o) ? o.GetInt32()        : 0,
                        Nome   = el.TryGetProperty("nome",   out var n) ? n.GetString() ?? "" : "",
                        Tipo   = el.TryGetProperty("tipo",   out var t) ? t.GetString() ?? "" : "",
                        Status = stepStatus,
                        Output = output,
                    });
                    total++;
                    if (stepStatus is "done" or "skipped") done++;
                }
            }

            var pct = status == "completed" ? 100 : (total > 0 ? done * 100 / total : 0);
            PrgMain.Value     = pct;
            TxtProgress.Text  = $"{pct}%";
            PrgMain.Foreground = status == "completed" ? WpfBrushes.LimeGreen
                               : status == "failed"    ? WpfBrushes.Tomato
                               : WpfBrushes.DodgerBlue;

            TxtRefreshStatus.Text = $"Aggiornato: {DateTime.Now:HH:mm:ss}";

            // Ferma il timer se terminato
            if (status is "completed" or "failed") _timer.Stop();
        }
        catch (Exception ex)
        {
            TxtRefreshStatus.Text = $"Errore: {ex.Message}";
        }
    }

    private static string FormatAgo(string iso)
    {
        if (!DateTime.TryParse(iso, out var dt)) return iso;
        var sec = (int)(DateTime.Now - dt).TotalSeconds;
        if (sec < 60)    return $"{sec}s fa";
        if (sec < 3600)  return $"{sec / 60}m fa";
        if (sec < 86400) return $"{sec / 3600}h fa";
        return dt.ToString("dd/MM/yyyy HH:mm");
    }

    private async void BtnRefresh_Click(object s, RoutedEventArgs e)
        => await RefreshAsync();

    private void BtnClose_Click(object s, RoutedEventArgs e)
        => Close();
}
