using System.Diagnostics;
using System.Text;
using Blade.Core;
using Blade.Server.Ai;
using Blade.Server.Data;
using Blade.Server.Search;

namespace Blade.Server.Answer;

public sealed record PolicyOutcome(PolicyResponse Response, LlmResult? Answer);

/// <summary>
/// Policy Assistant: hybrid retrieval (vector + BM25, fused) over policy-manual sections, a strictly grounded answer
/// with section citations, and a few related historical cases for training context.
/// </summary>
public sealed class PolicyService(BladeData data, ILlmClient llm, QueryEmbedder embedder)
{
    public const int SectionsInContext = 6;

    public static readonly string SystemPrompt = """
        You are the BLADE Policy Assistant for the Kestrel County Sheriff's Office.
        Rules:
        - Answer ONLY from the policy excerpts provided. Do not use outside knowledge or general law.
        - Cite the section after each statement in this exact form: [Policy 300.3.6].
        - If the excerpts do not cover part of the question, say clearly: "The provided policy sections do not address ..."
        - Do not give legal advice. Be precise about words like "shall", "should" and "may" — keep the policy's level of obligation.
        - Historical cases, if provided, are examples only. You may mention one briefly as an illustration, citing its case number in brackets; never treat a case as policy.
        Format (markdown): a direct 1-2 sentence answer, then "**What policy requires**" as bullets, then "**Related considerations**" bullets if relevant.
        Keep it under 280 words.
        """;

