using Microsoft.Data.Sqlite;

namespace Blade.Server.Data;

/// <summary>
/// Read-only view of the agency data (simulating read replicas of RMS / CAD / JMS / pawn systems).
/// Reference data is cached in memory at startup; searches still run as parameterized SQL against the replica.
/// The evaluation ground-truth table is deliberately never read here.
/// </summary>
public sealed class BladeData
{
    public string DbPath { get; }
    public DateOnly DataAsOf { get; private set; }

    public Dictionary<string, IncidentRecord> IncidentsByCase { get; } = [];
    public Dictionary<int, IncidentRecord> IncidentsById { get; } = [];
    public Dictionary<int, LocationRecord> Locations { get; } = [];
    public Dictionary<int, PersonRecord> Persons { get; } = [];
    public Dictionary<int, VehicleRecord> Vehicles { get; } = [];
    public Dictionary<string, VehicleRecord> VehiclesByPlate { get; } = [];
    public Dictionary<int, DeputyRecord> Deputies { get; } = [];
    public List<IncidentPersonLink> IncidentPersons { get; } = [];
    public List<IncidentVehicleLink> IncidentVehicles { get; } = [];
    public List<PropertyRecord> Property { get; } = [];
    public Dictionary<int, FieldInterviewRecord> FieldInterviews { get; } = [];
    public Dictionary<int, BookingRecord> Bookings { get; } = [];
    public Dictionary<int, PawnRecord> Pawns { get; } = [];
    public Dictionary<int, LprCameraRecord> LprCameras { get; } = [];
    public List<LprReadRecord> LprReads { get; } = [];
    public Dictionary<int, CadCallRecord> CadByIncident { get; } = [];
    public List<PolicySectionRecord> PolicySections { get; } = [];
    public Dictionary<string, PolicySectionRecord> PolicyBySection { get; } = [];
    public VectorIndex Vectors { get; } = new();
    public string? EmbeddingModel { get; private set; }

    public BladeData(string dbPath)
    {
        if (!File.Exists(dbPath))
            throw new FileNotFoundException($"BLADE database not found at {dbPath}. Run: dotnet run --project src/Blade.DataGen", dbPath);
        DbPath = dbPath;
        Load();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        c.Open();
        return c;
    }

    private static DateTime Dt(SqliteDataReader r, int i) => DateTime.Parse(r.GetString(i));
    private static DateTime? DtN(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : DateTime.Parse(r.GetString(i));
    private static string? S(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static int? IN(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);

    private void Each(SqliteConnection c, string sql, Action<SqliteDataReader> row)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        while (r.Read()) row(r);
    }

    private void Load()
    {
        using var c = Open();
        Each(c, "SELECT value FROM meta WHERE key='data_as_of'", r => DataAsOf = DateOnly.Parse(r.GetString(0)));

        Each(c, "SELECT id, address, city, district, premise_type, business_name, business_type, x, y FROM locations",
            r => Locations[r.GetInt32(0)] = new(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), S(r, 5), S(r, 6), r.GetDouble(7), r.GetDouble(8)));

        Each(c, "SELECT id, first_name, middle_name, last_name, dob, sex, home_location_id, phone, notes FROM persons",
            r => Persons[r.GetInt32(0)] = new(r.GetInt32(0), r.GetString(1), S(r, 2), r.GetString(3), DateOnly.Parse(r.GetString(4)), r.GetString(5), IN(r, 6), S(r, 7), S(r, 8)));

        Each(c, "SELECT id, plate, year, make, model, color, style, owner_person_id, notes FROM vehicles", r =>
        {
            var v = new VehicleRecord(r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), IN(r, 7), S(r, 8));
            Vehicles[v.Id] = v;
            VehiclesByPlate[v.Plate] = v;
        });

