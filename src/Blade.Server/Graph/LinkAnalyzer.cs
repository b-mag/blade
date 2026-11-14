using Blade.Core;
using Blade.Server.Answer;
using Blade.Server.Data;

namespace Blade.Server.Graph;

public sealed record LinkAnalysis(List<LinkFinding> Findings, HashSet<string> CitableIds, int WithheldContextRecords);

/// <summary>
/// Finds entities (vehicles, people, phones, places) that connect two or more of a set of incidents, explains each
/// connection with evidence from the source systems, and surfaces "bridge" people who appear around several
/// connectors. This is deterministic — the LLM later narrates these facts; it does not invent them.
/// </summary>
public sealed class LinkAnalyzer(EntityGraph g, BladeData d)
{
    private sealed record Reach(string Case, string Route, List<GraphEdge> Path);

    public LinkAnalysis Analyze(IReadOnlyCollection<string> caseNumbers, string role)
    {
        var allowed = Roles.AllowedSensitivities(role);
        var inSet = caseNumbers.ToHashSet();
        var reach = new Dictionary<string, List<Reach>>();
        void Add(string node, string c, string route, params GraphEdge[] path)
        {
            if (!reach.TryGetValue(node, out var l)) reach[node] = l = [];
            l.Add(new Reach(c, route, [.. path]));
        }

        foreach (var c in inSet)
        {
            var inode = EntityGraph.Inc(c);
            foreach (var e in g.EdgesOf(inode).Where(x => x.IsInvestigative))
            {
                var n = e.Other(inode);
                switch (g.Nodes[n].Kind)
                {
                    case NodeKind.Vehicle:
                        Add(n, c, n, e);
                        foreach (var e2 in g.EdgesOf(n).Where(x => x.Type == "REGISTERED_OWNER")) Add(e2.Other(n), c, n, e, e2);
                        break;
                    case NodeKind.Phone:
                        Add(n, c, n, e);
                        foreach (var e2 in g.EdgesOf(n).Where(x => x.Type == "PHONE_OF")) Add(e2.Other(n), c, n, e, e2);
                        break;
                    case NodeKind.Person:
                    case NodeKind.Location:
                        Add(n, c, n, e);
                        break;
                    case NodeKind.Pawn when e.Type == "SERIAL_MATCH":
                        foreach (var e2 in g.EdgesOf(n).Where(x => x.Type is "SOLD_BY" or "SELLER_PHONE")) Add(e2.Other(n), c, n, e, e2);
                        break;
                }
            }
        }

        var connectors = reach.Where(kv => kv.Value.Select(r => r.Case).Distinct().Count() >= 2).ToDictionary(kv => kv.Key, kv => kv.Value);
        // Suppress entities reached only *through* another connector (e.g., a van's registered owner) — shown as context instead.
        var suppressed = connectors.Keys.Where(x =>
        {
            var routes = connectors[x].Select(r => r.Route).Distinct().ToList();
            return routes.Count == 1 && routes[0] != x && connectors.ContainsKey(routes[0]);
        }).ToHashSet();

        var findings = new List<(LinkFinding Finding, HashSet<int> ContextPersons, Dictionary<int, string> PersonRoles)>();
        var withheld = 0;
        foreach (var (node, reaches) in connectors.Where(kv => !suppressed.Contains(kv.Key))
                     .OrderByDescending(kv => kv.Value.Select(r => r.Case).Distinct().Count())
                     .ThenBy(kv => KindOrder(g.Nodes[kv.Key].Kind)))
        {
            var kind = g.Nodes[node].Kind;
            var cases = reaches.Select(r => r.Case).Distinct().OrderBy(x => x).ToList();
            var evidence = reaches.SelectMany(r => r.Path).Select(e => e.Evidence).Distinct().ToList();
            var (context, persons, roles, hidden) = Expand(node, inSet, allowed);
            withheld += hidden;
            if (kind == NodeKind.Person) roles[int.Parse(node[2..])] = string.Join(", ", reaches.SelectMany(r => r.Path).Select(e => Describe(e.Type)).Distinct().Where(s => !s.StartsWith("registered") && !s.StartsWith("listed owner")));
            var label = kind switch
            {
                NodeKind.Person => d.Persons[int.Parse(node[2..])].FullName,
                NodeKind.Vehicle => g.Nodes[node].Label,
                _ => g.Nodes[node].Label,
            };
            var how = string.Join(", ", reaches.SelectMany(r => r.Path).Select(e => Describe(e.Type)).Distinct());
            var summary = $"{KindName(kind)} {label} connects {cases.Count} of these incidents ({how}).";
            var all = evidence.Concat(context).ToList();
            var ids = all.SelectMany(CitationGuard.ExtractRecordIds).Concat(cases).ToHashSet();
            findings.Add((new LinkFinding(kind.ToString().ToUpperInvariant(), node, label, summary, cases, all, [.. ids]), persons, roles));
        }

        // Bridges: people who sit in the context of two or more connectors.
        var bridges = new List<LinkFinding>();
        var personHits = findings
            .SelectMany((f, idx) => f.ContextPersons.Select(p => (p, idx)))
            .GroupBy(x => x.p)
            .Where(gp => gp.Select(x => x.idx).Distinct().Count() >= 2);
        foreach (var gp in personHits)
        {
            var person = d.Persons[gp.Key];
            var parts = gp.Select(x => x.idx).Distinct()
                .Select(idx => findings[idx].Finding.EntityId == EntityGraph.Per(gp.Key)
                    ? $"{findings[idx].PersonRoles[gp.Key]} in {string.Join(", ", findings[idx].Finding.CaseNumbers)}"
                    : $"{findings[idx].PersonRoles[gp.Key]} ({findings[idx].Finding.EntityLabel})").ToList();
            var cases = gp.SelectMany(x => findings[x.idx].Finding.CaseNumbers).Distinct().OrderBy(x => x).ToList();
            var ids = gp.SelectMany(x => findings[x.idx].Finding.SourceIds).ToHashSet();
            bridges.Add(new LinkFinding("BRIDGE", EntityGraph.Per(person.Id), person.FullName,
                $"{person.FullName} links {parts.Count} separate connections: {string.Join("; ", parts)}.",
                cases, parts, [.. ids]));
        }

        var result = bridges.Concat(findings.Select(f => f.Finding)).ToList();
        var citable = result.SelectMany(f => f.SourceIds).ToHashSet();
        return new LinkAnalysis(result, citable, withheld);
    }

