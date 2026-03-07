using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace PolarisManager;

public partial class App : Application
{
    public static readonly string LogPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "PolarisManager", "debug.log");

    private void OnStartup(object sender, StartupEventArgs e)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        Log("=== PolarisManager avviato ===");

        // Eccezioni non gestite sul thread UI
        DispatcherUnhandledException += (s, ex) =>
        {
            Log($"[UI Exception] {ex.Exception}");
            MessageBox.Show(
                $"Errore imprevisto:\n\n{ex.Exception.Message}\n\nDettagli salvati in:\n{LogPath}",
                "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        // Eccezioni non gestite su thread background
        AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
        {
            Log($"[Unhandled] {ex.ExceptionObject}");
            MessageBox.Show(
                $"Errore critico:\n\n{ex.ExceptionObject}\n\nDettagli in:\n{LogPath}",
                "Errore critico", MessageBoxButton.OK, MessageBoxImage.Error);
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
}
