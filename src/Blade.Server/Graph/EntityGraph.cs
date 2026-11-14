using System.Text.RegularExpressions;
using Blade.Server.Data;

namespace Blade.Server.Graph;

public enum NodeKind { Incident, Person, Vehicle, Phone, Location, FieldInterview, Booking, Pawn }

public sealed record GraphNode(string Id, NodeKind Kind, string Label, string? Sensitivity = null);

/// <summary>An edge carries its own evidence text and, when possible, the citable record it came from.</summary>
public sealed record GraphEdge(string A, string B, string Type, string Evidence, DateTime? When, string? SourceId, string? Role = null)
{
    /// <summary>Victims and reporting parties are not investigative links between incidents.</summary>
    public bool IsInvestigative => Role is null || Role.Split('/').Any(r => r is "SUSPECT" or "ARRESTEE" or "WITNESS"
        or "SUSPECT_VEHICLE" or "STOLEN" or "OBSERVED");

    public string Other(string id) => id == A ? B : A;
}

/// <summary>
/// The "Graph" in GraphRAG: people, vehicles, phones, places, incidents, field interviews, bookings and pawn
/// transactions from otherwise siloed systems, joined by explicit relationships. Some edges come straight from
/// structured data (involved-in, registered owner); others are derived — plates and phone numbers extracted from
/// free-text narratives, LPR reads that occurred near an incident in time and place, and pawned serial numbers
/// that match reported stolen property.
/// </summary>
public sealed partial class EntityGraph
{
    public Dictionary<string, GraphNode> Nodes { get; } = [];
    public Dictionary<string, List<GraphEdge>> Adjacency { get; } = [];
    public int EdgeCount { get; private set; }
    private readonly BladeData _d;

    public static string Inc(string caseNumber) => "I:" + caseNumber;
    public static string Per(int id) => "P:" + id;
    public static string Veh(string plate) => "V:" + plate;
    public static string Ph(string phone) => "PH:" + phone;
    public static string Loc(int id) => "L:" + id;
    public static string Fi(string n) => "FI:" + n;
    public static string Bk(string n) => "B:" + n;
    public static string Pt(string n) => "PT:" + n;

    [GeneratedRegex(@"\b\d{3}[A-Z]{3}\b")] private static partial Regex PlateRx();
    [GeneratedRegex(@"\(\d{3}\) 555-01\d{2}")] public static partial Regex PhoneRx();

    /// <summary>LPR read within this window of an incident's occurred time (same district) creates a "near scene" edge.</summary>
    public static readonly TimeSpan LprWindowBefore = TimeSpan.FromMinutes(45), LprWindowAfter = TimeSpan.FromMinutes(60);

