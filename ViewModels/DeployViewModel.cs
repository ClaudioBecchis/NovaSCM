using System.Collections.ObjectModel;
using System.Windows.Input;
using NovaSCM.Commands;

namespace NovaSCM.ViewModels;

/// <summary>
/// ViewModel per il tab Deploy OS — profili, autounattend, postinstall.
/// La generazione XML/PS1 resta nel code-behind per ora (logica complessa con 400+ righe).
/// </summary>
public class DeployViewModel : ViewModelBase
{
    // ── Profilo ──
    private string _profileName = "";
    public string ProfileName { get => _profileName; set => SetProperty(ref _profileName, value); }

    // ── Windows Edition ──
    private string _windowsEdition = "Windows 11 Pro";
    public string WindowsEdition { get => _windowsEdition; set => SetProperty(ref _windowsEdition, value); }

    // ── Locale ──
    private string _locale = "it-IT";
    public string Locale { get => _locale; set => SetProperty(ref _locale, value); }

    private string _timezone = "W. Europe Standard Time";
    public string Timezone { get => _timezone; set => SetProperty(ref _timezone, value); }

    // ── Computer ──
    private string _pcName = "";
    public string PcName { get => _pcName; set => SetProperty(ref _pcName, value); }

    // ── Domain ──
    private string _joinMode = "workgroup"; // workgroup, ad, aad
    public string JoinMode { get => _joinMode; set => SetProperty(ref _joinMode, value); }

    private string _domain = "";
    public string Domain { get => _domain; set => SetProperty(ref _domain, value); }

    private string _dcIp = "";
    public string DcIp { get => _dcIp; set => SetProperty(ref _dcIp, value); }

    private string _joinUser = "";
    public string JoinUser { get => _joinUser; set => SetProperty(ref _joinUser, value); }

    private string _joinPass = "";
    public string JoinPass { get => _joinPass; set => SetProperty(ref _joinPass, value); }

    // ── Account ──
    private string _adminPass = "";
    public string AdminPass { get => _adminPass; set => SetProperty(ref _adminPass, value); }

    private string _productKey = "";
    public string ProductKey { get => _productKey; set => SetProperty(ref _productKey, value); }

    // ── Packages ──
    public ObservableCollection<string> Packages { get; } = new();

    // ── Profiles List ──
    public ObservableCollection<string> ProfileNames { get; } = new();

    // ── Status ──
    private string _status = "";
    public string Status { get => _status; set => SetProperty(ref _status, value); }

    // ── Comandi ──
    public ICommand AddPackageCommand => new RelayCommand<string>(AddPackage);
    public ICommand RemovePackageCommand => new RelayCommand<string>(RemovePackage);

    public void AddPackage(string? package)
    {
        if (!string.IsNullOrWhiteSpace(package) && !Packages.Contains(package))
            Packages.Add(package.Trim());
    }

    public void RemovePackage(string? package)
    {
        if (package != null) Packages.Remove(package);
    }

    public void RefreshProfiles()
    {
        var dir = Services.ConfigService.ProfilesDir;
        ProfileNames.Clear();
        if (System.IO.Directory.Exists(dir))
        {
            foreach (var file in System.IO.Directory.GetFiles(dir, "*.json"))
                ProfileNames.Add(System.IO.Path.GetFileNameWithoutExtension(file));
        }
    }
}

/// <summary>RelayCommand tipizzato per MVVM.</summary>
public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke((T?)parameter) ?? true;
    public void Execute(object? parameter) => _execute((T?)parameter);
}
