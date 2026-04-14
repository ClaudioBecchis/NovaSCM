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
                Workflows.Add(new WfRow(
                    el.TryGetProperty("id",          out var id)  ? id.GetInt32()         : 0,
                    el.TryGetProperty("name",        out var nm)  ? nm.GetString()  ?? "" : "",
                    el.TryGetProperty("description", out var ds)  ? ds.GetString()  ?? "" : "",
                    el.TryGetProperty("version",     out var ve)  ? ve.GetInt32()         : 1,
                    el.TryGetProperty("step_count",  out var sc)  ? sc.GetInt32()         : 0
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
                Steps.Add(new WfStepRow(
                    el.TryGetProperty("id",          out var id)  ? id.GetInt32()         : 0,
                    workflowId,
                    el.TryGetProperty("order",       out var or)  ? or.GetInt32()         : 0,
                    el.TryGetProperty("name",        out var nm)  ? nm.GetString()  ?? "" : "",
                    el.TryGetProperty("type",        out var tp)  ? tp.GetString()  ?? "" : "",
                    el.TryGetProperty("parameters",  out var pr)  ? pr.GetString()  ?? "" : "",
                    el.TryGetProperty("on_error",    out var oe)  ? oe.GetString()  ?? "stop" : "stop",
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
                Assignments.Add(new WfAssignRow(
                    el.TryGetProperty("id",             out var id) ? id.GetInt32()         : 0,
                    el.TryGetProperty("pc_name",        out var pc) ? pc.GetString()  ?? "" : "",
                    el.TryGetProperty("workflow_name",  out var wn) ? wn.GetString()  ?? "" : "",
                    el.TryGetProperty("workflow_id",    out var wi) ? wi.GetInt32()         : 0,
                    el.TryGetProperty("status",         out var st) ? st.GetString()  ?? "" : "",
                    el.TryGetProperty("progress",       out var pg) ? pg.GetInt32()         : 0,
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