        Each(c, "SELECT id, badge, first_name, last_name, rank, unit, district FROM deputies",
            r => Deputies[r.GetInt32(0)] = new(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6)));

        Each(c, """
            SELECT id, case_number, offense_type, offense_description, district, location_id, premise_type, occurred_from, occurred_to,
                   reported_at, mo_tags, status, sensitivity, narrative, deputy_id FROM incidents
            """, r =>
        {
            var i = new IncidentRecord(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetInt32(5), r.GetString(6),
                Dt(r, 7), DtN(r, 8), Dt(r, 9), (S(r, 10) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries), r.GetString(11), r.GetString(12),
                r.GetString(13), r.GetInt32(14));
            IncidentsByCase[i.CaseNumber] = i;
            IncidentsById[i.Id] = i;
        });

        Each(c, "SELECT incident_id, person_id, role FROM incident_persons", r => IncidentPersons.Add(new(r.GetInt32(0), r.GetInt32(1), r.GetString(2))));
        Each(c, "SELECT incident_id, vehicle_id, role FROM incident_vehicles", r => IncidentVehicles.Add(new(r.GetInt32(0), r.GetInt32(1), r.GetString(2))));
        Each(c, "SELECT incident_id, description, serial, value, status FROM incident_property", r => Property.Add(new(r.GetInt32(0), r.GetString(1), S(r, 2), r.GetDouble(3), r.GetString(4))));

        Each(c, "SELECT id, fi_number, contact_at, location_id, district, person_id, vehicle_id, reason, notes FROM field_interviews",
            r => FieldInterviews[r.GetInt32(0)] = new(r.GetInt32(0), r.GetString(1), Dt(r, 2), r.GetInt32(3), r.GetString(4), r.GetInt32(5), IN(r, 6), r.GetString(7), r.GetString(8)));

        var co = new Dictionary<int, List<int>>();
        Each(c, "SELECT booking_id, person_id FROM booking_coarrestees", r =>
        {
            if (!co.TryGetValue(r.GetInt32(0), out var l)) co[r.GetInt32(0)] = l = [];
            l.Add(r.GetInt32(1));
        });
        Each(c, "SELECT id, booking_number, person_id, booked_at, released_at, charges, incident_id, housing_unit, classification, keep_separate_person_id, notes FROM bookings",
            r => Bookings[r.GetInt32(0)] = new(r.GetInt32(0), r.GetString(1), r.GetInt32(2), Dt(r, 3), DtN(r, 4), r.GetString(5), IN(r, 6), r.GetString(7), r.GetString(8),
                IN(r, 9), S(r, 10) ?? "", co.TryGetValue(r.GetInt32(0), out var l) ? [.. l] : []));

        Each(c, "SELECT id, ticket_number, transaction_at, shop_location_id, person_id, transaction_type, item_description, serial, amount, seller_phone FROM pawn_transactions",
            r => Pawns[r.GetInt32(0)] = new(r.GetInt32(0), r.GetString(1), Dt(r, 2), r.GetInt32(3), r.GetInt32(4), r.GetString(5), r.GetString(6), S(r, 7), r.GetDouble(8), S(r, 9)));

        Each(c, "SELECT id, name, location_id, district FROM lpr_cameras", r => LprCameras[r.GetInt32(0)] = new(r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetString(3)));
        Each(c, "SELECT id, read_at, plate, vehicle_id, camera_id FROM lpr_reads ORDER BY read_at", r => LprReads.Add(new(r.GetInt32(0), Dt(r, 1), r.GetString(2), IN(r, 3), r.GetInt32(4))));

        Each(c, "SELECT id, call_number, received_at, call_type, location_id, disposition, incident_id, notes FROM cad_calls WHERE incident_id IS NOT NULL",
            r => CadByIncident[r.GetInt32(6)] = new(r.GetInt32(0), r.GetString(1), Dt(r, 2), r.GetString(3), r.GetInt32(4), r.GetString(5), IN(r, 6), r.GetString(7)));

        Each(c, """
            SELECT s.policy_number, p.title, s.section_number, s.heading, s.level, s.text
            FROM policy_sections s JOIN policies p ON p.policy_number = s.policy_number ORDER BY CAST(s.policy_number AS INTEGER), s.ordinal
            """, r =>
        {
            var s = new PolicySectionRecord(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4), r.GetString(5));
            PolicySections.Add(s);
            PolicyBySection[s.SectionNumber] = s;
        });

        Each(c, "SELECT doc_type, doc_id, vector, model FROM embeddings", r =>
        {
            var bytes = (byte[])r[2];
            var v = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, v, 0, bytes.Length);
            Vectors.Add(r.GetString(0), r.GetString(1), v);
            EmbeddingModel ??= r.GetString(3);
        });
    }

    public LocationRecord LocationOf(IncidentRecord i) => Locations[i.LocationId];
}
