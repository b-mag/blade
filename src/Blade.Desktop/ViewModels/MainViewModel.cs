using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Blade.Core;
using Blade.Desktop.Infrastructure;
using Blade.Desktop.Services;
using Blade.Desktop.Views;

namespace Blade.Desktop.ViewModels;

public sealed record NavItem(string Group, string Title, string Glyph, object Page, string? Badge = null);

public sealed class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(8) };
    private SystemInfo? _system;
    private bool _connected;
    private string _connection = "Connecting to BLADE server…";
    private NavItem? _selected;
    private string _user = "j.alvarez";
    private string _role = Roles.Analyst;
    private bool _startingServer;

    public MainViewModel(ApiClient api, AppContent content)
    {
        Api = api;
        Content = content;
        Ask = new AskViewModel(this);
        Policy = new PolicyViewModel(this);
        Links = new LinkExplorerViewModel(this);
        Audit = new AuditViewModel(this);

        Nav.Add(new NavItem("PLATFORM", "Security & Governance", "", new HomePageViewModel(content.Home)));
        Nav.Add(new NavItem("PLATFORM", "Architecture & Deployment", "", new ArchitecturePageViewModel(content.Architecture)));
        Nav.Add(new NavItem("LIVE AI DEMO", "Ask BLADE", "", Ask, "LIVE"));
        Nav.Add(new NavItem("LIVE AI DEMO", "Policy Assistant", "", Policy, "LIVE"));
        Nav.Add(new NavItem("LIVE AI DEMO", "Link Explorer", "", Links, "LIVE"));
        Nav.Add(new NavItem("LIVE AI DEMO", "Audit Log", "", Audit, "LIVE"));
        Nav.Add(new NavItem("OPPORTUNITIES", "ROI Summary", "", new RoiSummaryPageViewModel(content.RoiSummary, content.Opportunities, this)));
        foreach (var o in content.Opportunities.OrderBy(o => o.RoiRank))
            Nav.Add(new NavItem("OPPORTUNITIES", o.Title, "", new OpportunityPageViewModel(o)));
        Selected = Nav[0];

        RefreshCommand = new AsyncCommand(RefreshSystemAsync);
        StartServerCommand = new RelayCommand(StartServer, () => !_connected && !_startingServer);
        _poll.Tick += async (_, _) => await RefreshSystemAsync();
        _poll.Start();
        _ = RefreshSystemAsync();

        MarkdownRenderer.CitationClicked += (kind, id) =>
        {
            switch (kind)
            {
                case "CASE": _ = OpenRecordAsync(id); break;
                case "POLICY": _ = ShowPolicySectionAsync(id); break;
                default: ShowInLinkExplorer(id); break;
            }
        };
    }

    public ApiClient Api { get; }
    public AppContent Content { get; }
    public AskViewModel Ask { get; }
    public PolicyViewModel Policy { get; }
    public LinkExplorerViewModel Links { get; }
    public AuditViewModel Audit { get; }
    public ObservableCollection<NavItem> Nav { get; } = [];
    public AsyncCommand RefreshCommand { get; }
    public RelayCommand StartServerCommand { get; }

    public NavItem? Selected { get => _selected; set { if (Set(ref _selected, value)) OnPropertyChanged(nameof(CurrentPage)); } }
    public object? CurrentPage => _selected?.Page;

    public SystemInfo? System { get => _system; private set { Set(ref _system, value); OnPropertyChanged(nameof(ProviderBadge)); OnPropertyChanged(nameof(IsLocal)); OnPropertyChanged(nameof(StatusLine)); } }
    public bool Connected { get => _connected; private set { Set(ref _connected, value); OnPropertyChanged(nameof(Disconnected)); } }
    public bool Disconnected => !_connected;
    public string Connection { get => _connection; private set => Set(ref _connection, value); }
    public bool IsLocal => _system?.Local ?? true;
    public string ProviderBadge => _system is null ? "OFFLINE" : _system.Local ? "LOCAL AI" : "CLOUD (POC ONLY)";
    public string StatusLine => _system is null ? Connection
        : $"Server {Api.BaseUrl} · {_system.ChatModel} + {_system.EmbeddingModel} via {_system.Provider} · models {(_system.ModelsWarm ? "warm" : "loading…")} · {_system.Incidents:N0} incidents, {_system.PolicySections} policy sections, {_system.Embeddings:N0} vectors · data as of {_system.DataAsOf:yyyy-MM-dd}";

    public string User { get => _user; set { if (Set(ref _user, value)) Api.User = value; } }
    public string Role
    {
        get => _role;
        set
        {
            if (!Set(ref _role, value)) return;
            Api.Role = value;
            foreach (var r in new[] { nameof(IsDeputy), nameof(IsAnalyst), nameof(IsSupervisor), nameof(IsAuditor) }) OnPropertyChanged(r);
            User = value switch { Roles.Deputy => "dep.harmon", Roles.Analyst => "j.alvarez", Roles.Supervisor => "sgt.whitaker", _ => "audit.fenton" };
        }
    }
    public bool IsDeputy { get => _role == Roles.Deputy; set { if (value) Role = Roles.Deputy; } }
    public bool IsAnalyst { get => _role == Roles.Analyst; set { if (value) Role = Roles.Analyst; } }
    public bool IsSupervisor { get => _role == Roles.Supervisor; set { if (value) Role = Roles.Supervisor; } }
    public bool IsAuditor { get => _role == Roles.Auditor; set { if (value) Role = Roles.Auditor; } }

    private async Task RefreshSystemAsync()
    {
        try
        {
            System = await Api.SystemAsync();
            Connected = true;
            Connection = "Connected";
            _startingServer = false;
        }
        catch (Exception)
        {
            Connected = false;
            System = null;
            Connection = _startingServer ? "Starting local BLADE server… (first start loads data and models, ~10–30 s)" : $"BLADE server not reachable at {Api.BaseUrl}.";
            OnPropertyChanged(nameof(StatusLine));
        }
    }

    private void StartServer()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Blade.slnx"))) dir = dir.Parent;
        if (dir is null) { MessageBox.Show("Could not locate the BLADE solution folder to start the server.", "BLADE"); return; }
        Process.Start(new ProcessStartInfo("dotnet", "run --project src/Blade.Server --no-launch-profile")
        {
            WorkingDirectory = dir.FullName,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Minimized,
        });
        _startingServer = true;
        Connection = "Starting local BLADE server… (first start loads data and models, ~10–30 s)";
        OnPropertyChanged(nameof(StatusLine));
    }

    // ------------------------------------------------------------------ cross-tab navigation
    public void Navigate(object page) => Selected = Nav.First(n => n.Page == page);

    public void ShowInLinkExplorer(string query)
    {
        Navigate(Links);
        Links.Query = query;
        Links.SearchCommand.Execute(null);
    }

    public void ShowGraph(GraphView graph)
    {
        Navigate(Links);
        Links.Load(graph);
    }

    public async Task OpenRecordAsync(string caseNumber)
    {
        try
        {
            var detail = await Api.IncidentAsync(caseNumber);
            new RecordWindow(detail, this) { Owner = Application.Current.MainWindow }.Show();
        }
        catch (ApiException ex) { MessageBox.Show(ex.Message, ex.Denied ? "Access denied" : "BLADE", MessageBoxButton.OK, ex.Denied ? MessageBoxImage.Warning : MessageBoxImage.Error); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "BLADE", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    public async Task ShowPolicySectionAsync(string section)
    {
        try
        {
            var s = await Api.PolicySectionAsync(section);
            new TextWindow($"Policy {s.SectionNumber} — {s.Heading}", $"Policy {s.PolicyNumber}: {s.PolicyTitle}", s.Text) { Owner = Application.Current.MainWindow }.Show();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "BLADE", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}

public sealed record HomePageViewModel(HomeContent Content);
public sealed record ArchitecturePageViewModel(ArchitectureContent Content);
public sealed record OpportunityPageViewModel(Opportunity Content);

public sealed class RoiSummaryPageViewModel(RoiSummary summary, List<Opportunity> opportunities, MainViewModel main)
{
    public RoiSummary Summary { get; } = summary;
    public List<Opportunity> Ranked { get; } = opportunities.OrderBy(o => o.RoiRank).ToList();
    public RelayCommand OpenCommand { get; } = new(o => main.Selected = main.Nav.First(n => n.Page is OpportunityPageViewModel p && ReferenceEquals(p.Content, o)));
}
