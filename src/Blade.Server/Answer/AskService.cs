using System.Diagnostics;
using System.Text;
using Blade.Core;
using Blade.Server.Ai;
using Blade.Server.Data;
using Blade.Server.Graph;
using Blade.Server.Search;

namespace Blade.Server.Answer;

public sealed record AskOutcome(AskResponse Response, string Question, LlmResult? Planner, LlmResult? Answer);

/// <summary>
/// Ask BLADE pipeline: interpret → retrieve (role-filtered, hybrid) → link analysis (graph) → series detection →
/// grounded answer with citations → citation guard → PII filter. Every stage is timed for the Under-the-Hood panel.
/// </summary>
public sealed class AskService(BladeData data, ILlmClient llm, QueryEmbedder embedder, IncidentSearch search, LinkAnalyzer links, QueryPlanner planner, GraphViews graphs)
{
    public const int MaxContextIncidents = 10;

    public static readonly string AnswerSystemPrompt = """
        You are BLADE, an investigative analysis assistant for the Kestrel County Sheriff's Office. You support deputies and crime analysts.
        Rules:
        - Use ONLY the records and system-detected links provided. Do not use outside knowledge about people, places or events.
        - Cite every factual statement with the record identifier in square brackets, e.g. [25-002136], [FI-26-00123], [B25-004012], [PT-FP-10147].
        - Never invent identifiers, names, plates, dates or numbers. If something is not in the provided material, say it is not in the records.
        - The system-detected links were computed from source systems; explain them plainly and cite their records.
        - Use neutral, factual language. Describe people as "listed as suspect", "field-interviewed", "co-arrested" — not as guilty.
        - If a possible series was detected, lead with it and list its case numbers.
        Example of a properly cited sentence:
          "A white Ford Transit van was read by an LPR camera 20 minutes before the burglary at Copperline Construction Supply [25-002136]."
        Format (markdown, no numbered headings):
        A short opening paragraph answering the question directly (how many matching incidents, any possible series), with citations.
        **Key patterns** — 2-4 bullets (time of day, targets, method, property), each with citations.
        **Connections found** — one bullet per detected link explaining how the records connect, each with citations.
        **Suggested next steps** — 2-3 bullets.
        Be concise: under 230 words. Cite the 1-3 most relevant identifiers per bullet, not every case.
        """;

    private const string CitationReminder =
        "Write the answer now. EVERY bullet and sentence that states a fact MUST end with one or more record identifiers in square brackets, e.g. [25-002136] or [FI-26-00123]. Use only identifiers that appear above.";

