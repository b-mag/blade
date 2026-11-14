using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Blade.Core;
using Blade.Desktop.Infrastructure;
using Blade.Desktop.Services;

namespace Blade.Desktop.ViewModels;

/// <summary>Shared busy/elapsed/error/answer handling for the live AI tabs.</summary>
public abstract class AiPageViewModel : ObservableObject
{
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Stopwatch _sw = new();
    private bool _busy;
    private string? _error;
    private FlowDocument? _answer;
    private AiTrace? _trace;
    private bool _showTrace;
    private string _question = "";

    protected AiPageViewModel(MainViewModel main)
    {
        Main = main;
        _tick.Tick += (_, _) => OnPropertyChanged(nameof(Elapsed));
    }

    public MainViewModel Main { get; }
    public string Question { get => _question; set => Set(ref _question, value); }
    public bool IsBusy { get => _busy; private set { Set(ref _busy, value); OnPropertyChanged(nameof(BusyText)); } }
    public string Elapsed => $"{_sw.Elapsed.TotalSeconds:F1} s";
    public string BusyText => $"Working on {Main.System?.ChatModel ?? "the model"} via {Main.System?.Provider ?? "server"} — nothing leaves this network.";
    public string? Error { get => _error; protected set => Set(ref _error, value); }
    public FlowDocument? Answer { get => _answer; protected set => Set(ref _answer, value); }
    public AiTrace? Trace { get => _trace; protected set { Set(ref _trace, value); OnPropertyChanged(nameof(HasTrace)); } }
    public bool HasTrace => _trace is not null;
    public bool ShowTrace { get => _showTrace; set => Set(ref _showTrace, value); }
    public ObservableCollection<string> Warnings { get; } = [];
    public RelayCommand UseSampleCommand => new(q => Question = (string)q!);

    protected async Task RunAsync(Func<Task> work)
    {
        Error = null;
        IsBusy = true;
        _sw.Restart();
        _tick.Start();
        try { await work(); }
        catch (ApiException ex) { Error = ex.Denied ? $"⛔ {ex.Message}" : ex.Message; }
        catch (Exception ex) { Error = $"Could not reach the BLADE server: {ex.Message}"; }
        finally
        {
            _sw.Stop();
            _tick.Stop();
            OnPropertyChanged(nameof(Elapsed));
            IsBusy = false;
        }
    }

    protected static FlowDocument Render(string markdown) => MarkdownRenderer.Render(markdown,
        (Brush)App.Current.Resources["Text"], (Brush)App.Current.Resources["Accent"], (Brush)App.Current.Resources["Danger"]);

    protected void SetWarnings(IEnumerable<string> w)
    {
        Warnings.Clear();
        foreach (var x in w) Warnings.Add(x);
    }
}

public sealed class AskViewModel : AiPageViewModel
{
    private AskResponse? _response;
    private bool _editFilter;
    private string _lookback = "", _hourFrom = "", _hourTo = "", _semantic = "";

    public AskViewModel(MainViewModel main) : base(main)
    {
        Question = Samples[0];
        AskCommand = new AsyncCommand(() => RunAsync(() => SendAsync(null)), () => !string.IsNullOrWhiteSpace(Question) && !IsBusy);
        RerunCommand = new AsyncCommand(() => RunAsync(() => SendAsync(BuildFilter())), () => _response is not null && !IsBusy);
        OpenRecordCommand = new RelayCommand(c => { if (c is string s) _ = Main.OpenRecordAsync(s); else if (c is IncidentHit h) _ = Main.OpenRecordAsync(h.CaseNumber); });
        OpenGraphCommand = new RelayCommand(() => { if (_response?.Graph is { } g) Main.ShowGraph(g); }, () => _response?.Graph is not null);
        ExploreCommand = new RelayCommand(id => Main.ShowInLinkExplorer((string)id!));
    }

    public string[] Samples { get; } =
    [
        "Show similar burglaries in the north district last 18 months involving forced entry on commercial buildings after 10pm",
        "Any catalytic converter thefts in the east or south districts this year?",
        "Domestic violence incidents involving a knife in the last year",
        "Residential burglaries in the west district during the day in the past 6 months",
    ];

    public AsyncCommand AskCommand { get; }
    public AsyncCommand RerunCommand { get; }
    public RelayCommand OpenRecordCommand { get; }
    public RelayCommand OpenGraphCommand { get; }
    public RelayCommand ExploreCommand { get; }

    public AskResponse? Response { get => _response; private set { Set(ref _response, value); OnPropertyChanged(nameof(HasResponse)); OnPropertyChanged(nameof(Summary)); } }
    public bool HasResponse => _response is not null;
    public string Summary => _response is null ? "" :
        $"{_response.TotalMatches} matching incidents{(_response.Withheld > 0 ? $" · {_response.Withheld} withheld for your role" : "")} · {_response.Citations.Count(c => c.Verified)} verified citations{(_response.Citations.Any(c => !c.Verified) ? $" · {_response.Citations.Count(c => !c.Verified)} UNVERIFIED" : "")} · audit #{_response.AuditId}";

