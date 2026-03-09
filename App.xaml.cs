using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor   = System.Windows.Media.Color;
using WpfColors  = System.Windows.Media.Colors;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfFontFamily = System.Windows.Media.FontFamily;

namespace PolarisManager;

public partial class App : Application
{
    public static readonly string LogPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "PolarisManager", "debug.log");

    private const string GitHubIssuesUrl = "https://github.com/ClaudioBecchis/NovaSCM/issues/new";
    private const string AppVersion      = "1.7.3";

    private void OnStartup(object sender, StartupEventArgs e)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        Log("=== PolarisManager avviato ===");

        // Eccezioni non gestite sul thread UI
        DispatcherUnhandledException += (s, ex) =>
        {
            Log($"[UI Exception] {ex.Exception}");
            ShowCrashDialog(ex.Exception);
            ex.Handled = true;
        };

        // Eccezioni non gestite su thread background
        AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
        {
            Log($"[Unhandled] {ex.ExceptionObject}");
            var exc = ex.ExceptionObject as Exception;
            Dispatcher.Invoke(() => ShowCrashDialog(exc, isFatal: true));
        };

        // Task non gestiti
        TaskScheduler.UnobservedTaskException += (s, ex) =>
        {
            Log($"[Task] {ex.Exception}");
            ex.SetObserved();
        };

        // Modalità OSD: NovaSCM.exe --osd <pcname> <apiurl>
        var args = e.Args;
        if (args.Length >= 1 && args[0] == "--osd")
        {
            var pcName = args.Length >= 2 ? args[1] : Environment.MachineName;
            var apiUrl = args.Length >= 3 ? args[2] : "";
            Log($"[OSD] Avvio modalità OSD — PC={pcName} API={apiUrl}");
            var osd = new OsdWindow(pcName, apiUrl);
            MainWindow = osd;
            osd.Show();
        }
        else
        {
            var main = new MainWindow();
            MainWindow = main;
            main.Show();
        }
    }

    public static void Log(string msg)
    {
        try
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
            File.AppendAllText(LogPath, line + Environment.NewLine);
            System.Diagnostics.Debug.WriteLine(line);
        }
        catch { }
    }

    // ── Crash reporter ────────────────────────────────────────────────────────
    private static void ShowCrashDialog(Exception? ex, bool isFatal = false)
    {
        var title   = isFatal ? "Errore critico — NovaSCM" : "Errore imprevisto — NovaSCM";
        var message = ex?.Message ?? "Errore sconosciuto";
        var stack   = ex?.ToString() ?? "Nessun dettaglio disponibile";

        // Finestra crash personalizzata
        var win = new Window
        {
            Title           = title,
            Width           = 620,
            Height          = 420,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode      = ResizeMode.NoResize,
            Background      = new WpfSolidColorBrush(WpfColor.FromRgb(30, 30, 30)),
            Foreground      = WpfBrushes.White,
            FontFamily      = new WpfFontFamily("Segoe UI"),
        };

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Titolo
        var lblTitle = new TextBlock
        {
            Text       = isFatal ? "💥 Errore critico" : "⚠️ Errore imprevisto",
            FontSize   = 18,
            FontWeight = FontWeights.Bold,
            Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(255, 80, 80)),
            Margin     = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(lblTitle, 0);

        // Messaggio breve
        var lblMsg = new TextBlock
        {
            Text         = message,
            FontSize     = 13,
            Foreground   = WpfBrushes.LightGray,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(lblMsg, 1);

        // Stack trace
        var txtStack = new TextBox
        {
            Text              = stack,
            IsReadOnly        = true,
            FontFamily        = new WpfFontFamily("Consolas"),
            FontSize          = 11,
            Background        = new WpfSolidColorBrush(WpfColor.FromRgb(20, 20, 20)),
            Foreground        = new WpfSolidColorBrush(WpfColor.FromRgb(180, 180, 180)),
            BorderBrush       = new WpfSolidColorBrush(WpfColor.FromRgb(60, 60, 60)),
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping      = TextWrapping.NoWrap,
            Margin            = new Thickness(0, 0, 0, 12),
        };
        Grid.SetRow(txtStack, 2);

        // Pulsanti
        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var btnGithub = new Button
        {
            Content    = "🐛  Segnala su GitHub",
            Padding    = new Thickness(14, 8, 14, 8),
            Margin     = new Thickness(0, 0, 8, 0),
            Background = new WpfSolidColorBrush(WpfColor.FromRgb(36, 103, 178)),
            Foreground = WpfBrushes.White,
            BorderBrush = WpfBrushes.Transparent,
            Cursor     = System.Windows.Input.Cursors.Hand,
        };
        // Costruisce l'URL GitHub pre-compilato
        static string BuildGitHubUrl(string appVer, string msg, string st) {
            var os   = Environment.OSVersion.ToString();
            var body = WebUtility.UrlEncode(
                $"**NovaSCM v{appVer}** — {os}\n\n" +
                $"**Errore:** {msg}\n\n" +
                $"**Stack trace:**\n```\n{st}\n```\n\n" +
                $"**Log:** `{LogPath}`\n\n" +
                $"**Passi per riprodurre:**\n1. \n2. \n3. \n\n" +
                $"**Comportamento atteso:**\n\n**Comportamento effettivo:**\n");
            var title = WebUtility.UrlEncode($"[Bug] {msg.Split('\n')[0].Truncate(80)}");
            return $"{GitHubIssuesUrl}?title={title}&body={body}&labels=bug";
        }
        var ghUrl = BuildGitHubUrl(AppVersion, message, stack);

        // Auto-apre il browser immediatamente (anche senza clic utente)
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ghUrl) { UseShellExecute = true }); }
        catch { }

        btnGithub.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ghUrl) { UseShellExecute = true }); }
            catch { }
        };

        var btnCopy = new Button
        {
            Content    = "📋  Copia",
            Padding    = new Thickness(14, 8, 14, 8),
            Margin     = new Thickness(0, 0, 8, 0),
            Background = new WpfSolidColorBrush(WpfColor.FromRgb(60, 60, 60)),
            Foreground = WpfBrushes.White,
            BorderBrush = WpfBrushes.Transparent,
            Cursor     = System.Windows.Input.Cursors.Hand,
        };
        btnCopy.Click += (_, _) => Clipboard.SetText(stack);

        var btnClose = new Button
        {
            Content    = isFatal ? "Chiudi app" : "Ignora",
            Padding    = new Thickness(14, 8, 14, 8),
            Background = new WpfSolidColorBrush(WpfColor.FromRgb(80, 40, 40)),
            Foreground = WpfBrushes.White,
            BorderBrush = WpfBrushes.Transparent,
            Cursor     = System.Windows.Input.Cursors.Hand,
        };
        btnClose.Click += (_, _) =>
        {
            win.Close();
            if (isFatal) Current.Shutdown(1);
        };

        btnPanel.Children.Add(btnGithub);
        btnPanel.Children.Add(btnCopy);
        btnPanel.Children.Add(btnClose);
        Grid.SetRow(btnPanel, 3);

        grid.Children.Add(lblTitle);
        grid.Children.Add(lblMsg);
        grid.Children.Add(txtStack);
        grid.Children.Add(btnPanel);

        win.Content = grid;
        win.ShowDialog();
    }
}

// Estensione helper
internal static class StringExtensions
{
    public static string Truncate(this string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
