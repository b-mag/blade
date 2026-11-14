using System.Text.Json.Serialization;
using Blade.Core;
using Blade.Server;
using Blade.Server.Ai;
using Blade.Server.Answer;
using Blade.Server.Audit;
using Blade.Server.Data;
using Blade.Server.Graph;
using Blade.Server.Search;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var options = builder.Configuration.GetSection("Blade").Get<BladeOptions>() ?? new BladeOptions();
var repoRoot = FindRepoRoot(builder.Environment.ContentRootPath);
var dbPath = string.IsNullOrWhiteSpace(options.Data.Database) ? Path.Combine(repoRoot, "data", "blade.db") : options.Data.Database;
var auditPath = string.IsNullOrWhiteSpace(options.Data.AuditDatabase) ? Path.Combine(repoRoot, "data", "audit.db") : options.Data.AuditDatabase;

builder.Services.AddSingleton(options.AI);
builder.Services.AddSingleton(_ => new BladeData(dbPath));
builder.Services.AddSingleton(_ => new AuditLog(auditPath));
builder.Services.AddSingleton<EntityGraph>();
builder.Services.AddSingleton<LinkAnalyzer>();
builder.Services.AddSingleton<GraphViews>();
builder.Services.AddSingleton<IncidentSearch>();
builder.Services.AddHttpClient("ai", c => c.Timeout = TimeSpan.FromMinutes(4));
builder.Services.AddSingleton(sp => new QueryEmbedder(sp.GetRequiredService<IHttpClientFactory>().CreateClient("ai"), options.AI));
builder.Services.AddSingleton<ILlmClient>(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("ai");
    return options.AI.Provider.Equals("OpenAICompatible", StringComparison.OrdinalIgnoreCase)
        ? new OpenAiCompatibleLlmClient(http, options.AI)
        : new OllamaLlmClient(http, options.AI);
});
builder.Services.AddSingleton<QueryPlanner>();
builder.Services.AddSingleton<AskService>();
builder.Services.AddSingleton<PolicyService>();
builder.Services.AddSingleton<ModelWarmup>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ModelWarmup>());

var app = builder.Build();

// Build data + graph eagerly so the first request is fast and startup fails loudly if the data is missing.
var data = app.Services.GetRequiredService<BladeData>();
var graph = app.Services.GetRequiredService<EntityGraph>();
app.Logger.LogInformation("BLADE loaded {Incidents} incidents, {Sections} policy sections, {Vectors} vectors; graph {Nodes} nodes / {Edges} edges",
    data.IncidentsByCase.Count, data.PolicySections.Count, data.Vectors.Count, graph.Nodes.Count, graph.EdgeCount);

var api = app.MapGroup("/api");

