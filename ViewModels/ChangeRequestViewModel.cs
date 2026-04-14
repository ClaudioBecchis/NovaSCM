using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using NovaSCM.Commands;
using PolarisManager;

namespace NovaSCM.ViewModels;

/// <summary>
/// ViewModel per il tab Change Request — creazione, tracking, status.
/// </summary>
public class ChangeRequestViewModel : ViewModelBase
{
    private readonly NovaSCMApiService? _api;

    public ChangeRequestViewModel() { }
    public ChangeRequestViewModel(NovaSCMApiService? api) => _api = api;

    // ── CR List ──
    public ObservableCollection<CrRow> ChangeRequests { get; } = new();

    // ── New CR Form ──
    private string _pcName = "";
    public string PcName { get => _pcName; set => SetProperty(ref _pcName, value); }

    private string _domain = "";
    public string Domain { get => _domain; set => SetProperty(ref _domain, value); }

    private string _ou = "";
    public string Ou { get => _ou; set => SetProperty(ref _ou, value); }

    private string _assignedUser = "";
    public string AssignedUser { get => _assignedUser; set => SetProperty(ref _assignedUser, value); }

    private string _notes = "";
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }

    private string _dcIp = "";
    public string DcIp { get => _dcIp; set => SetProperty(ref _dcIp, value); }

    private string _joinUser = "";
    public string JoinUser { get => _joinUser; set => SetProperty(ref _joinUser, value); }

    private string _joinPass = "";
    public string JoinPass { get => _joinPass; set => SetProperty(ref _joinPass, value); }

    private string _adminPass = "";
    public string AdminPass { get => _adminPass; set => SetProperty(ref _adminPass, value); }

    public ObservableCollection<string> Packages { get; } = new();

    // ── Selected CR ──
    private CrRow? _selectedCr;
    public CrRow? SelectedCr { get => _selectedCr; set => SetProperty(ref _selectedCr, value); }

    // ── Status ──
    private string _status = "";
    public string Status { get => _status; set => SetProperty(ref _status, value); }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    // ── Comandi ──
    public ICommand RefreshCommand => new AsyncRelayCommand(LoadListAsync);
    public ICommand CreateCommand => new AsyncRelayCommand(CreateAsync);

    public async Task LoadListAsync()
    {
        if (_api == null) return;
        IsLoading = true;
        try
        {
            var json = await _api.GetCrListJsonAsync(forceRefresh: true);
            var doc  = JsonDocument.Parse(json);
            ChangeRequests.Clear();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                ChangeRequests.Add(new CrRow(
                    el.TryGetProperty("id",            out var id)   ? id.GetInt32()         : 0,
                    el.TryGetProperty("pc_name",       out var pc)   ? pc.GetString()  ?? "" : "",
                    el.TryGetProperty("domain",        out var dom)  ? dom.GetString() ?? "" : "",
                    el.TryGetProperty("ou",            out var ou)   ? ou.GetString()  ?? "" : "",
                    el.TryGetProperty("assigned_user", out var au)   ? au.GetString()  ?? "" : "",
                    el.TryGetProperty("status",        out var st)   ? st.GetString()  ?? "" : "",
                    el.TryGetProperty("created_at",    out var ca)   ? ca.GetString()  ?? "" : "",
                    el.TryGetProperty("notes",         out var no)   ? no.GetString()  ?? "" : "",
                    el.TryGetProperty("last_seen",     out var ls)   ? ls.GetString()  ?? "" : ""
                ));
            }
        }
        catch (Exception ex) { Status = $"Errore: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    public async Task CreateAsync()
    {
        if (_api == null || string.IsNullOrWhiteSpace(PcName))
        {
            Status = "Nome PC obbligatorio";
            return;
        }
        try
        {
            await _api.PostCrAsync(new
            {
                pc_name       = PcName,
                domain        = Domain,
                ou            = Ou,
                assigned_user = AssignedUser,
                notes         = Notes,
                dc_ip         = DcIp,
                join_user     = JoinUser,
                join_pass     = JoinPass,
                admin_pass    = AdminPass,
                packages      = Packages.ToList()
            });
            Status = $"CR creata per {PcName}";
            PcName = ""; Notes = "";
            await LoadListAsync();
        }
        catch (Exception ex) { Status = $"Errore: {ex.Message}"; }
    }

    public async Task UpdateStatusAsync(int crId, string newStatus)
    {
        if (_api == null) return;
        try
        {
            await _api.SetCrStatusAsync(crId, newStatus);
            await LoadListAsync();
        }
        catch (Exception ex) { Status = $"Errore: {ex.Message}"; }
    }

    public async Task DeleteAsync(int crId)
    {
        if (_api == null) return;
        try
        {
            await _api.DeleteCrAsync(crId);
            await LoadListAsync();
        }
        catch (Exception ex) { Status = $"Errore: {ex.Message}"; }
    }

    public void AddPackage(string package)
    {
        if (!string.IsNullOrWhiteSpace(package) && !Packages.Contains(package))
            Packages.Add(package.Trim());
    }

    public void RemovePackage(string package)
    {
        Packages.Remove(package);
    }
}

public record CrRow(int Id, string PcName, string Domain, string Ou, string AssignedUser,
    string Status, string CreatedAt, string Notes, string LastSeen);
