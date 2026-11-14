using Blade.Core;
using Blade.DataGen.Generation;
using Blade.Server.Answer;
using Blade.Server.Audit;
using Blade.Server.Data;
using Blade.Server.Graph;
using Blade.Server.Search;
using Microsoft.Data.Sqlite;
using static Blade.Tests.DataGenFixture;

namespace Blade.Tests;

public sealed class PlannerAndGuardTests
{
    private static readonly DateOnly Today = new(2026, 11, 14);

    [Fact]
    public void Planner_output_is_normalized_and_dates_are_computed_in_code()
    {
        var raw = QueryPlanner.Parse("""
            {"offense_types":["BURGLARY","BURGLARY","ARSON"],"districts":["north"],"lookback_months":18,"since_month":0,
             "hour_from":22,"hour_to":6,"premise_types":["COMMERCIAL"],"mo_tags":["FORCED_ENTRY","FORCED_ENTRY","FORCED_ENTRY"],"semantic_query":" pry rear door "}
            """);
        var f = QueryPlanner.Normalize(raw, Today);

        Assert.Equal(["BURGLARY"], f.OffenseTypes);        // duplicate removed, invented enum dropped
        Assert.Equal(["NORTH"], f.Districts);              // case-normalized
        Assert.Equal(["FORCED_ENTRY"], f.MoTags);          // runaway repetition collapsed
        Assert.Equal(new DateOnly(2025, 5, 14), f.DateFrom);
        Assert.Equal(Today, f.DateTo);
        Assert.Equal((22, 6), (f.HourFrom, f.HourTo));
        Assert.Equal("pry rear door", f.SemanticQuery);
    }

    [Theory]
    [InlineData(0, 6, 2026, 6, 1)]   // "since June" in November → this year
    [InlineData(0, 12, 2025, 12, 1)] // "since December" in November → last year
    [InlineData(0, 1, 2026, 1, 1)]   // "this year"
    public void Since_month_resolves_to_the_most_recent_past_month(int lookback, int since, int y, int m, int d)
    {
        var f = QueryPlanner.Normalize(new SearchFilter { LookbackMonths = lookback, SinceMonth = since }, Today);
        Assert.Equal(new DateOnly(y, m, d), f.DateFrom);
    }

    [Fact]
    public void Invalid_hours_disable_the_time_filter()
    {
        var f = QueryPlanner.Normalize(new SearchFilter { HourFrom = 22, HourTo = -1 }, Today);
        Assert.Equal((-1, -1), (f.HourFrom, f.HourTo));
    }

    [Fact]
    public void Citation_guard_flags_identifiers_the_model_was_not_given()
    {
        var allowed = new HashSet<string> { "25-002136", "FI-26-00264" };
        var g = CitationGuard.Check("Van seen [25-002136] and on [FI-26-00264]; also see [24-009999] and [PT-FP-10147].", allowed);

        Assert.Equal(["24-009999", "PT-FP-10147"], g.Unverified.Order().ToList());
        Assert.Contains("24-009999 ⚠UNVERIFIED", g.Text);
        Assert.Contains(g.Citations, c => c is { Id: "25-002136", Verified: true, Kind: "CASE" });
        Assert.Contains(g.Citations, c => c is { Id: "FI-26-00264", Kind: "FIELD_INTERVIEW" });
    }

    [Fact]
    public void Citation_guard_checks_policy_sections()
    {
        var g = CitationGuard.Check("Keep distance [Policy 300.3.6]. Shoot at cars [Policy 300.9.9].", new HashSet<string>(), new HashSet<string> { "300.3.6" });
        Assert.Equal(["300.9.9"], g.Unverified);
        Assert.Contains(g.Citations, c => c is { Id: "300.3.6", Verified: true, Kind: "POLICY" });
    }

