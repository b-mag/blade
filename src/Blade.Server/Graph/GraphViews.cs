using Blade.Core;
using Blade.Server.Data;

namespace Blade.Server.Graph;

/// <summary>Builds visual subgraphs for the Link Explorer (role-filtered; restricted incidents never appear).</summary>
public sealed class GraphViews(EntityGraph g, BladeData d)
{
    public const int MaxNodes = 70;

    private static readonly HashSet<string> NoisyHubs = ["OCCURRED_AT", "LIVES_AT"];

    /// <summary>Resolves free text to a node: case number, plate, phone, FI/booking/pawn number, or person name.</summary>
    public string? Resolve(string q)
    {
        q = q.Trim();
        if (q.Length == 0) return null;
        foreach (var id in new[] { EntityGraph.Inc(q), EntityGraph.Veh(q.ToUpperInvariant()), EntityGraph.Ph(q), EntityGraph.Fi(q.ToUpperInvariant()), EntityGraph.Bk(q.ToUpperInvariant()), EntityGraph.Pt(q.ToUpperInvariant()) })
            if (g.Nodes.ContainsKey(id)) return id;
        if (g.Nodes.ContainsKey(q)) return q;
        var person = d.Persons.Values
            .Where(p => p.FullName.Contains(q, StringComparison.OrdinalIgnoreCase) || $"{p.First} {p.Last}".Equals(q, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => g.EdgesOf(EntityGraph.Per(p.Id)).Count)
            .FirstOrDefault();
        if (person is not null) return EntityGraph.Per(person.Id);
        var location = d.Locations.Values
            .Where(l => l.Display.Contains(q, StringComparison.OrdinalIgnoreCase) && g.Nodes.ContainsKey(EntityGraph.Loc(l.Id)))
            .OrderByDescending(l => g.EdgesOf(EntityGraph.Loc(l.Id)).Count)
            .FirstOrDefault();
        return location is null ? null : EntityGraph.Loc(location.Id);
    }

    public GraphView Neighborhood(string nodeId, string role, int depth = 2)
    {
        var allowed = Roles.AllowedSensitivities(role);
        var withheld = 0;
        var nodes = new HashSet<string> { nodeId };
        var frontier = new List<string> { nodeId };
        var edges = new HashSet<GraphEdge>();
        for (var level = 0; level < depth && nodes.Count < MaxNodes; level++)
        {
            var next = new List<string>();
            foreach (var n in frontier)
            {
                // Expanding through very busy nodes (a shared business address, a big apartment block) explodes the view.
                var es = g.EdgesOf(n);
                if (level > 0 && es.Count > 25) continue;
                foreach (var e in es.OrderByDescending(Weight))
                {
                    if (level > 0 && NoisyHubs.Contains(e.Type)) continue;
                    var other = e.Other(n);
                    if (!Visible(other, allowed)) { withheld++; continue; }
                    if (!nodes.Contains(other))
                    {
                        if (nodes.Count >= MaxNodes) break;
                        nodes.Add(other);
                        next.Add(other);
                    }
                    edges.Add(e);
                }
            }
            frontier = next;
        }
        // Include edges between already-present nodes (e.g., co-arrested, LPR near scene).
        foreach (var n in nodes)
            foreach (var e in g.EdgesOf(n))
                if (nodes.Contains(e.Other(n))) edges.Add(e);

        return new GraphView(nodes.Select(n => Dto(n, n == nodeId)).ToList(), edges.Select(Dto).ToList(), g.Nodes[nodeId].Label, withheld);
    }

    /// <summary>Graph for an Ask BLADE answer: the incidents in the series/links plus every connector and its context.</summary>
    public GraphView ForFindings(IEnumerable<LinkFinding> findings, IEnumerable<string> focusCases, string role)
    {
        var allowed = Roles.AllowedSensitivities(role);
        var focus = focusCases.Select(EntityGraph.Inc).ToHashSet();
        var nodes = new HashSet<string>(focus);
        foreach (var f in findings.Where(f => f.Kind != "LOCATION"))
        {
            nodes.Add(f.EntityId);
            foreach (var c in f.CaseNumbers) nodes.Add(EntityGraph.Inc(c));
            foreach (var id in f.SourceIds)
            {
                var n = new[] { EntityGraph.Inc(id), EntityGraph.Fi(id), EntityGraph.Bk(id), EntityGraph.Pt(id) }.FirstOrDefault(g.Nodes.ContainsKey);
                if (n is not null) nodes.Add(n);
            }
        }
        // One hop of context around connectors (owner, co-residents, co-arrestees, FI subjects).
        foreach (var n in nodes.ToList().Where(n => n.StartsWith("V:") || n.StartsWith("P:") || n.StartsWith("FI:") || n.StartsWith("B:") || n.StartsWith("PT:")))
            foreach (var e in g.EdgesOf(n).Where(e => e.Type is "REGISTERED_OWNER" or "FI_SUBJECT" or "CO_ARRESTED" or "BOOKED" or "SOLD_BY" or "PHONE_OF"))
                nodes.Add(e.Other(n));
        nodes.RemoveWhere(n => !Visible(n, allowed));

        var edges = nodes.SelectMany(n => g.EdgesOf(n)).Where(e => nodes.Contains(e.A) && nodes.Contains(e.B) && !NoisyHubs.Contains(e.Type)).ToHashSet();
        // Show co-residence explicitly as a person-to-person relationship.
        var extra = new List<GraphEdgeDto>();
        var people = nodes.Where(n => n.StartsWith("P:")).Select(n => d.Persons[int.Parse(n[2..])]).ToList();
        foreach (var a in people)
            foreach (var b in people.Where(b => b.Id > a.Id && b.HomeLocationId is not null && b.HomeLocationId == a.HomeLocationId))
                extra.Add(new GraphEdgeDto(EntityGraph.Per(a.Id), EntityGraph.Per(b.Id), "SAME_ADDRESS", $"same address ({d.Locations[a.HomeLocationId!.Value].Address})"));

        var nodeDtos = nodes.Select(n => Dto(n, focus.Contains(n))).ToList();
        // A hub node for the detected series keeps M.O.-only members connected to the picture.
        if (focus.Count >= 2)
        {
            nodeDtos.Add(new GraphNodeDto("S:series", "SERIES", $"Possible series\n{focus.Count} incidents", "System-detected: entity links + matching M.O.", true));
            extra.AddRange(focus.Where(nodes.Contains).Select(f => new GraphEdgeDto("S:series", f, "SERIES_MEMBER", "series member")));
        }
        return new GraphView(nodeDtos, edges.Select(Dto).Concat(extra).ToList(), "Connections for this answer");
    }

    private bool Visible(string node, string[] allowed) => g.IncidentOf(node) is not { } i || allowed.Contains(i.Sensitivity);

    private static int Weight(GraphEdge e) => e.Type switch
    {
        "LPR_NEAR_SCENE" or "SERIAL_MATCH" or "CO_ARRESTED" or "FI_VEHICLE" or "FI_SUBJECT" => 5,
        "REGISTERED_OWNER" or "PHONE_OF" or "PHONE_MENTIONED" or "SOLD_BY" or "BOOKED" => 4,
        "INVOLVED" or "VEHICLE_IN_REPORT" or "PLATE_MENTIONED" => 3,
        _ => 1,
    };

    private GraphNodeDto Dto(string id, bool focus)
    {
        var n = g.Nodes[id];
        string? detail = null;
        if (g.IncidentOf(id) is { } i) detail = $"{i.OccurredFrom:yyyy-MM-dd HH:mm} · {d.Locations[i.LocationId].Display} · {i.Status}";
        else if (id.StartsWith("P:") && d.Persons.TryGetValue(int.Parse(id[2..]), out var p)) detail = p.Notes;
        else if (id.StartsWith("V:") && d.VehiclesByPlate.TryGetValue(id[2..], out var v)) detail = v.Notes;
        return new GraphNodeDto(id, n.Kind.ToString().ToUpperInvariant(), ShortLabel(n), detail, focus);
    }

    private static string ShortLabel(GraphNode n) => n.Kind switch
    {
        NodeKind.Incident => n.Label.Length > 34 ? n.Label[..34] + "…" : n.Label,
        NodeKind.FieldInterview or NodeKind.Booking or NodeKind.Pawn => n.Label.Split(' ')[0] + "\n" + string.Join(' ', n.Label.Split(' ').Skip(1)),
        _ => n.Label,
    };

    private static GraphEdgeDto Dto(GraphEdge e) => new(e.A, e.B, e.Type, e.Type switch
    {
        "LPR_NEAR_SCENE" => "LPR near scene",
        "SERIAL_MATCH" => "serial match",
        "REGISTERED_OWNER" => "registered owner",
        "CO_ARRESTED" => "co-arrested",
        "FI_SUBJECT" => "field interview",
        "FI_VEHICLE" => "vehicle on FI",
        "SOLD_BY" => "sold/pawned by",
        "PHONE_OF" => "phone of",
        "PHONE_MENTIONED" => "phone in narrative",
        "PLATE_MENTIONED" => "plate in narrative",
        "SELLER_PHONE" => "seller phone",
        "BOOKED" => "booked",
        "BOOKING_FOR" => "booking for",
        "INVOLVED" => (e.Role ?? "involved").ToLowerInvariant().Replace('_', ' '),
        "VEHICLE_IN_REPORT" => (e.Role ?? "vehicle").ToLowerInvariant().Replace('_', ' '),
        _ => e.Type.ToLowerInvariant().Replace('_', ' '),
    });
}
