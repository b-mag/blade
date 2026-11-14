using System.Text.RegularExpressions;
using Blade.Core;
using Blade.Server.Data;
using Microsoft.Data.Sqlite;

namespace Blade.Server.Search;

public sealed record ScoredIncident(IncidentRecord Incident, double Vector, double Keyword, double Relevance);

public sealed record SearchOutcome(List<ScoredIncident> Matches, int Withheld, bool RelaxedMo, string Sql, bool VectorUsed, int TotalVisible);

/// <summary>
/// Hybrid retrieval: (1) parameterized SQL applies the structured filter AND the caller's record-sensitivity
/// clearance, so restricted records never reach the model; (2) FTS5/BM25 keyword relevance and (3) vector
/// similarity rank the survivors (reciprocal rank fusion).
/// </summary>
public sealed partial class IncidentSearch(BladeData data)
{
    public const int MaxMatches = 200;

    public SearchOutcome Search(SearchFilter f, string role, float[]? queryVector)
    {
        var (visible, sql, visibleTotal) = Query(f, Roles.AllowedSensitivities(role), anyMo: false);
        var relaxed = false;
        if (visible.Count == 0 && f.MoTags.Count > 1)
        {
            (visible, sql, visibleTotal) = Query(f, Roles.AllowedSensitivities(role), anyMo: true);
            relaxed = true;
        }
        var (_, _, allTotal) = Query(f, Vocabulary.Sensitivities, anyMo: relaxed, countOnly: true);
        var withheld = Math.Max(0, allTotal - visibleTotal);

        var keyword = KeywordScores(f.SemanticQuery, visible.Select(i => i.CaseNumber).ToHashSet());
        var vector = new Dictionary<string, double>();
        if (queryVector is not null)
            foreach (var (id, score) in data.Vectors.Search("INCIDENT", queryVector, visible.Count, visible.Select(i => i.CaseNumber).ToHashSet().Contains))
                vector[id] = score;

        // Reciprocal rank fusion of vector and keyword rankings (k = 60), recency as a light tiebreaker.
        var vRank = vector.OrderByDescending(kv => kv.Value).Select((kv, idx) => (kv.Key, idx)).ToDictionary(x => x.Key, x => x.idx);
        var kRank = keyword.OrderByDescending(kv => kv.Value).Select((kv, idx) => (kv.Key, idx)).ToDictionary(x => x.Key, x => x.idx);
        var scored = visible.Select(i =>
        {
            var rrf = (vRank.TryGetValue(i.CaseNumber, out var vr) ? 1.0 / (60 + vr) : 0) + (kRank.TryGetValue(i.CaseNumber, out var kr) ? 1.0 / (60 + kr) : 0);
            return new ScoredIncident(i, vector.GetValueOrDefault(i.CaseNumber), keyword.GetValueOrDefault(i.CaseNumber), rrf);
        })
        .OrderByDescending(s => s.Relevance).ThenByDescending(s => s.Incident.OccurredFrom)
        .ToList();

        // Normalize relevance to 0..1 for display.
        var max = scored.Count > 0 ? scored.Max(s => s.Relevance) : 0;
        if (max > 0) scored = scored.Select(s => s with { Relevance = Math.Round(s.Relevance / max, 3) }).ToList();

        return new SearchOutcome(scored, withheld, relaxed, sql, queryVector is not null, visibleTotal);
    }

    private (List<IncidentRecord> Rows, string Sql, int Total) Query(SearchFilter f, string[] sensitivities, bool anyMo, bool countOnly = false)
    {
        using var c = data.Open();
        using var cmd = c.CreateCommand();
        var where = new List<string>();

        void In(string column, IReadOnlyList<string> values, string prefix)
        {
            if (values.Count == 0) return;
            var names = values.Select((v, n) => { cmd.Parameters.AddWithValue($"${prefix}{n}", v); return $"${prefix}{n}"; });
            where.Add($"i.{column} IN ({string.Join(", ", names)})");
        }

        In("sensitivity", sensitivities, "sens");
        In("offense_type", f.OffenseTypes, "off");
        In("district", f.Districts, "dist");
        In("premise_type", f.PremiseTypes, "prem");
        if (f.DateFrom is { } from) { where.Add("i.occurred_date >= $from"); cmd.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd")); }
        if (f.DateTo is { } to) { where.Add("i.occurred_date <= $to"); cmd.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd")); }
        if (f.HourFrom >= 0 && f.HourTo >= 0)
        {
            where.Add(f.HourFrom < f.HourTo ? "(i.occurred_hour >= $hf AND i.occurred_hour < $ht)" : "(i.occurred_hour >= $hf OR i.occurred_hour < $ht)");
            cmd.Parameters.AddWithValue("$hf", f.HourFrom);
            cmd.Parameters.AddWithValue("$ht", f.HourTo);
        }
        if (f.MoTags.Count > 0)
        {
            var clauses = f.MoTags.Select((m, n) =>
            {
                cmd.Parameters.AddWithValue($"$mo{n}", m);
                return $"EXISTS (SELECT 1 FROM incident_mo m WHERE m.incident_id = i.id AND m.mo_tag = $mo{n})";
            });
            where.Add("(" + string.Join(anyMo ? " OR " : " AND ", clauses) + ")");
        }

        var whereSql = where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "";
        cmd.CommandText = $"SELECT COUNT(*) FROM incidents i{whereSql}";
        var total = Convert.ToInt32(cmd.ExecuteScalar());
        if (countOnly) return ([], cmd.CommandText, total);

        cmd.CommandText = $"SELECT i.case_number FROM incidents i{whereSql} ORDER BY i.occurred_from DESC LIMIT {MaxMatches}";
        var rows = new List<IncidentRecord>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) rows.Add(data.IncidentsByCase[r.GetString(0)]);
        return (rows, cmd.CommandText, total);
    }

    private static readonly HashSet<string> Stop = ["the", "and", "for", "with", "from", "that", "this", "after", "before", "into", "on", "in", "of", "at", "a", "an", "or", "to", "show", "find", "similar", "any", "all"];

    [GeneratedRegex(@"[A-Za-z0-9]+")] private static partial Regex WordRx();

    public static string? FtsQuery(string text)
    {
        var words = WordRx().Matches(text.ToLowerInvariant()).Select(m => m.Value).Where(w => w.Length > 2 && !Stop.Contains(w)).Distinct().Take(12).ToList();
        return words.Count == 0 ? null : string.Join(" OR ", words.Select(w => $"\"{w}\""));
    }

    private Dictionary<string, double> KeywordScores(string text, HashSet<string> allowed)
    {
        var scores = new Dictionary<string, double>();
        var q = FtsQuery(text);
        if (q is null || allowed.Count == 0) return scores;
        using var c = data.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT case_number, bm25(incidents_fts) FROM incidents_fts WHERE incidents_fts MATCH $q";
        cmd.Parameters.AddWithValue("$q", q);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (allowed.Contains(r.GetString(0))) scores[r.GetString(0)] = -r.GetDouble(1); // bm25: lower is better
        return scores;
    }
}