    public EntityGraph(BladeData d)
    {
        _d = d;
        foreach (var i in d.IncidentsById.Values)
            Node(Inc(i.CaseNumber), NodeKind.Incident, $"{i.CaseNumber} {i.OffenseDescription}", i.Sensitivity);
        foreach (var p in d.Persons.Values) Node(Per(p.Id), NodeKind.Person, p.FullName);
        foreach (var v in d.Vehicles.Values) Node(Veh(v.Plate), NodeKind.Vehicle, $"{v.Plate} ({v.Description})");

        // RMS: people, vehicles, places
        foreach (var g in d.IncidentPersons.GroupBy(x => (x.IncidentId, x.PersonId)))
        {
            var i = d.IncidentsById[g.Key.IncidentId];
            var roles = string.Join("/", g.Select(x => x.Role).Distinct());
            Edge(Inc(i.CaseNumber), Per(g.Key.PersonId), "INVOLVED", $"{d.Persons[g.Key.PersonId].FullName} listed as {roles} in {i.CaseNumber}", i.OccurredFrom, i.CaseNumber, roles);
        }
        foreach (var x in d.IncidentVehicles)
        {
            var i = d.IncidentsById[x.IncidentId];
            var v = d.Vehicles[x.VehicleId];
            Edge(Inc(i.CaseNumber), Veh(v.Plate), "VEHICLE_IN_REPORT", $"{v.Plate} listed as {x.Role} in {i.CaseNumber}", i.OccurredFrom, i.CaseNumber, x.Role);
        }
        foreach (var i in d.IncidentsById.Values)
        {
            var loc = d.Locations[i.LocationId];
            Node(Loc(loc.Id), NodeKind.Location, loc.Display);
            Edge(Inc(i.CaseNumber), Loc(loc.Id), "OCCURRED_AT", $"{i.CaseNumber} occurred at {loc.Display}", i.OccurredFrom, i.CaseNumber);

            // Entity extraction from unstructured narrative text
            foreach (var plate in PlateRx().Matches(i.Narrative).Select(m => m.Value).Distinct())
                if (d.VehiclesByPlate.ContainsKey(plate) && !Linked(Inc(i.CaseNumber), Veh(plate)))
                    Edge(Inc(i.CaseNumber), Veh(plate), "PLATE_MENTIONED", $"Plate {plate} mentioned in the narrative of {i.CaseNumber}", i.OccurredFrom, i.CaseNumber);
            foreach (var phone in PhoneRx().Matches(i.Narrative).Select(m => m.Value).Distinct())
            {
                Node(Ph(phone), NodeKind.Phone, phone);
                Edge(Inc(i.CaseNumber), Ph(phone), "PHONE_MENTIONED", $"Phone {phone} mentioned in the narrative of {i.CaseNumber}", i.OccurredFrom, i.CaseNumber);
            }
        }
        foreach (var p in d.Persons.Values)
        {
            if (p.HomeLocationId is { } h)
            {
                Node(Loc(h), NodeKind.Location, d.Locations[h].Display);
                Edge(Per(p.Id), Loc(h), "LIVES_AT", $"{p.FullName} lives at {d.Locations[h].Address}", null, null);
            }
            if (p.Phone is { } ph)
            {
                Node(Ph(ph), NodeKind.Phone, ph);
                Edge(Per(p.Id), Ph(ph), "PHONE_OF", $"{ph} is the listed phone for {p.FullName}", null, null);
            }
        }
        foreach (var v in d.Vehicles.Values)
            if (v.OwnerPersonId is { } o)
                Edge(Veh(v.Plate), Per(o), "REGISTERED_OWNER", $"{v.Plate} ({v.Description}) is registered to {d.Persons[o].FullName}", null, null);

        // Field interviews
        foreach (var f in d.FieldInterviews.Values)
        {
            Node(Fi(f.FiNumber), NodeKind.FieldInterview, $"{f.FiNumber} field interview {f.ContactAt:yyyy-MM-dd}");
            var who = d.Persons[f.PersonId].FullName;
            Edge(Fi(f.FiNumber), Per(f.PersonId), "FI_SUBJECT", $"{who} contacted on field interview {f.FiNumber} ({f.ContactAt:yyyy-MM-dd HH:mm}): {f.Reason}", f.ContactAt, f.FiNumber);
            if (f.VehicleId is { } vid)
            {
                var v = d.Vehicles[vid];
                Edge(Fi(f.FiNumber), Veh(v.Plate), "FI_VEHICLE", $"{v.Plate} recorded on field interview {f.FiNumber} ({f.ContactAt:yyyy-MM-dd HH:mm}) with {who}", f.ContactAt, f.FiNumber);
            }
            foreach (var phone in PhoneRx().Matches(f.Notes).Select(m => m.Value).Distinct())
            {
                Node(Ph(phone), NodeKind.Phone, phone);
                Edge(Fi(f.FiNumber), Ph(phone), "PHONE_MENTIONED", $"Phone {phone} recorded on field interview {f.FiNumber}", f.ContactAt, f.FiNumber);
            }
        }

        // Jail bookings
        foreach (var b in d.Bookings.Values)
        {
            Node(Bk(b.BookingNumber), NodeKind.Booking, $"{b.BookingNumber} booking {b.BookedAt:yyyy-MM-dd}");
            var who = d.Persons[b.PersonId].FullName;
            Edge(Bk(b.BookingNumber), Per(b.PersonId), "BOOKED", $"{who} booked {b.BookedAt:yyyy-MM-dd} on {b.BookingNumber}: {b.Charges}", b.BookedAt, b.BookingNumber);
            if (b.IncidentId is { } iid)
                Edge(Bk(b.BookingNumber), Inc(d.IncidentsById[iid].CaseNumber), "BOOKING_FOR", $"Booking {b.BookingNumber} relates to {d.IncidentsById[iid].CaseNumber}", b.BookedAt, b.BookingNumber);
            foreach (var co in b.CoArrestees.Where(co => co > b.PersonId))
                Edge(Per(b.PersonId), Per(co), "CO_ARRESTED", $"{who} and {d.Persons[co].FullName} were arrested together ({b.BookingNumber}, {b.BookedAt:yyyy-MM-dd})", b.BookedAt, b.BookingNumber);
        }

        // Pawn / scrap transactions, including serial-number matches to stolen property
        var stolenBySerial = d.Property.Where(p => p.Serial is not null).GroupBy(p => p.Serial!).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var p in d.Pawns.Values)
        {
            Node(Pt(p.TicketNumber), NodeKind.Pawn, $"{p.TicketNumber} {p.TransactionType.ToLowerInvariant()} {p.TransactionAt:yyyy-MM-dd}");
            var who = d.Persons[p.PersonId].FullName;
            var shop = d.Locations[p.ShopLocationId].BusinessName;
            Edge(Pt(p.TicketNumber), Per(p.PersonId), "SOLD_BY", $"{who} sold/pawned \"{p.ItemDescription}\" at {shop} on {p.TransactionAt:yyyy-MM-dd} ({p.TicketNumber})", p.TransactionAt, p.TicketNumber);
            if (p.SellerPhone is { } ph)
            {
                Node(Ph(ph), NodeKind.Phone, ph);
                Edge(Pt(p.TicketNumber), Ph(ph), "SELLER_PHONE", $"Seller phone {ph} recorded on {p.TicketNumber}", p.TransactionAt, p.TicketNumber);
            }
            if (p.Serial is { } s && stolenBySerial.TryGetValue(s, out var stolen))
                foreach (var item in stolen)
                {
                    var i = d.IncidentsById[item.IncidentId];
                    Edge(Pt(p.TicketNumber), Inc(i.CaseNumber), "SERIAL_MATCH",
                        $"Serial {s} on {p.TicketNumber} ({p.ItemDescription}, {p.TransactionAt:yyyy-MM-dd}) matches property reported stolen in {i.CaseNumber}", p.TransactionAt, p.TicketNumber);
                }
        }

