using PolarisManager;

namespace NovaSCM.ViewModels;

/// <summary>
/// ViewModel principale — coordina tutti i sub-ViewModel dei tab.
/// Usato come DataContext della MainWindow.
/// </summary>
public class MainViewModel : ViewModelBase
{
    // ── Sub-ViewModels (uno per tab) ──
    public NetworkViewModel Network { get; }
    public DashboardViewModel Dashboard { get; }
    public SettingsViewModel Settings { get; }
    public WorkflowViewModel Workflow { get; }
    public ChangeRequestViewModel ChangeRequest { get; }
    public DeployViewModel Deploy { get; }
    public ProxmoxViewModel Proxmox { get; }

    // ── Stato globale ──
    private string _statusMessage = "Pronto";
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    private bool _isDarkTheme = true;
    public bool IsDarkTheme { get => _isDarkTheme; set => SetProperty(ref _isDarkTheme, value); }

    private bool _isNavCollapsed;
    public bool IsNavCollapsed { get => _isNavCollapsed; set => SetProperty(ref _isNavCollapsed, value); }

    private bool _isLogVisible;
    public bool IsLogVisible { get => _isLogVisible; set => SetProperty(ref _isLogVisible, value); }

    private int _selectedTabIndex;
    public int SelectedTabIndex { get => _selectedTabIndex; set => SetProperty(ref _selectedTabIndex, value); }

    public MainViewModel()
    {
        Network = new NetworkViewModel();
        Dashboard = new DashboardViewModel();
        Settings = new SettingsViewModel();
        Workflow = new WorkflowViewModel();
        ChangeRequest = new ChangeRequestViewModel();
        Deploy = new DeployViewModel();
        Proxmox = new ProxmoxViewModel();
    }

    public MainViewModel(NovaSCMApiService? api)
    {
        Network = new NetworkViewModel();
        Dashboard = new DashboardViewModel(api);
        Settings = new SettingsViewModel();
        Workflow = new WorkflowViewModel(api);
        ChangeRequest = new ChangeRequestViewModel(api);
        Deploy = new DeployViewModel();
        Proxmox = new ProxmoxViewModel();
    }

    /// <summary>Inizializza tutti i sub-ViewModel (carica config, dati iniziali).</summary>
    public void Initialize()
    {
        Settings.Load();
        Deploy.RefreshProfiles();
    }
}
