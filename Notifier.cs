using System.Windows;
using System.Windows.Threading;
using WpfColor  = System.Windows.Media.Color;
using WpfBrush  = System.Windows.Media.SolidColorBrush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace PolarisManager;

// FEAT-02: notifiche desktop per eventi critici (toast WPF senza dipendenze esterne)
public static class Notifier
{
    public enum Level { Info, Warning, Error }

    public static void Show(string title, string body, Level level = Level.Info,
                            Action? onClick = null, int autoCloseSec = 7)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var toast = new AlertToast(title, body, level, onClick, autoCloseSec);
            toast.Show();
        });
    }

    public static void WorkflowFailed(string pcName, string stepName)
        => Show("⚠️  Workflow fallito", $"{pcName} — step: {stepName}", Level.Error);

    public static void WorkflowCompleted(string pcName)
        => Show("✅  Workflow completato", pcName, Level.Info);

    public static void PcOffline(string pcName)
        => Show("📡  PC offline", $"{pcName} non risponde", Level.Warning);

    public static void CertExpiringSoon(string pcName, int daysLeft)
        => Show("🔐  Certificato in scadenza", $"{pcName} — scade tra {daysLeft} giorni", Level.Warning);
}

// Finestra toast riutilizzabile (simile a UpdateToast, senza XAML separato)
public class AlertToast : Window
{
    private readonly DispatcherTimer _timer = new();

    // BUG: ogni toast si posizionava nello stesso angolo in basso a destra
    // senza tener conto di altri toast aperti — due notifiche ravvicinate si
    // sovrapponevano illeggibili. Lista dei toast vivi per lo stacking verticale.
    private static readonly List<AlertToast> _open = [];

    private static void Restack()
    {
        var wa = SystemParameters.WorkArea;
        double y = wa.Bottom - 18;
        foreach (var t in _open)
        {
            t.Left = wa.Right - t.ActualWidth - 18;
            t.Top  = y - t.ActualHeight;
            y -= t.ActualHeight + 8;
        }
    }

    public AlertToast(string title, string body, Notifier.Level level,
                      Action? onClick, int autoCloseSec)
    {
        WindowStyle         = WindowStyle.None;
        AllowsTransparency  = true;
        Background          = WpfBrushes.Transparent;
        Topmost             = true;
        ShowInTaskbar       = false;
        SizeToContent       = SizeToContent.WidthAndHeight;
        ResizeMode          = ResizeMode.NoResize;
        Width               = 340;
        Cursor              = System.Windows.Input.Cursors.Hand;

        var accent = level switch
        {
            Notifier.Level.Error   => WpfColor.FromRgb(220, 38, 38),
            Notifier.Level.Warning => WpfColor.FromRgb(245, 158, 11),
            _                      => WpfColor.FromRgb(59, 130, 246)
        };

        var border = new System.Windows.Controls.Border
        {
            Background    = new WpfBrush(WpfColor.FromRgb(15, 23, 42)),
            BorderBrush   = new WpfBrush(accent),
            BorderThickness = new Thickness(0, 3, 0, 0),
            CornerRadius  = new CornerRadius(8),
            Padding       = new Thickness(16, 12, 16, 14),
            Child         = new System.Windows.Controls.StackPanel
            {
                Children =
                {
                    new System.Windows.Controls.TextBlock
                    {
                        Text       = title,
                        Foreground = WpfBrushes.White,
                        FontSize   = 13,
                        FontWeight = FontWeights.SemiBold
                    },
                    new System.Windows.Controls.TextBlock
                    {
                        Text       = body,
                        Foreground = new WpfBrush(WpfColor.FromRgb(148, 163, 184)),
                        FontSize   = 12,
                        Margin     = new Thickness(0, 4, 0, 0),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
        Content = border;

        Loaded += (_, _) =>
        {
            _open.Add(this);
            Restack();
        };

        Closed += (_, _) =>
        {
            _open.Remove(this);
            Restack();
        };

        MouseDown += (_, _) => { _timer.Stop(); Close(); onClick?.Invoke(); };

        _timer.Interval = TimeSpan.FromSeconds(autoCloseSec);
        _timer.Tick    += (_, _) => { _timer.Stop(); Close(); };
        _timer.Start();
    }
}
