using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using NovaSCM.Commands;
using PolarisManager;

namespace NovaSCM.ViewModels;

/// <summary>
/// ViewModel per il tab Dashboard — metriche real-time, activity feed.
/// </summary>
public class DashboardViewModel : ViewModelBase
{
    private readonly NovaSCMApiService? _api;

    public DashboardViewModel() { }
    public DashboardViewModel(NovaSCMApiService? api) => _api = api;

    // ── Metriche ──
    private string _pcOnline = "0/0";
    public string PcOnline { get => _pcOnline; set => SetProperty(ref _pcOnline, value); }

    private string _wfRunning = "0";
    public string WfRunning { get => _wfRunning; set => SetProperty(ref _wfRunning, value); }

    private string _crOpen = "0";
    public string CrOpen { get => _crOpen; set => SetProperty(ref _crOpen, value); }

    private string _deviceCount = "0";
    public string DeviceCount { get => _deviceCount; set => SetProperty(ref _deviceCount, value); }

    private bool _isRefreshing;
    public bool IsRefreshing { get => _isRefreshing; set => SetProperty(ref _isRefreshing, value); }

    // ── Activity Feed ──
    public ObservableCollection<ActivityItem> Activities { get; } = new();

    // ── Comandi ──
    public ICommand RefreshCommand => new AsyncRelayCommand(RefreshAsync);

    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        try
        {
            if (_api != null)
            {
                var json = await _api.GetDashboardJsonAsync();
                if (json != null)
                {
                    var doc = JsonDocument.Parse(json);
                    var data = doc.RootElement;
                    CrOpen    = data.TryGetProperty("cr_open",    out var cr) ? cr.ToString() : "0";
                    WfRunning = data.TryGetProperty("wf_running", out var wf) ? wf.ToString() : "0";
                }
            }
        }
        catch { /* silenzioso — dashboard non critica */ }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>Aggiorna contatori da dati locali (device list, workflows, CRs).</summary>
    public void UpdateFromLocal(int onlineCount, int totalCount, int wfRunningCount, int crOpenCount)
    {
        PcOnline = $"{onlineCount}/{totalCount}";
        WfRunning = wfRunningCount.ToString();
        CrOpen = crOpenCount.ToString();
        DeviceCount = totalCount.ToString();
    }
}

public record ActivityItem(string Icon, string Description, string Timestamp);