    private (List<string> Lines, HashSet<int> Persons, Dictionary<int, string> Roles, int Withheld) Expand(string node, HashSet<string> inSet, string[] allowed)
    {
        var lines = new List<string>();
        var persons = new HashSet<int>();
        var roles = new Dictionary<int, string>();
        var withheld = 0;
        void Person(int id, string role) { persons.Add(id); roles.TryAdd(id, role); }
        bool Visible(string incNode, out IncidentRecord? i)
        {
            i = g.IncidentOf(incNode);
            if (i is null) return true;
            if (allowed.Contains(i.Sensitivity)) return true;
            withheld++;
            return false;
        }

        var kind = g.Nodes[node].Kind;
        foreach (var e in g.EdgesOf(node))
        {
            var other = e.Other(node);
            switch (kind, e.Type)
            {
                case (NodeKind.Vehicle, "REGISTERED_OWNER"):
                {
                    var owner = d.Persons[int.Parse(other[2..])];
                    lines.Add(e.Evidence + ".");
                    Person(owner.Id, "registered owner of the vehicle");
                    if (owner.HomeLocationId is { } h)
                        foreach (var co in d.Persons.Values.Where(p => p.HomeLocationId == h && p.Id != owner.Id))
                        {
                            lines.Add($"{co.FullName} lives at the same address as the registered owner ({d.Locations[h].Address}).");
                            Person(co.Id, "lives with the vehicle's registered owner");
                        }
                    break;
                }
                case (NodeKind.Vehicle, "FI_VEHICLE"):
                {
                    var fi = d.FieldInterviews.Values.First(f => f.FiNumber == e.SourceId);
                    lines.Add($"{e.Evidence}. Notes: {NarrativeText.Truncate(fi.Notes, 260)}");
                    Person(fi.PersonId, $"field-interviewed with the vehicle ({fi.FiNumber})");
                    break;
                }
                case (NodeKind.Vehicle, "VEHICLE_IN_REPORT" or "PLATE_MENTIONED"):
                case (NodeKind.Person, "INVOLVED"):
                case (NodeKind.Phone, "PHONE_MENTIONED"):
                    if (other.StartsWith("I:") && inSet.Contains(other[2..])) break;
                    if (!Visible(other, out var inc)) break;
                    lines.Add(inc is null ? e.Evidence + "." : $"{e.Evidence} — {inc.OffenseDescription}, {inc.OccurredFrom:yyyy-MM-dd}, {d.Locations[inc.LocationId].Display}, status {inc.Status}.");
                    break;
                case (NodeKind.Person, "BOOKED"):
                {
                    var b = d.Bookings.Values.First(x => x.BookingNumber == e.SourceId);
                    lines.Add(e.Evidence + (b.CoArrestees.Length > 0 ? $" Co-arrested with {string.Join(", ", b.CoArrestees.Select(c => d.Persons[c].FullName))}." : "."));
                    foreach (var c in b.CoArrestees) Person(c, $"co-arrested with {g.Nodes[node].Label} ({b.BookingNumber})");
                    break;
                }
                case (NodeKind.Person, "FI_SUBJECT"):
                {
                    var fi = d.FieldInterviews.Values.First(f => f.FiNumber == e.SourceId);
                    lines.Add($"{e.Evidence}. Notes: {NarrativeText.Truncate(fi.Notes, 220)}");
                    break;
                }
                case (NodeKind.Person, "SOLD_BY"):
                case (NodeKind.Person, "PHONE_OF"):
                case (NodeKind.Phone, "SELLER_PHONE"):
                    lines.Add(e.Evidence + ".");
                    break;
                case (NodeKind.Phone, "PHONE_OF"):
                    lines.Add(e.Evidence + ".");
                    Person(int.Parse(other[2..]), $"listed owner of phone {g.Nodes[node].Label}");
                    break;
            }
        }
        if (kind == NodeKind.Person) persons.Add(int.Parse(node[2..]));
        return (lines.Distinct().Take(14).ToList(), persons, roles, withheld);
    }

    private static int KindOrder(NodeKind k) => k switch { NodeKind.Vehicle => 0, NodeKind.Person => 1, NodeKind.Phone => 2, _ => 3 };
    private static string KindName(NodeKind k) => k switch { NodeKind.Vehicle => "Vehicle", NodeKind.Person => "Person", NodeKind.Phone => "Phone number", NodeKind.Location => "Location", _ => k.ToString() };

    private static string Describe(string edgeType) => edgeType switch
    {
        "LPR_NEAR_SCENE" => "LPR reads near the scenes",
        "VEHICLE_IN_REPORT" => "listed in reports",
        "PLATE_MENTIONED" => "plate written in narratives",
        "REGISTERED_OWNER" => "registered owner of a linked vehicle",
        "PHONE_MENTIONED" => "phone number written in narratives",
        "PHONE_OF" => "listed owner of a linked phone",
        "SERIAL_MATCH" => "pawned/sold items whose serial numbers match stolen property",
        "SOLD_BY" => "seller on matching pawn tickets",
        "SELLER_PHONE" => "seller phone on matching pawn tickets",
        "INVOLVED" => "named in reports",
        "OCCURRED_AT" => "same location",
        _ => edgeType.ToLowerInvariant(),
    };
}