    public async Task<AskOutcome> AskAsync(AskRequest req, string role, CancellationToken ct = default)
    {
        var stages = new List<TraceStage>();
        var warnings = new List<string>();
        var sw = Stopwatch.StartNew();
        var today = DateOnly.FromDateTime(DateTime.Now);

        // 1. Interpret
        PlanResult plan;
        if (req.FilterOverride is not null)
        {
            plan = new PlanResult(QueryPlanner.Normalize(req.FilterOverride with { DateFrom = null, DateTo = null }, today), "(filter edited by user)", null, []);
            stages.Add(new TraceStage("Interpret question", 0, "User-edited filter supplied; LLM planner skipped"));
        }
        else
        {
            plan = await planner.PlanAsync(req.Question, today, ct);
            stages.Add(new TraceStage("Interpret question (LLM → JSON filter)", sw.ElapsedMilliseconds, $"{plan.Llm?.CompletionTokens} output tokens"));
        }
        warnings.AddRange(plan.Warnings);
        var filter = plan.Filter;

        // 2. Embed query
        sw.Restart();
        var semantic = filter.SemanticQuery.Length > 0 ? filter.SemanticQuery : req.Question;
        var qv = await embedder.EmbedQueryAsync(semantic, ct);
        stages.Add(new TraceStage("Embed query (local embedding model)", sw.ElapsedMilliseconds, qv is null ? "Embedding model unavailable — keyword + structured only" : $"{qv.Length} dimensions"));
        if (qv is null) warnings.Add("Embedding model unavailable: results ranked by keyword relevance only.");

        // 3. Retrieve
        sw.Restart();
        var outcome = search.Search(filter, role, qv);
        stages.Add(new TraceStage("Retrieve (parameterized SQL + FTS5 + vector, role-filtered)", sw.ElapsedMilliseconds,
            $"{outcome.Matches.Count} matches, {outcome.Withheld} withheld by role"));
        if (outcome.RelaxedMo) warnings.Add("No incidents matched all M.O. tags; showing incidents matching any of them.");
        if (outcome.Withheld > 0) warnings.Add($"{outcome.Withheld} matching record(s) withheld: your role ({role}) is not cleared for their sensitivity level.");

        // 4. Link analysis over the top results
        sw.Restart();
        var linkSet = outcome.Matches.Take(40).Select(m => m.Incident.CaseNumber).ToList();
        var analysis = links.Analyze(linkSet, role);
        stages.Add(new TraceStage("Link analysis (entity graph)", sw.ElapsedMilliseconds, $"{analysis.Findings.Count} connections across {linkSet.Count} incidents"));

        // 5. Series detection
        sw.Restart();
        var series = SeriesDetector.Detect(outcome.Matches.Take(40).ToList(), analysis.Findings, data.Vectors);
        stages.Add(new TraceStage("Series detection (entity seeds + M.O. similarity)", sw.ElapsedMilliseconds, series is null ? "No series detected" : $"{series.Members.Count} incidents"));

        // 6. Build grounded context and answer
        var seriesCases = series?.Members.Select(m => m.CaseNumber).ToHashSet() ?? [];
        var context = outcome.Matches.Where(m => seriesCases.Contains(m.Incident.CaseNumber))
            .Concat(outcome.Matches.Where(m => !seriesCases.Contains(m.Incident.CaseNumber)))
            .Take(MaxContextIncidents).ToList();
        var allowedIds = context.Select(c => c.Incident.CaseNumber).Concat(analysis.CitableIds).ToHashSet();
        var userPrompt = BuildPrompt(req.Question, filter, outcome, context, series, analysis);

        sw.Restart();
        string answer;
        LlmResult? ans = null;
        if (outcome.Matches.Count == 0)
        {
            answer = "No incidents in the records you are cleared to view match this search. Try widening the date range, district or M.O. filters.";
            stages.Add(new TraceStage("Compose answer", 0, "Skipped — no matching records (nothing to ground an answer on)"));
        }
        else
        {
            ans = await llm.CompleteAsync(new LlmRequest(AnswerSystemPrompt, userPrompt, Temperature: 0.1, MaxTokens: 1000), ct);
            answer = ans.Text;
            stages.Add(new TraceStage("Compose answer (LLM, grounded)", sw.ElapsedMilliseconds, $"{ans.PromptTokens} prompt / {ans.CompletionTokens} output tokens"));

            // Grounding check: an answer without verifiable citations is regenerated once with explicit feedback.
            if (CitationGuard.Check(answer, allowedIds).Citations.Count(c => c.Verified) < 2)
            {
                sw.Restart();
                var retry = await llm.CompleteAsync(new LlmRequest(AnswerSystemPrompt,
                    userPrompt + "\n\nYOUR PREVIOUS DRAFT WAS REJECTED because it did not cite record identifiers. Rewrite it so every factual bullet ends with [case number] citations.",
                    Temperature: 0.1, MaxTokens: 1000), ct);
                stages.Add(new TraceStage("Re-generate (citation check failed)", sw.ElapsedMilliseconds, $"{retry.PromptTokens} prompt / {retry.CompletionTokens} output tokens"));
                warnings.Add("First draft lacked citations and was regenerated automatically.");
                ans = retry with { PromptTokens = ans.PromptTokens + retry.PromptTokens, CompletionTokens = ans.CompletionTokens + retry.CompletionTokens };
                answer = retry.Text;
            }
        }

        // 7. Guards
        sw.Restart();
        var guard = CitationGuard.Check(answer, allowedIds);
        var (redacted, redactions) = PiiRedactor.Redact(guard.Text);
        stages.Add(new TraceStage("Citation guard + PII filter", sw.ElapsedMilliseconds, $"{guard.Citations.Count} citations, {guard.Unverified.Count} unverified, {redactions} redactions"));
        if (guard.Unverified.Count > 0) warnings.Add($"The model cited {guard.Unverified.Count} identifier(s) it was not given; they are flagged ⚠UNVERIFIED.");

        var response = new AskResponse
        {
            Answer = redacted,
            Filter = filter,
            Chips = QueryPlanner.Chips(filter),
            TotalMatches = outcome.TotalVisible,
            Withheld = outcome.Withheld,
            Results = outcome.Matches.Select(m => ToHit(m, series)).ToList(),
            Series = series,
            Graph = analysis.Findings.Count > 0 ? graphs.ForFindings(analysis.Findings, seriesCases, role) : null,
            Links = analysis.Findings,
            Citations = guard.Citations,
            Warnings = warnings,
            Trace = new AiTrace
            {
                Provider = llm.Provider,
                Model = llm.Model,
                Local = llm.IsLocal,
                Stages = stages,
                PlannerSystemPrompt = req.FilterOverride is null ? QueryPlanner.SystemPrompt : null,
                PlannerRawOutput = plan.RawOutput,
                AnswerSystemPrompt = AnswerSystemPrompt,
                AnswerUserPrompt = userPrompt,
                AnswerRawOutput = ans?.Text,
                PromptTokens = (plan.Llm?.PromptTokens ?? 0) + (ans?.PromptTokens ?? 0),
                CompletionTokens = (plan.Llm?.CompletionTokens ?? 0) + (ans?.CompletionTokens ?? 0),
                Retrieved = context.Select(c => new RetrievedItem(c.Incident.CaseNumber, "INCIDENT", c.Relevance,
                    $"vector {c.Vector:F3} · keyword {c.Keyword:F2}{(seriesCases.Contains(c.Incident.CaseNumber) ? " · series" : "")}")).ToList(),
                SafetyChecks =
                [
                    $"Role filter: {role} cleared for {string.Join(", ", Roles.AllowedSensitivities(role))}; {outcome.Withheld} record(s) withheld before retrieval",
                    "Query executed as parameterized SQL (model output never executed as code)",
                    $"Citation guard: {guard.Citations.Count(c => c.Verified)} verified, {guard.Unverified.Count} unverified",
                    $"PII output filter: {redactions} redaction(s)",
                    $"Model: {llm.Model} via {llm.Provider}",
                ],
            },
        };
        return new AskOutcome(response, req.Question, plan.Llm, ans);
    }