api.MapGet("/system", (ILlmClient llm, ModelWarmup warm) => new SystemInfo(
    Agency.Name, $"{Agency.ProductName} — {Agency.ProductLongName}", Agency.SyntheticDataBanner, llm.Provider, llm.Model,
    data.EmbeddingModel ?? options.AI.EmbeddingModel, llm.IsLocal, warm.IsWarm, data.DataAsOf, data.IncidentsByCase.Count,
    data.PolicySections.Count, data.Vectors.Count, typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.1"));

api.MapPost("/ask", async (AskRequest req, HttpContext http, AskService ask, AuditLog audit, CancellationToken ct) =>
{
    var user = UserContext.From(http);
    if (string.IsNullOrWhiteSpace(req.Question) && req.FilterOverride is null) return Results.BadRequest("Question is required.");
    if (user.Role == Roles.Auditor) return Deny(audit, user, "ASK", req.Question);
    var started = DateTime.UtcNow;
    try
    {
        var o = await ask.AskAsync(req, user.Role, ct);
        var id = audit.Write(new AuditWrite(user, "ASK", req.Question,
            Detail: System.Text.Json.JsonSerializer.Serialize(o.Response.Filter),
            RecordsReturned: o.Response.Results.Select(r => r.CaseNumber),
            RecordsCited: o.Response.Citations.Where(c => c.Verified).Select(c => c.Id),
            Provider: o.Response.Trace.Provider, Model: o.Response.Trace.Model,
            LatencyMs: (long)(DateTime.UtcNow - started).TotalMilliseconds,
            PromptTokens: o.Response.Trace.PromptTokens, CompletionTokens: o.Response.Trace.CompletionTokens,
            Outcome: o.Response.Citations.Any(c => !c.Verified) ? "OK_UNVERIFIED_CITATIONS" : "OK"));
        return Results.Ok(o.Response with { AuditId = id });
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        audit.Write(new AuditWrite(user, "ASK", req.Question, Detail: ex.Message, Outcome: "ERROR"));
        return Results.Problem(ex.Message, title: "AI service error");
    }
});

api.MapPost("/policy", async (PolicyRequest req, HttpContext http, PolicyService policy, AuditLog audit, CancellationToken ct) =>
{
    var user = UserContext.From(http);
    if (string.IsNullOrWhiteSpace(req.Question)) return Results.BadRequest("Question is required.");
    var started = DateTime.UtcNow;
    try
    {
        var o = await policy.AskAsync(req, user.Role, ct);
        var id = audit.Write(new AuditWrite(user, "POLICY_QUERY", req.Question,
            RecordsReturned: o.Response.Sections.Select(s => "Policy " + s.SectionNumber).Concat(o.Response.RelatedCases.Select(c => c.CaseNumber)),
            RecordsCited: o.Response.Citations.Where(c => c.Verified).Select(c => c.Id),
            Provider: o.Response.Trace.Provider, Model: o.Response.Trace.Model,
            LatencyMs: (long)(DateTime.UtcNow - started).TotalMilliseconds,
            PromptTokens: o.Response.Trace.PromptTokens, CompletionTokens: o.Response.Trace.CompletionTokens));
        return Results.Ok(o.Response with { AuditId = id });
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        audit.Write(new AuditWrite(user, "POLICY_QUERY", req.Question, Detail: ex.Message, Outcome: "ERROR"));
        return Results.Problem(ex.Message, title: "AI service error");
    }
});

api.MapGet("/incidents/{caseNumber}", (string caseNumber, HttpContext http, AuditLog audit) =>
{
    var user = UserContext.From(http);
    if (!data.IncidentsByCase.TryGetValue(caseNumber, out var i)) return Results.NotFound();
    if (!Roles.AllowedSensitivities(user.Role).Contains(i.Sensitivity))
        return Deny(audit, user, "RECORD_VIEW", null, $"{caseNumber} ({i.Sensitivity})");
    audit.Write(new AuditWrite(user, "RECORD_VIEW", RecordsReturned: [caseNumber]));
    var dep = data.Deputies[i.DeputyId];
    return Results.Ok(new IncidentDetail(i.CaseNumber, i.OffenseType, i.OffenseDescription, i.District, i.PremiseType, data.LocationOf(i).Display,
        i.OccurredFrom, i.OccurredTo, i.ReportedAt, string.Join(", ", i.MoTags), i.Status, i.Sensitivity, dep.Display, i.Narrative,
        data.IncidentPersons.Where(p => p.IncidentId == i.Id).Select(p => new PersonRef(p.PersonId, data.Persons[p.PersonId].FullName, p.Role)).ToList(),
        data.IncidentVehicles.Where(v => v.IncidentId == i.Id).Select(v => new VehicleRef(v.VehicleId, data.Vehicles[v.VehicleId].Plate, data.Vehicles[v.VehicleId].Description, v.Role)).ToList(),
        data.Property.Where(p => p.IncidentId == i.Id).Select(p => $"{p.Description}{(p.Serial is null ? "" : $" (S/N {p.Serial})")} — ${p.Value:N0} [{p.Status}]").ToList(),
        data.CadByIncident.TryGetValue(i.Id, out var cad) ? cad.CallNumber : null));
});

api.MapGet("/graph", (string q, int? depth, HttpContext http, GraphViews views, AuditLog audit) =>
{
    var user = UserContext.From(http);
    if (user.Role == Roles.Auditor) return Deny(audit, user, "GRAPH_QUERY", q);
    var node = views.Resolve(q);
    if (node is null)
    {
        audit.Write(new AuditWrite(user, "GRAPH_QUERY", q, Detail: "No matching entity", Outcome: "NOT_FOUND"));
        return Results.NotFound(new { error = $"No case, plate, phone, record number or person matches \"{q}\"." });
    }
    var view = views.Neighborhood(node, user.Role, Math.Clamp(depth ?? 2, 1, 3));
    audit.Write(new AuditWrite(user, "GRAPH_QUERY", q, Detail: $"{node}: {view.Nodes.Count} nodes, {view.Withheld} withheld",
        RecordsReturned: view.Nodes.Where(n => n.Kind == "INCIDENT").Select(n => n.Id[2..])));
    return Results.Ok(view);
});

api.MapGet("/policy/sections/{section}", (string section) =>
    data.PolicyBySection.TryGetValue(section, out var s)
        ? Results.Ok(new PolicySectionHit(s.SectionNumber, s.PolicyNumber, s.PolicyTitle, s.Heading, s.Text, 1))
        : Results.NotFound());

api.MapGet("/policy/sections", () => data.PolicySections.Select(s => new PolicySectionHit(s.SectionNumber, s.PolicyNumber, s.PolicyTitle, s.Heading, s.Text, 0)));

api.MapGet("/audit", (HttpContext http, AuditLog audit, string? user, string? action, int? take) =>
{
    var u = UserContext.From(http);
    return Roles.CanViewAudit(u.Role) ? Results.Ok(audit.Query(user, action, take ?? 200)) : Deny(audit, u, "AUDIT_VIEW");
});

api.MapGet("/audit/usage", (HttpContext http, AuditLog audit) =>
{
    var u = UserContext.From(http);
    return Roles.CanViewAudit(u.Role) ? Results.Ok(audit.Usage()) : Deny(audit, u, "AUDIT_VIEW");
});

api.MapGet("/audit/verify", (HttpContext http, AuditLog audit) =>
{
    var u = UserContext.From(http);
    return Roles.CanViewAudit(u.Role) ? Results.Ok(audit.Verify()) : Deny(audit, u, "AUDIT_VIEW");
});

api.MapPost("/admin/warmup", async (ModelWarmup warm, CancellationToken ct) => { await warm.WarmAsync(ct); return Results.Ok(new { warm.IsWarm }); });

app.Run();

static IResult Deny(AuditLog audit, UserContext user, string action, string? question = null, string? detail = null)
{
    audit.Write(new AuditWrite(user, action, question, Detail: detail ?? $"Role {user.Role} not permitted", Outcome: "DENIED"));
    return Results.Json(new { error = $"Access denied for role {user.Role}. This attempt has been logged." }, statusCode: StatusCodes.Status403Forbidden);
}

static string FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Blade.slnx"))) dir = dir.Parent;
    return dir?.FullName ?? start;
}

public partial class Program;
