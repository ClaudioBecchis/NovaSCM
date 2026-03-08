// NovaSCM v1.6.0
using System.Windows;
using System.Windows.Threading;

namespace PolarisManager;

public partial class UpdateToast : Window
{
    private readonly Action _onInstall;
    private readonly DispatcherTimer _autoClose = new() { Interval = TimeSpan.FromSeconds(12) };

    public UpdateToast(string version, string notes, Action onInstall)
    {
        InitializeComponent();
        _onInstall        = onInstall;
        TxtVersion.Text   = $"NovaSCM v{version}";
        TxtNotes.Text     = notes;
        TxtNotes.Visibility = string.IsNullOrEmpty(notes) ? Visibility.Collapsed : Visibility.Visible;

        // Posiziona in basso a destra
        Loaded += (_, _) => PositionBottomRight();

        _autoClose.Tick += (_, _) => { _autoClose.Stop(); Close(); };
        _autoClose.Start();
    }

    private void PositionBottomRight()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - 18;
        Top  = wa.Bottom - ActualHeight - 18;
    }

    private void BtnInstall_Click(object s, RoutedEventArgs e)
    {
        _autoClose.Stop();
        Close();
        _onInstall();
    }

    private void BtnClose_Click(object s, RoutedEventArgs e)
    {
        _autoClose.Stop();
        Close();
    }
}