    private string BuildPrompt(string question, SearchFilter f, SearchOutcome outcome, List<ScoredIncident> context, SeriesFinding? series, LinkAnalysis analysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"QUESTION: {question}");
        sb.AppendLine();
        sb.AppendLine($"FILTER APPLIED: {string.Join(" | ", QueryPlanner.Chips(f).Select(c => $"{c.Label}: {c.Value}"))}");
        sb.AppendLine($"MATCHING INCIDENTS: {outcome.TotalVisible} (showing the {context.Count} most relevant below)");
        sb.AppendLine();
        sb.AppendLine("RECORDS:");
        foreach (var s in context)
        {
            var i = s.Incident;
            sb.AppendLine($"[{i.CaseNumber}] {i.OccurredFrom:yyyy-MM-dd HH:mm} | {i.OffenseDescription} | {data.LocationOf(i).Display} ({i.District}) | M.O.: {string.Join(", ", i.MoTags)} | Status: {i.Status}");
            sb.AppendLine($"  Summary: {NarrativeText.Truncate(NarrativeText.Content(i.Narrative), 400)}");
        }
        if (series is not null)
        {
            sb.AppendLine();
            sb.AppendLine("SYSTEM-DETECTED POSSIBLE SERIES:");
            sb.AppendLine(series.Summary);
            foreach (var m in series.Members) sb.AppendLine($"- [{m.CaseNumber}] {m.Reason}");
        }
        if (analysis.Findings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("SYSTEM-DETECTED LINKS (from LPR, field interviews, jail bookings, pawn records and reports):");
            foreach (var l in analysis.Findings.Where(l => l.Kind != "LOCATION").Take(5))
            {
                sb.AppendLine($"* {CitationGuard.Bracket(l.Summary)} Cases: {string.Join(" ", l.CaseNumbers.Select(c => $"[{c}]"))}");
                foreach (var e in l.Evidence.Take(7)) sb.AppendLine($"  - {CitationGuard.Bracket(e)}");
            }
        }
        sb.AppendLine();
        sb.AppendLine(CitationReminder);
        return sb.ToString();
    }

    private IncidentHit ToHit(ScoredIncident s, SeriesFinding? series)
    {
        var i = s.Incident;
        var member = series?.Members.FirstOrDefault(m => m.CaseNumber == i.CaseNumber);
        return new IncidentHit(i.CaseNumber, i.OffenseType, i.OffenseDescription, i.District, i.PremiseType, data.LocationOf(i).Display,
            i.OccurredFrom, i.ReportedAt, string.Join(", ", i.MoTags), i.Status, i.Sensitivity, s.Relevance,
            NarrativeText.Truncate(NarrativeText.Content(i.Narrative), 240), member is not null, member?.Reason);
    }
}
