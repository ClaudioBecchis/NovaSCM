using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using NovaSCM.Commands;
using PolarisManager;

namespace NovaSCM.ViewModels;

/// <summary>
/// ViewModel per il tab Workflow — CRUD workflow, step, assegnazioni.
/// </summary>
public class WorkflowViewModel : ViewModelBase
{
    private readonly NovaSCMApiService? _api;

    public WorkflowViewModel() { }
    public WorkflowViewModel(NovaSCMApiService? api) => _api = api;

    // ── Collections ──
    public ObservableCollection<WfRow> Workflows { get; } = new();
    public ObservableCollection<WfStepRow> Steps { get; } = new();
    public ObservableCollection<WfAssignRow> Assignments { get; } = new();

    // ── Selected ──
    private WfRow? _selectedWorkflow;
    public WfRow? SelectedWorkflow
    {
        get => _selectedWorkflow;
        set
        {
            if (SetProperty(ref _selectedWorkflow, value) && value != null)
                _ = LoadStepsAsync(value.Id);
        }
    }

    private WfStepRow? _selectedStep;
    public WfStepRow? SelectedStep { get => _selectedStep; set => SetProperty(ref _selectedStep, value); }

    private int _selectedWorkflowId;
    public int SelectedWorkflowId { get => _selectedWorkflowId; set => SetProperty(ref _selectedWorkflowId, value); }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private string _status = "";
    public string Status { get => _status; set => SetProperty(ref _status, value); }

    // ── Comandi ──
    public ICommand RefreshCommand => new AsyncRelayCommand(LoadAllAsync);
    public ICommand RefreshAssignmentsCommand => new AsyncRelayCommand(LoadAssignmentsAsync);

    public async Task LoadAllAsync()
    {
        if (_api == null) return;
        IsLoading = true;
        try
        {
            var json = await _api.GetWorkflowsJsonAsync();
            var doc  = JsonDocument.Parse(json);
            Workflows.Clear();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                // BUG: leggeva chiavi inglesi (name/description/version/step_count)
                // ma il server (SELECT * FROM workflows) restituisce le colonne DB
                // in italiano (nome/descrizione/versione); step_count non esiste
                // affatto come colonna — ogni riga risultava sempre con nome/
                // descrizione vuoti, versione sempre 1, step_count sempre 0.
                Workflows.Add(new WfRow(
                    el.TryGetProperty("id",          out var id)  ? id.GetInt32()         : 0,
                    el.TryGetProperty("nome",        out var nm)  ? nm.GetString()  ?? "" : "",
                    el.TryGetProperty("descrizione", out var ds)  ? ds.GetString()  ?? "" : "",
                    el.TryGetProperty("versione",    out var ve)  ? ve.GetInt32()         : 1,
                    0 // step_count: non restituito da GET /api/workflows, richiederebbe una query separata per riga
                ));
            }
            await LoadAssignmentsAsync();
        }
        catch (Exception ex) { Status = $"Errore: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    public async Task LoadStepsAsync(int workflowId)
    {
        if (_api == null) return;
        SelectedWorkflowId = workflowId;
        try
        {
            var json   = await _api.GetWorkflowDetailJsonAsync(workflowId);
            var doc    = JsonDocument.Parse(json);
            var stepsEl = doc.RootElement.TryGetProperty("steps", out var s) ? s : doc.RootElement;
            Steps.Clear();
            foreach (var el in stepsEl.EnumerateArray())
            {
                // BUG: leggeva chiavi inglesi (order/name/type/parameters/on_error)
                // ma il server (SELECT * FROM workflow_steps) restituisce le colonne
                // DB in italiano (ordine/nome/tipo/parametri/su_errore) — ogni step
                // risultava con nome/tipo/parametri vuoti e su_errore sempre "stop".
                Steps.Add(new WfStepRow(
                    el.TryGetProperty("id",          out var id)  ? id.GetInt32()         : 0,
                    workflowId,
                    el.TryGetProperty("ordine",      out var or)  ? or.GetInt32()         : 0,
                    el.TryGetProperty("nome",        out var nm)  ? nm.GetString()  ?? "" : "",
                    el.TryGetProperty("tipo",        out var tp)  ? tp.GetString()  ?? "" : "",
                    el.TryGetProperty("parametri",   out var pr)  ? pr.GetString()  ?? "" : "",
                    el.TryGetProperty("su_errore",   out var oe)  ? oe.GetString()  ?? "stop" : "stop",
                    el.TryGetProperty("platform",    out var pl)  ? pl.GetString()  ?? "" : ""
                ));
            }
        }
        catch (Exception ex) { Status = $"Errore caricamento step: {ex.Message}"; }
    }

    public async Task LoadAssignmentsAsync()
    {
        if (_api == null) return;
        try
        {
            var json = await _api.GetPcWorkflowsJsonAsync();
            var doc  = JsonDocument.Parse(json);
            Assignments.Clear();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                // BUG: leggeva "workflow_name" ma il server restituisce
                // "workflow_nome" (alias SQL w.nome AS workflow_nome) — nome
                // workflow sempre vuoto. "progress" non esiste come colonna in
                // pc_workflows, non è mai stato restituito dal server: resta 0
                // come placeholder finché non verrà eventualmente implementato
                // lato server (es. done/total step).
                Assignments.Add(new WfAssignRow(
                    el.TryGetProperty("id",             out var id) ? id.GetInt32()         : 0,
                    el.TryGetProperty("pc_name",        out var pc) ? pc.GetString()  ?? "" : "",
                    el.TryGetProperty("workflow_nome",  out var wn) ? wn.GetString()  ?? "" : "",
                    el.TryGetProperty("workflow_id",    out var wi) ? wi.GetInt32()         : 0,
                    el.TryGetProperty("status",         out var st) ? st.GetString()  ?? "" : "",
                    0, // progress: non presente nello schema pc_workflows
                    el.TryGetProperty("assigned_at",    out var aa) ? aa.GetString()  ?? "" : "",
                    el.TryGetProperty("last_seen",      out var ls) ? ls.GetString()  ?? "" : ""
                ));
            }
        }
        catch (Exception ex) { Status = $"Errore assegnazioni: {ex.Message}"; }
    }
}

// Record per le righe — da spostare in Models/ se serve
public record WfRow(int Id, string Nome, string Descrizione, int Versione, int StepCount);
public record WfStepRow(int Id, int WorkflowId, int Ordine, string Nome, string Tipo, string Parametri, string SuErrore, string Platform);
public record WfAssignRow(int Id, string PcName, string WorkflowNome, int WorkflowId, string Status, int Progress, string AssignedAt, string LastSeen);