    [Fact]
    public void Pii_redactor_masks_license_and_ssn_patterns()
    {
        var (text, n) = PiiRedactor.Redact("DL K12345678 and SSN 123-45-6789 for case 25-002136");
        Assert.Equal(2, n);
        Assert.Equal("DL [DL# REDACTED] and SSN [SSN REDACTED] for case 25-002136", text);
    }

    [Fact]
    public void Roles_restrict_record_sensitivity_and_audit_access()
    {
        Assert.Equal(["NORMAL"], Roles.AllowedSensitivities(Roles.Deputy));
        Assert.Contains("JUVENILE", Roles.AllowedSensitivities(Roles.Analyst));
        Assert.DoesNotContain("RESTRICTED", Roles.AllowedSensitivities(Roles.Analyst));
        Assert.False(Roles.CanViewAudit(Roles.Deputy));
        Assert.True(Roles.CanViewAudit(Roles.Auditor));
    }
}

public sealed class AuditLogTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"blade-audit-{Guid.NewGuid():N}.db");
    private static readonly UserContext User = new("test.user", Roles.Analyst, "WS-1");

    [Fact]
    public void Hash_chain_verifies_and_detects_tampering()
    {
        var log = new AuditLog(_path);
        log.Write(new AuditWrite(User, "ASK", "north burglaries", RecordsReturned: ["25-000001"], PromptTokens: 100, CompletionTokens: 50));
        var middle = log.Write(new AuditWrite(User, "RECORD_VIEW", RecordsReturned: ["25-000001"]));
        log.Write(new AuditWrite(User with { Role = Roles.Deputy }, "AUDIT_VIEW", Outcome: "DENIED"));

        Assert.True(log.Verify().Valid);
        var usage = log.Usage().Single();
        Assert.Equal((1, 1, 1, 150L), (usage.AiQueries, usage.RecordViews, usage.Denied, usage.Tokens));

        using (var c = new SqliteConnection($"Data Source={_path};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"UPDATE audit_events SET records_returned = '' WHERE id = {middle}";
            cmd.ExecuteNonQuery();
        }
        var v = log.Verify();
        Assert.False(v.Valid);
        Assert.Equal(middle, v.FirstInvalidId);
    }

    [Fact]
    public void Questions_are_pii_filtered_before_logging()
    {
        var log = new AuditLog(_path);
        log.Write(new AuditWrite(User, "ASK", "anything on DL K12345678?"));
        Assert.Equal("anything on DL [DL# REDACTED]?", log.Query().Single().Question);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { /* best effort */ }
    }
}

public sealed class RetrievalAndGraphTests(DataGenFixture fx) : IClassFixture<DataGenFixture>
{
    private BladeData? _data;
    private BladeData Data => _data ??= new BladeData(fx.DbPath);

    private static SearchFilter DemoFilter() => QueryPlanner.Normalize(new SearchFilter
    {
        OffenseTypes = ["BURGLARY"], Districts = ["NORTH"], PremiseTypes = ["COMMERCIAL"], MoTags = ["FORCED_ENTRY"],
        LookbackMonths = 18, HourFrom = 22, HourTo = 6, SemanticQuery = "forced entry rear door pry",
    }, new DateOnly(2026, 11, 14));

    private HashSet<string> Truth(string storyline, string notePrefix)
    {
        using var db = fx.Open();
        return Rows(db, $"SELECT entity_key FROM eval_storyline_truth WHERE storyline = '{storyline}' AND note LIKE '{notePrefix}%'")
            .Select(r => (string)r[0]!).ToHashSet();
    }

    [Fact]
    public void Structured_search_returns_the_series_without_any_model()
    {
        var outcome = new IncidentSearch(Data).Search(DemoFilter(), Roles.Analyst, queryVector: null);
        var series = Truth(Storylines.S1, "Series burglary");
        Assert.All(series, s => Assert.Contains(outcome.Matches, m => m.Incident.CaseNumber == s));
        Assert.False(outcome.VectorUsed);
    }