        // LPR reads near incident scenes (same district, inside the time window)
        var byDistrict = d.IncidentsById.Values.GroupBy(i => i.District).ToDictionary(g => g.Key, g => g.OrderBy(i => i.OccurredFrom).ToList());
        foreach (var r in d.LprReads)
        {
            if (r.VehicleId is null) continue;
            var cam = d.LprCameras[r.CameraId];
            if (!byDistrict.TryGetValue(cam.District, out var list)) continue;
            foreach (var i in list) // incidents ascending by time, so delta shrinks as we go
            {
                var delta = r.ReadAt - i.OccurredFrom;
                if (delta > LprWindowAfter) continue;   // incident long before the read
                if (delta < -LprWindowBefore) break;    // this and all later incidents are long after the read
                var minutes = (int)Math.Round(delta.TotalMinutes);
                Edge(Veh(r.Plate), Inc(i.CaseNumber), "LPR_NEAR_SCENE",
                    $"LPR camera {cam.Name} read {r.Plate} at {r.ReadAt:yyyy-MM-dd HH:mm}, {Math.Abs(minutes)} min {(minutes < 0 ? "before" : "after")} the estimated time of {i.CaseNumber}",
                    r.ReadAt, i.CaseNumber);
            }
        }
    }

    private void Node(string id, NodeKind kind, string label, string? sensitivity = null) => Nodes.TryAdd(id, new GraphNode(id, kind, label, sensitivity));

    private void Edge(string a, string b, string type, string evidence, DateTime? when, string? source, string? role = null)
    {
        var e = new GraphEdge(a, b, type, evidence, when, source, role);
        (Adjacency.TryGetValue(a, out var la) ? la : Adjacency[a] = []).Add(e);
        (Adjacency.TryGetValue(b, out var lb) ? lb : Adjacency[b] = []).Add(e);
        EdgeCount++;
    }

    private bool Linked(string a, string b) => Adjacency.TryGetValue(a, out var l) && l.Any(e => e.Other(a) == b);

    public IReadOnlyList<GraphEdge> EdgesOf(string id) => Adjacency.TryGetValue(id, out var l) ? l : [];

    public IncidentRecord? IncidentOf(string nodeId) =>
        nodeId.StartsWith("I:") && _d.IncidentsByCase.TryGetValue(nodeId[2..], out var i) ? i : null;
}