    public async Task<PolicyOutcome> AskAsync(PolicyRequest req, string role, CancellationToken ct = default)
    {
        var stages = new List<TraceStage>();
        var warnings = new List<string>();
        var sw = Stopwatch.StartNew();

        var qv = await embedder.EmbedQueryAsync(req.Question, ct);
        stages.Add(new TraceStage("Embed question (local embedding model)", sw.ElapsedMilliseconds, qv is null ? "unavailable — keyword only" : $"{qv.Length} dimensions"));
        if (qv is null) warnings.Add("Embedding model unavailable: policy sections ranked by keyword relevance only.");

        sw.Restart();
        var vector = qv is null ? [] : data.Vectors.Search("POLICY", qv, 12);
        var keyword = Keyword(req.Question, 12);
        var fused = new Dictionary<string, double>();
        foreach (var (id, idx) in vector.Select((v, i) => (v.Id, i))) fused[id] = fused.GetValueOrDefault(id) + 1.0 / (60 + idx);
        foreach (var (id, idx) in keyword.Select((v, i) => (v, i))) fused[id] = fused.GetValueOrDefault(id) + 1.0 / (60 + idx);
        var top = fused.OrderByDescending(kv => kv.Value).Take(SectionsInContext)
            .Where(kv => data.PolicyBySection.ContainsKey(kv.Key))
            .Select(kv => (Section: data.PolicyBySection[kv.Key], Score: kv.Value)).ToList();
        var max = top.Count > 0 ? top.Max(t => t.Score) : 1;
        stages.Add(new TraceStage("Retrieve policy sections (vector + BM25, reciprocal rank fusion)", sw.ElapsedMilliseconds,
            string.Join(", ", top.Select(t => t.Section.SectionNumber))));

        sw.Restart();
        var allowed = Roles.AllowedSensitivities(role);
        var related = qv is null ? [] : data.Vectors.Search("INCIDENT", qv, 3,
                id => data.IncidentsByCase.TryGetValue(id, out var i) && allowed.Contains(i.Sensitivity))
            .Where(r => r.Score >= 0.62).Select(r => (Incident: data.IncidentsByCase[r.Id], r.Score)).ToList();
        stages.Add(new TraceStage("Find related historical cases (vector)", sw.ElapsedMilliseconds, $"{related.Count} cases"));

        var prompt = new StringBuilder();
        prompt.AppendLine($"QUESTION: {req.Question}").AppendLine().AppendLine("POLICY EXCERPTS:");
        foreach (var (s, _) in top)
            prompt.AppendLine($"[Policy {s.SectionNumber}] (Policy {s.PolicyNumber} — {s.PolicyTitle} / {s.Heading})").AppendLine(s.Text).AppendLine();
        if (related.Count > 0)
        {
            prompt.AppendLine("HISTORICAL CASES (examples only, not policy):");
            foreach (var (i, _) in related)
                prompt.AppendLine($"[{i.CaseNumber}] {i.OccurredFrom:yyyy-MM-dd} {i.OffenseDescription}: {NarrativeText.Truncate(NarrativeText.Content(i.Narrative), 420)}");
        }

        sw.Restart();
        var ans = await llm.CompleteAsync(new LlmRequest(SystemPrompt, prompt.ToString(), Temperature: 0.1, MaxTokens: 650), ct);
        stages.Add(new TraceStage("Compose answer (LLM, grounded)", sw.ElapsedMilliseconds, $"{ans.PromptTokens} prompt / {ans.CompletionTokens} output tokens"));

        sw.Restart();
        var guard = CitationGuard.Check(ans.Text, related.Select(r => r.Incident.CaseNumber).ToHashSet(), top.Select(t => t.Section.SectionNumber).ToHashSet());
        var (text, redactions) = PiiRedactor.Redact(guard.Text);
        stages.Add(new TraceStage("Citation guard + PII filter", sw.ElapsedMilliseconds, $"{guard.Citations.Count} citations, {guard.Unverified.Count} unverified"));
        if (guard.Unverified.Count > 0) warnings.Add($"The model cited {guard.Unverified.Count} section(s) or record(s) it was not given; flagged ⚠UNVERIFIED.");
        if (!guard.Citations.Any(c => c.Kind == "POLICY" && c.Verified)) warnings.Add("The answer does not cite any policy section — treat it with caution.");

        var response = new PolicyResponse
        {
            Answer = text + "\n\n*Policy reference only — not legal advice. Confirm with your supervisor and the current official manual.*",
            Sections = top.Select(t => new PolicySectionHit(t.Section.SectionNumber, t.Section.PolicyNumber, t.Section.PolicyTitle, t.Section.Heading, t.Section.Text, Math.Round(t.Score / max, 3))).ToList(),
            RelatedCases = related.Select(r => new IncidentHit(r.Incident.CaseNumber, r.Incident.OffenseType, r.Incident.OffenseDescription, r.Incident.District,
                r.Incident.PremiseType, data.LocationOf(r.Incident).Display, r.Incident.OccurredFrom, r.Incident.ReportedAt, string.Join(", ", r.Incident.MoTags),
                r.Incident.Status, r.Incident.Sensitivity, Math.Round(r.Score, 3), NarrativeText.Truncate(NarrativeText.Content(r.Incident.Narrative), 240), false, null)).ToList(),
            Citations = guard.Citations,
            Warnings = warnings,
            Trace = new AiTrace
            {
                Provider = llm.Provider, Model = llm.Model, Local = llm.IsLocal, Stages = stages,
                AnswerSystemPrompt = SystemPrompt, AnswerUserPrompt = prompt.ToString(), AnswerRawOutput = ans.Text,
                PromptTokens = ans.PromptTokens, CompletionTokens = ans.CompletionTokens,
                Retrieved = top.Select(t => new RetrievedItem(t.Section.SectionNumber, "POLICY", Math.Round(t.Score / max, 3), t.Section.Heading))
                    .Concat(related.Select(r => new RetrievedItem(r.Incident.CaseNumber, "INCIDENT", Math.Round(r.Score, 3), "related historical case"))).ToList(),
                SafetyChecks =
                [
                    "Answer restricted to retrieved policy excerpts (grounding prompt)",
                    $"Citation guard: {guard.Citations.Count(c => c.Verified)} verified, {guard.Unverified.Count} unverified",
                    $"Related cases filtered to {role} clearance",
                    $"PII output filter: {redactions} redaction(s)",
                ],
            },
        };
        return new PolicyOutcome(response, ans);
    }

    private List<string> Keyword(string question, int k)
    {
        var q = IncidentSearch.FtsQuery(question);
        if (q is null) return [];
        using var c = data.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT section_number FROM policy_fts WHERE policy_fts MATCH $q ORDER BY bm25(policy_fts, 0, 2.0, 1.0) LIMIT $k";
        cmd.Parameters.AddWithValue("$q", q);
        cmd.Parameters.AddWithValue("$k", k);
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }
}