    [Fact]
    public void Deputies_cannot_retrieve_juvenile_records_and_are_told_how_many_were_withheld()
    {
        var everything = QueryPlanner.Normalize(new SearchFilter(), new DateOnly(2026, 11, 14));
        var deputy = new IncidentSearch(Data).Search(everything with { }, Roles.Deputy, null);
        Assert.DoesNotContain(deputy.Matches, m => m.Incident.Sensitivity != "NORMAL");

        var juvenileOnly = Data.IncidentsByCase.Values.Count(i => i.Sensitivity != "NORMAL");
        Assert.True(juvenileOnly > 0);
        var narrow = new IncidentSearch(Data).Search(everything with { OffenseTypes = ["VANDALISM", "THEFT"] }, Roles.Deputy, null);
        Assert.True(narrow.Withheld > 0);
    }

    [Fact]
    public void Link_analysis_finds_the_van_the_pawner_and_the_bridge_person()
    {
        var graph = new EntityGraph(Data);
        var outcome = new IncidentSearch(Data).Search(DemoFilter(), Roles.Analyst, null);
        var analysis = new LinkAnalyzer(graph, Data).Analyze(outcome.Matches.Select(m => m.Incident.CaseNumber).ToList(), Roles.Analyst);

        var van = Assert.Single(analysis.Findings, f => f is { Kind: "VEHICLE", EntityId: "V:418KTV" });
        Assert.Equal(3, van.CaseNumbers.Count);
        Assert.Contains(van.Evidence, e => e.Contains("LPR camera"));

        var cody = Assert.Single(analysis.Findings, f => f.Kind == "PERSON" && f.EntityLabel.StartsWith("Cody"));
        Assert.Equal(2, cody.CaseNumbers.Count);

        Assert.Contains(analysis.Findings, f => f.Kind == "BRIDGE" && f.EntityLabel.StartsWith("Travis"));
        // Victims / reporting parties must not create person links.
        Assert.DoesNotContain(analysis.Findings, f => f.Kind == "PERSON" && !f.EntityLabel.StartsWith("Cody"));
        // The registered owner is shown as context of the van, not as a separate connector.
        Assert.DoesNotContain(analysis.Findings, f => f.Kind == "PERSON" && f.EntityLabel.StartsWith("Amber"));
    }

    [Fact]
    public void Series_detector_seeds_only_from_entity_linked_planted_cases()
    {
        var graph = new EntityGraph(Data);
        var outcome = new IncidentSearch(Data).Search(DemoFilter(), Roles.Analyst, null);
        var analysis = new LinkAnalyzer(graph, Data).Analyze(outcome.Matches.Select(m => m.Incident.CaseNumber).ToList(), Roles.Analyst);
        var series = SeriesDetector.Detect(outcome.Matches, analysis.Findings, Data.Vectors);

        Assert.NotNull(series); // no embeddings in the test DB, so only entity-linked seeds
        var truth = Truth(Storylines.S1, "Series burglary");
        Assert.Equal(4, series.Members.Count);
        Assert.All(series.Members, m => Assert.Contains(m.CaseNumber, truth));
        Assert.Equal(["FORCED_ENTRY", "PRY_TOOL", "REAR_ENTRY"], series.SharedMoTags.Order().ToList());
    }

    [Fact]
    public void Catalytic_ring_is_linked_through_the_shared_phone_owner()
    {
        var graph = new EntityGraph(Data);
        var ring = Truth(Storylines.S2, "").Where(k => k.Contains('-') && !k.StartsWith('(')).ToList();
        var analysis = new LinkAnalyzer(graph, Data).Analyze(ring, Roles.Analyst);
        var jaylen = Assert.Single(analysis.Findings, f => f.Kind == "PERSON" && f.EntityLabel.StartsWith("Jaylen"));
        Assert.True(jaylen.CaseNumbers.Count >= 3); // flyer phone, tip report phone, suspect truck owner
    }
}