    // Filter editor ("human in the loop": the user can correct the model's interpretation)
    public bool EditFilter { get => _editFilter; set => Set(ref _editFilter, value); }
    public ObservableCollection<Choice> Offenses { get; } = new(Vocabulary.OffenseTypes.Select(v => new Choice(v)));
    public ObservableCollection<Choice> Districts { get; } = new(Vocabulary.Districts.Select(v => new Choice(v)));
    public ObservableCollection<Choice> Premises { get; } = new(Vocabulary.PremiseTypes.Select(v => new Choice(v)));
    public ObservableCollection<Choice> MoTags { get; } = new(Vocabulary.MoTags.Select(v => new Choice(v)));
    public string LookbackMonths { get => _lookback; set => Set(ref _lookback, value); }
    public string HourFrom { get => _hourFrom; set => Set(ref _hourFrom, value); }
    public string HourTo { get => _hourTo; set => Set(ref _hourTo, value); }
    public string SemanticQuery { get => _semantic; set => Set(ref _semantic, value); }

    private async Task SendAsync(SearchFilter? filter)
    {
        var r = await Main.Api.AskAsync(new AskRequest(Question, filter));
        Response = r;
        Answer = Render(r.Answer);
        Trace = r.Trace;
        SetWarnings(r.Warnings);
        LoadFilter(r.Filter);
    }

    private void LoadFilter(SearchFilter f)
    {
        foreach (var c in Offenses) c.Selected = f.OffenseTypes.Contains(c.Value);
        foreach (var c in Districts) c.Selected = f.Districts.Contains(c.Value);
        foreach (var c in Premises) c.Selected = f.PremiseTypes.Contains(c.Value);
        foreach (var c in MoTags) c.Selected = f.MoTags.Contains(c.Value);
        LookbackMonths = f.DateFrom is { } from && f.DateTo is { } to ? (((to.Year - from.Year) * 12) + to.Month - from.Month).ToString() : "";
        HourFrom = f.HourFrom >= 0 ? f.HourFrom.ToString() : "";
        HourTo = f.HourTo >= 0 ? f.HourTo.ToString() : "";
        SemanticQuery = f.SemanticQuery;
    }

    private SearchFilter BuildFilter() => new()
    {
        OffenseTypes = Offenses.Where(c => c.Selected).Select(c => c.Value).ToList(),
        Districts = Districts.Where(c => c.Selected).Select(c => c.Value).ToList(),
        PremiseTypes = Premises.Where(c => c.Selected).Select(c => c.Value).ToList(),
        MoTags = MoTags.Where(c => c.Selected).Select(c => c.Value).ToList(),
        LookbackMonths = int.TryParse(LookbackMonths, out var m) ? m : 0,
        HourFrom = int.TryParse(HourFrom, out var hf) ? hf : -1,
        HourTo = int.TryParse(HourTo, out var ht) ? ht : -1,
        SemanticQuery = SemanticQuery,
    };
}

public sealed class PolicyViewModel : AiPageViewModel
{
    private PolicyResponse? _response;

    public PolicyViewModel(MainViewModel main) : base(main)
    {
        Question = Samples[0];
        AskCommand = new AsyncCommand(() => RunAsync(async () =>
        {
            var r = await Main.Api.PolicyAsync(new PolicyRequest(Question));
            Response = r;
            Answer = Render(r.Answer);
            Trace = r.Trace;
            SetWarnings(r.Warnings);
        }), () => !string.IsNullOrWhiteSpace(Question) && !IsBusy);
        OpenRecordCommand = new RelayCommand(c => { if (c is IncidentHit h) _ = Main.OpenRecordAsync(h.CaseNumber); });
    }

    public string[] Samples { get; } =
    [
        "What does policy say about use of force with a knife threat when the subject is non-compliant?",
        "Can I use AI to help write my report narrative?",
        "What is our policy on vehicle pursuits for a stolen car?",
        "When must I activate my body-worn camera, and can I review footage before writing my report?",
        "What has to be redacted before we release a report under a public records request?",
    ];

    public AsyncCommand AskCommand { get; }
    public RelayCommand OpenRecordCommand { get; }
    public PolicyResponse? Response { get => _response; private set { Set(ref _response, value); OnPropertyChanged(nameof(HasResponse)); OnPropertyChanged(nameof(Summary)); } }
    public bool HasResponse => _response is not null;
    public string Summary => _response is null ? "" :
        $"{_response.Sections.Count} sections retrieved · {_response.Citations.Count(c => c.Verified)} verified citations{(_response.Citations.Any(c => !c.Verified) ? " · UNVERIFIED citations present" : "")} · audit #{_response.AuditId}";
}

