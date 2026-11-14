using Blade.Core;
using Blade.Server.Data;

namespace Blade.Server.Search;

/// <summary>
/// Crime-analyst style series detection over a filtered result set:
///  1. Seeds = results already tied together by hard entity links (same vehicle near scenes, pawned serials, shared people/phones).
///  2. Expand by M.O.: other results that share the seeds' common M.O. tags AND whose narrative embedding is about as
///     close to the seeds' centroid as the seeds themselves are.
/// Embedding similarity alone was measured to be unreliable for this (templated report language chains everything
/// together); anchoring on entity evidence first fixes that.
/// </summary>
public static class SeriesDetector
{
    public const double Tolerance = 0.03;

    public static SeriesFinding? Detect(IReadOnlyList<ScoredIncident> results, IReadOnlyList<LinkFinding> links, VectorIndex vectors)
    {
        var resultCases = results.Select(r => r.Incident.CaseNumber).ToHashSet();
        var seedReasons = new Dictionary<string, List<string>>();
        foreach (var f in links.Where(l => l.Kind is "VEHICLE" or "PERSON" or "PHONE"))
            foreach (var c in f.CaseNumbers.Where(resultCases.Contains))
                (seedReasons.TryGetValue(c, out var l) ? l : seedReasons[c] = []).Add($"{f.Kind.ToLowerInvariant()} {f.EntityLabel}");
        if (seedReasons.Count < 2) return null;

        var byCase = results.ToDictionary(r => r.Incident.CaseNumber, r => r.Incident);
        var seeds = seedReasons.Keys.ToList();
        var common = seeds.Select(c => byCase[c].MoTags.AsEnumerable()).Aggregate((a, b) => a.Intersect(b)).ToList();
        var offense = seeds.GroupBy(c => byCase[c].OffenseType).OrderByDescending(gp => gp.Count()).First().Key;

        var members = seeds.Select(c => new SeriesMember(c, "Entity-linked: " + string.Join("; ", seedReasons[c].Distinct()))).ToList();

        var seedVectors = seeds.Select(c => vectors.Get("INCIDENT", c)).Where(v => v is not null).Cast<float[]>().ToList();
        if (seedVectors.Count >= 2)
        {
            var centroid = VectorIndex.Centroid(seedVectors);
            var threshold = seedVectors.Min(v => VectorIndex.Dot(v, centroid)) - Tolerance;
            foreach (var r in results)
            {
                var i = r.Incident;
                if (seedReasons.ContainsKey(i.CaseNumber) || i.OffenseType != offense) continue;
                if (common.Count > 0 && !common.All(i.MoTags.Contains)) continue;
                if (vectors.Get("INCIDENT", i.CaseNumber) is not { } v) continue;
                var sim = VectorIndex.Dot(v, centroid);
                if (sim >= threshold)
                    members.Add(new SeriesMember(i.CaseNumber, $"Same M.O. ({string.Join(", ", common)}) and narrative similarity {sim:F2} to the linked cases (threshold {threshold:F2})"));
            }
        }

        if (members.Count < 3) return null;
        members = members.OrderBy(m => byCase[m.CaseNumber].OccurredFrom).ToList();
        var linked = members.Count(m => m.Reason.StartsWith("Entity"));
        var first = byCase[members[0].CaseNumber].OccurredFrom;
        var last = byCase[members[^1].CaseNumber].OccurredFrom;
        var summary = $"Possible series of {members.Count} incidents between {first:yyyy-MM-dd} and {last:yyyy-MM-dd}: " +
                      $"{linked} tied together by shared entities and {members.Count - linked} added on matching M.O. and narrative similarity. Requires analyst confirmation.";
        return new SeriesFinding(members, common, summary);
    }
}
