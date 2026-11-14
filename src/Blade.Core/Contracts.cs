namespace Blade.Core;

// ============================================================================
// API contracts shared by Blade.Server and Blade.Desktop.
// ============================================================================

/// <summary>
/// Structured search filter produced by the LLM query planner (or edited by the user).
/// The model only fills in relative values (lookback months, hours); dates are computed in code.
/// </summary>
public sealed record SearchFilter
{
    public List<string> OffenseTypes { get; init; } = [];
    public List<string> Districts { get; init; } = [];
    public List<string> PremiseTypes { get; init; } = [];
    public List<string> MoTags { get; init; } = [];
    public int LookbackMonths { get; init; }
    public int SinceMonth { get; init; }
    public int HourFrom { get; init; } = -1;
    public int HourTo { get; init; } = -1;
    public string SemanticQuery { get; init; } = "";

    /// <summary>Computed by the server from LookbackMonths/SinceMonth. Null = no lower bound.</summary>
    public DateOnly? DateFrom { get; init; }
    public DateOnly? DateTo { get; init; }
}

public sealed record FilterChip(string Label, string Value);

public sealed record AskRequest(string Question, SearchFilter? FilterOverride = null);

public sealed record IncidentHit(
    string CaseNumber, string OffenseType, string OffenseDescription, string District, string PremiseType,
    string Location, DateTime OccurredFrom, DateTime ReportedAt, string MoTags, string Status, string Sensitivity,
    double Relevance, string Snippet, bool InSeries, string? SeriesReason);

public sealed record SeriesMember(string CaseNumber, string Reason);

public sealed record SeriesFinding(List<SeriesMember> Members, List<string> SharedMoTags, string Summary);

/// <summary>A deterministic link discovered in the entity graph (vehicle, person, phone, location or bridge).</summary>
public sealed record LinkFinding(string Kind, string EntityId, string EntityLabel, string Summary, List<string> CaseNumbers, List<string> Evidence, List<string> SourceIds);

public sealed record Citation(string Id, string Kind, bool Verified);

public sealed record TraceStage(string Name, long Ms, string? Detail = null);

public sealed record RetrievedItem(string Id, string Kind, double Score, string Note);

/// <summary>Everything the "Under the Hood" panel shows.</summary>
public sealed record AiTrace
{
    public string Provider { get; init; } = "";
    public string Model { get; init; } = "";
    public bool Local { get; init; }
    public List<TraceStage> Stages { get; init; } = [];
    public string? PlannerSystemPrompt { get; init; }
    public string? PlannerRawOutput { get; init; }
    public string? AnswerSystemPrompt { get; init; }
    public string? AnswerUserPrompt { get; init; }
    public string? AnswerRawOutput { get; init; }
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public List<RetrievedItem> Retrieved { get; init; } = [];
    public List<string> SafetyChecks { get; init; } = [];
}

public sealed record GraphNodeDto(string Id, string Kind, string Label, string? Detail = null, bool Focus = false);
public sealed record GraphEdgeDto(string From, string To, string Type, string Label);
public sealed record GraphView(List<GraphNodeDto> Nodes, List<GraphEdgeDto> Edges, string? Title = null, int Withheld = 0);

public sealed record AskResponse
{
    public GraphView? Graph { get; init; }
    public string Answer { get; init; } = "";
    public SearchFilter Filter { get; init; } = new();
    public List<FilterChip> Chips { get; init; } = [];
    public int TotalMatches { get; init; }
    public int Withheld { get; init; }
    public List<IncidentHit> Results { get; init; } = [];
    public SeriesFinding? Series { get; init; }
    public List<LinkFinding> Links { get; init; } = [];
    public List<Citation> Citations { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public AiTrace Trace { get; init; } = new();
    public long AuditId { get; init; }
}

public sealed record PolicyRequest(string Question);

public sealed record PolicySectionHit(string SectionNumber, string PolicyNumber, string PolicyTitle, string Heading, string Text, double Score);

public sealed record PolicyResponse
{
    public string Answer { get; init; } = "";
    public List<PolicySectionHit> Sections { get; init; } = [];
    public List<IncidentHit> RelatedCases { get; init; } = [];
    public List<Citation> Citations { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public AiTrace Trace { get; init; } = new();
    public long AuditId { get; init; }
}

public sealed record PersonRef(int Id, string Name, string Role);
public sealed record VehicleRef(int Id, string Plate, string Description, string Role);

public sealed record IncidentDetail(
    string CaseNumber, string OffenseType, string OffenseDescription, string District, string PremiseType, string Location,
    DateTime OccurredFrom, DateTime? OccurredTo, DateTime ReportedAt, string MoTags, string Status, string Sensitivity,
    string Deputy, string Narrative, List<PersonRef> Persons, List<VehicleRef> Vehicles, List<string> Property, string? CadCallNumber);

public sealed record AuditEntry(
    long Id, DateTime TimestampUtc, string User, string Role, string Workstation, string Action, string? Question,
    string? Detail, string? RecordsReturned, string? RecordsCited, string? Provider, string? Model, long LatencyMs,
    int PromptTokens, int CompletionTokens, string Outcome, string Hash);

public sealed record UsageRow(string User, string Role, int AiQueries, int RecordViews, int Denied, long Tokens, DateTime LastActivityUtc);

public sealed record AuditVerifyResult(bool Valid, long Entries, long? FirstInvalidId, string Message);

public sealed record SystemInfo(
    string Agency, string Product, string DataNotice, string Provider, string ChatModel, string EmbeddingModel, bool Local,
    bool ModelsWarm, DateOnly DataAsOf, int Incidents, int PolicySections, int Embeddings, string ServerVersion);

public static class Roles
{
    public const string Deputy = "DEPUTY";
    public const string Analyst = "ANALYST";
    public const string Supervisor = "SUPERVISOR";
    public const string Auditor = "AUDITOR";
    public static readonly string[] All = [Deputy, Analyst, Supervisor, Auditor];

    /// <summary>Record sensitivities each role may retrieve. Enforced before anything reaches a model.</summary>
    public static string[] AllowedSensitivities(string role) => role switch
    {
        Deputy => ["NORMAL"],
        Analyst => ["NORMAL", "JUVENILE"],
        Supervisor => ["NORMAL", "JUVENILE", "RESTRICTED"],
        Auditor => ["NORMAL"],
        _ => ["NORMAL"],
    };

    public static bool CanViewAudit(string role) => role is Supervisor or Auditor;
}

public static class BladeHeaders
{
    public const string User = "X-Blade-User";
    public const string Role = "X-Blade-Role";
    public const string Workstation = "X-Blade-Workstation";
}

/// <summary>
/// One line of a streamed answer (newline-delimited JSON). Order: <c>context</c> (everything except the answer text),
/// then <c>delta</c> chunks, an optional <c>reset</c> if the server regenerates the answer, and finally <c>done</c>
/// with the complete, guard-checked response, or <c>error</c>.
/// </summary>
public sealed record StreamEvent<T>(string Type, string? Text = null, T? Response = null, string? Error = null) where T : class;

public static class StreamEventTypes
{
    public const string Context = "context";
    public const string Delta = "delta";
    public const string Reset = "reset";
    public const string Done = "done";
    public const string Error = "error";
}