public sealed class LinkExplorerViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private string _query = "418KTV";
    private GraphView? _graph;
    private GraphNodeDto? _selectedNode;
    private string? _error;
    private bool _busy;

    public LinkExplorerViewModel(MainViewModel main)
    {
        _main = main;
        SearchCommand = new AsyncCommand(async () =>
        {
            Error = null; IsBusy = true;
            try { Load(await _main.Api.GraphAsync(Query)); }
            catch (Exception ex) { Error = ex.Message; }
            finally { IsBusy = false; }
        }, () => !string.IsNullOrWhiteSpace(Query) && !IsBusy);
        ExpandCommand = new AsyncCommand(async () =>
        {
            if (_selectedNode is null || _graph is null) return;
            Error = null; IsBusy = true;
            try { Load(Merge(_graph, await _main.Api.GraphAsync(_selectedNode.Id, 1))); }
            catch (Exception ex) { Error = ex.Message; }
            finally { IsBusy = false; }
        }, () => _selectedNode is not null && !IsBusy);
        OpenCommand = new RelayCommand(() =>
        {
            if (_selectedNode?.Kind == "INCIDENT") _ = _main.OpenRecordAsync(_selectedNode.Id[2..]);
        }, () => _selectedNode?.Kind == "INCIDENT");
    }

    public string Query { get => _query; set => Set(ref _query, value); }
    public GraphView? Graph { get => _graph; private set { Set(ref _graph, value); OnPropertyChanged(nameof(Stats)); } }
    public GraphNodeDto? SelectedNode { get => _selectedNode; set { Set(ref _selectedNode, value); OnPropertyChanged(nameof(SelectedEdges)); } }
    public IEnumerable<string> SelectedEdges => _graph is null || _selectedNode is null ? []
        : _graph.Edges.Where(e => e.From == _selectedNode.Id || e.To == _selectedNode.Id)
            .Select(e => $"{e.Label} → {_graph.Nodes.FirstOrDefault(n => n.Id == (e.From == _selectedNode.Id ? e.To : e.From))?.Label.Replace('\n', ' ')}");
    public string Stats => _graph is null ? "" : $"{_graph.Title} · {_graph.Nodes.Count} entities · {_graph.Edges.Count} relationships{(_graph.Withheld > 0 ? $" · {_graph.Withheld} restricted links hidden for your role" : "")}";
    public string? Error { get => _error; private set => Set(ref _error, value); }
    public bool IsBusy { get => _busy; private set => Set(ref _busy, value); }
    public AsyncCommand SearchCommand { get; }
    public AsyncCommand ExpandCommand { get; }
    public RelayCommand OpenCommand { get; }
    public string[] Samples { get; } = ["418KTV", "Travis Kessler", "Cody Mercer", "Jaylen Ortega", "1418 Larkspur"];
    public RelayCommand SampleCommand => new(q => { Query = (string)q!; SearchCommand.Execute(null); });

    public void Load(GraphView g)
    {
        Graph = g;
        SelectedNode = g.Nodes.FirstOrDefault(n => n.Focus);
    }

    private static GraphView Merge(GraphView a, GraphView b)
    {
        var nodes = a.Nodes.ToDictionary(n => n.Id);
        foreach (var n in b.Nodes) nodes.TryAdd(n.Id, n with { Focus = false });
        var edges = a.Edges.Concat(b.Edges).DistinctBy(e => (e.From, e.To, e.Type)).ToList();
        return new GraphView([.. nodes.Values], edges, a.Title, a.Withheld + b.Withheld);
    }
}

public sealed class AuditViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private string? _error;
    private AuditVerifyResult? _verify;
    private string _filterUser = "", _filterAction = "";

    public AuditViewModel(MainViewModel main)
    {
        _main = main;
        RefreshCommand = new AsyncCommand(RefreshAsync);
        VerifyCommand = new AsyncCommand(async () =>
        {
            try { Verify = await _main.Api.VerifyAsync(); Error = null; }
            catch (Exception ex) { Error = ex.Message; }
        });
    }

    public ObservableCollection<AuditEntry> Entries { get; } = [];
    public ObservableCollection<UsageRow> Usage { get; } = [];
    public AuditVerifyResult? Verify { get => _verify; private set => Set(ref _verify, value); }
    public string? Error { get => _error; private set => Set(ref _error, value); }
    public string FilterUser { get => _filterUser; set => Set(ref _filterUser, value); }
    public string FilterAction { get => _filterAction; set => Set(ref _filterAction, value); }
    public string[] Actions { get; } = ["", "ASK", "POLICY_QUERY", "GRAPH_QUERY", "RECORD_VIEW", "AUDIT_VIEW"];
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand VerifyCommand { get; }

    private async Task RefreshAsync()
    {
        try
        {
            var entries = await _main.Api.AuditAsync(FilterUser, FilterAction);
            var usage = await _main.Api.UsageAsync();
            Entries.Clear(); foreach (var e in entries) Entries.Add(e);
            Usage.Clear(); foreach (var u in usage) Usage.Add(u);
            Error = null;
        }
        catch (ApiException ex) when (ex.Denied)
        {
            Entries.Clear(); Usage.Clear();
            Error = $"⛔ {ex.Message} Switch the role to SUPERVISOR or AUDITOR to review the audit trail.";
        }
        catch (Exception ex) { Error = ex.Message; }
    }
}
