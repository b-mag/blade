using System.Diagnostics;
using Blade.Core;
using Blade.DataGen.Embedding;
using Blade.DataGen.Generation;
using Blade.DataGen.Model;
using Blade.DataGen.Persistence;
using Blade.DataGen.Policies;
using Microsoft.Data.Sqlite;

namespace Blade.DataGen;

public sealed record GeneratorOptions
{
    public required string OutputPath { get; init; }
    public required string PolicyDirectory { get; init; }
    public int Seed { get; init; } = 42;
    public int BaselineIncidents { get; init; } = 1450;
    public bool Embed { get; init; } = true;
    public string OllamaUrl { get; init; } = "http://localhost:11434";
    public string EmbedModel { get; init; } = "nomic-embed-text";
    public Action<string> Log { get; init; } = _ => { };
}

public sealed record GeneratorResult(Dictionary<string, long> TableCounts, int Embedded, TimeSpan Elapsed);

/// <summary>Builds the complete synthetic KCSO database. Deterministic for a given seed.</summary>
public static class DataGenerator
{
    public static async Task<GeneratorResult> RunAsync(GeneratorOptions o, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var world = new World(o.Seed, Agency.DataAsOf);
        var narratives = new NarrativeBuilder(world);
        var baseline = new BaselineGenerator(world, narratives);

        o.Log($"Generating baseline records (seed {o.Seed}, {o.BaselineIncidents} incidents)...");
        baseline.Generate(o.BaselineIncidents);
        o.Log("Planting investigative storylines...");
        new Storylines(world, narratives, baseline).PlantAll();
        AssignNumbers(world);

        var policies = PolicyParser.LoadDirectory(o.PolicyDirectory);
        o.Log($"Parsed {policies.Count} policies / {policies.Sum(p => p.Sections.Count)} sections from {o.PolicyDirectory}");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(o.OutputPath))!);
        foreach (var f in new[] { o.OutputPath, o.OutputPath + "-wal", o.OutputPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);

        await using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = o.OutputPath, Pooling = false }.ToString());
        await conn.OpenAsync(ct);
        await using (var ddl = conn.CreateCommand()) { ddl.CommandText = Schema.Ddl; await ddl.ExecuteNonQueryAsync(ct); }

        o.Log("Writing database...");
        using (var tx = conn.BeginTransaction())
        using (var db = new DbWriter(conn, tx))
        {
            WriteMeta(db, o);
            WriteWorld(db, world);
            WritePolicies(db, policies);
            foreach (var i in world.Incidents) db.Insert("incidents_fts", ("case_number", i.CaseNumber), ("narrative", i.Narrative));
            foreach (var s in policies.SelectMany(p => p.Sections))
                db.Insert("policy_fts", ("section_number", s.SectionNumber), ("heading", s.Heading), ("text", s.Text));
            tx.Commit();
        }

        var embedded = 0;
        if (o.Embed) embedded = await EmbedAsync(conn, world, policies, o, ct);
        else o.Log("Skipping embeddings (--no-embed).");

        var counts = new Dictionary<string, long>();
        foreach (var table in new[] { "locations", "persons", "vehicles", "deputies", "incidents", "incident_persons", "incident_property", "cad_calls",
                                      "field_interviews", "lpr_reads", "bookings", "pawn_transactions", "policies", "policy_sections", "embeddings", "eval_storyline_truth" })
        {
            await using var c = conn.CreateCommand();
            c.CommandText = $"SELECT COUNT(*) FROM {table}";
            counts[table] = (long)(await c.ExecuteScalarAsync(ct))!;
        }
        return new GeneratorResult(counts, embedded, sw.Elapsed);
    }

    // ------------------------------------------------------------------ numbering (after everything exists, in time order)
    private static void AssignNumbers(World w)
    {
        var rng = new Random(7); // separate RNG so numbering never perturbs content generation

        w.Incidents.Sort((a, b) => a.ReportedAt.CompareTo(b.ReportedAt));
        var perYear = new Dictionary<int, int>();
        for (var n = 0; n < w.Incidents.Count; n++)
        {
            var i = w.Incidents[n];
            var y = i.ReportedAt.Year;
            perYear[y] = (perYear.TryGetValue(y, out var c) ? c : 1000 + rng.Next(200)) + rng.Next(1, 4);
            i.Id = n + 1;
            i.CaseNumber = $"{y % 100:00}-{perYear[y]:000000}";
        }

        w.CadCalls.Sort((a, b) => a.ReceivedAt.CompareTo(b.ReceivedAt));
        var cadPerYear = new Dictionary<int, int>();
        for (var n = 0; n < w.CadCalls.Count; n++)
        {
            var c = w.CadCalls[n];
            var y = c.ReceivedAt.Year;
            cadPerYear[y] = (cadPerYear.TryGetValue(y, out var k) ? k : 0) + rng.Next(1, 6);
            c.Id = n + 1;
            c.CallNumber = $"C{y % 100:00}-{cadPerYear[y]:000000}";
        }

        w.FieldInterviews.Sort((a, b) => a.ContactAt.CompareTo(b.ContactAt));
        for (var n = 0; n < w.FieldInterviews.Count; n++)
        {
            var f = w.FieldInterviews[n];
            f.Id = n + 1;
            f.FiNumber = $"FI-{f.ContactAt.Year % 100:00}-{n + 1:00000}";
        }

        w.LprReads.Sort((a, b) => a.ReadAt.CompareTo(b.ReadAt));
        for (var n = 0; n < w.LprReads.Count; n++) w.LprReads[n].Id = n + 1;

        w.Bookings.Sort((a, b) => a.BookedAt.CompareTo(b.BookedAt));
        for (var n = 0; n < w.Bookings.Count; n++)
        {
            var b = w.Bookings[n];
            b.Id = n + 1;
            b.BookingNumber = $"B{b.BookedAt.Year % 100:00}-{4000 + n * 3 + rng.Next(3):000000}";
        }

        w.Pawns.Sort((a, b) => a.TransactionAt.CompareTo(b.TransactionAt));
        for (var n = 0; n < w.Pawns.Count; n++)
        {
            var p = w.Pawns[n];
            p.Id = n + 1;
            var prefix = new string(p.Shop.BusinessName!.Where(char.IsUpper).ToArray());
            p.TicketNumber = $"{(p.TransactionType == "SCRAP_SALE" ? "SC" : "PT")}-{prefix}-{10000 + n}";
        }
    }

    // ------------------------------------------------------------------ writers
    private static void WriteMeta(DbWriter db, GeneratorOptions o)
    {
        db.Insert("meta", ("key", "agency"), ("value", Agency.Name));
        db.Insert("meta", ("key", "data_as_of"), ("value", Agency.DataAsOf.ToString("yyyy-MM-dd")));
        db.Insert("meta", ("key", "seed"), ("value", o.Seed.ToString()));
        db.Insert("meta", ("key", "generated_utc"), ("value", DateTime.UtcNow.ToString("O")));
        db.Insert("meta", ("key", "notice"), ("value", Agency.SyntheticDataBanner + ". All people, places, incidents and policies are invented."));
    }

    private static void WriteWorld(DbWriter db, World w)
    {
        foreach (var l in w.Locations)
            db.Insert("locations", ("id", l.Id), ("address", l.Address), ("number", l.Number), ("street", l.Street), ("unit", l.Unit), ("city", l.City),
                ("district", l.District), ("premise_type", l.PremiseType), ("business_name", l.BusinessName), ("business_type", l.BusinessType), ("x", l.X), ("y", l.Y));

        foreach (var p in w.Persons)
            db.Insert("persons", ("id", p.Id), ("first_name", p.First), ("middle_name", p.Middle), ("last_name", p.Last), ("dob", p.Dob.ToString("yyyy-MM-dd")),
                ("sex", p.Sex), ("home_location_id", p.HomeLocationId), ("phone", p.Phone), ("dl_number", p.DlNumber), ("notes", p.Notes));

        foreach (var v in w.Vehicles)
            db.Insert("vehicles", ("id", v.Id), ("plate", v.Plate), ("year", v.Year), ("make", v.Make), ("model", v.Model), ("color", v.Color),
                ("style", v.Style), ("owner_person_id", v.OwnerPersonId), ("notes", v.Notes));

        foreach (var d in w.Deputies)
            db.Insert("deputies", ("id", d.Id), ("badge", d.Badge), ("first_name", d.First), ("last_name", d.Last), ("rank", d.Rank), ("unit", d.Unit), ("district", d.District));

        foreach (var i in w.Incidents)
        {
            db.Insert("incidents", ("id", i.Id), ("case_number", i.CaseNumber), ("offense_type", i.OffenseType), ("offense_code", i.OffenseCode),
                ("offense_description", i.OffenseDescription), ("district", i.District), ("location_id", i.Location.Id), ("premise_type", i.PremiseType),
                ("occurred_from", DbWriter.Ts(i.OccurredFrom)), ("occurred_to", DbWriter.Ts(i.OccurredTo)), ("occurred_date", i.OccurredFrom.ToString("yyyy-MM-dd")),
                ("occurred_hour", i.OccurredFrom.Hour), ("reported_at", DbWriter.Ts(i.ReportedAt)), ("mo_tags", string.Join(',', i.MoTags.Distinct())),
                ("status", i.Status), ("deputy_id", i.Deputy.Id), ("sensitivity", i.Sensitivity), ("narrative", i.Narrative));
            foreach (var mo in i.MoTags.Distinct()) db.Insert("incident_mo", ("incident_id", i.Id), ("mo_tag", mo));
            foreach (var (p, role) in i.Persons) db.Insert("incident_persons", ("incident_id", i.Id), ("person_id", p.Id), ("role", role));
            foreach (var (v, role) in i.Vehicles) db.Insert("incident_vehicles", ("incident_id", i.Id), ("vehicle_id", v.Id), ("role", role));
            foreach (var pr in i.Property)
                db.Insert("incident_property", ("incident_id", i.Id), ("description", pr.Description), ("serial", pr.Serial), ("value", (double)pr.Value), ("status", pr.Status));
        }

        foreach (var c in w.CadCalls)
            db.Insert("cad_calls", ("id", c.Id), ("call_number", c.CallNumber), ("received_at", DbWriter.Ts(c.ReceivedAt)), ("call_type", c.CallType),
                ("priority", c.Priority), ("location_id", c.Location.Id), ("district", c.Location.District), ("disposition", c.Disposition),
                ("incident_id", c.Incident?.Id), ("deputy_id", c.Deputy?.Id), ("caller_phone", c.CallerPhone), ("notes", c.Notes));

        foreach (var f in w.FieldInterviews)
            db.Insert("field_interviews", ("id", f.Id), ("fi_number", f.FiNumber), ("contact_at", DbWriter.Ts(f.ContactAt)), ("location_id", f.Location.Id),
                ("district", f.Location.District), ("deputy_id", f.Deputy.Id), ("person_id", f.Person.Id), ("vehicle_id", f.Vehicle?.Id), ("reason", f.Reason), ("notes", f.Notes));

        foreach (var cam in w.LprCameras)
            db.Insert("lpr_cameras", ("id", cam.Id), ("name", cam.Name), ("location_id", cam.Location.Id), ("district", cam.Location.District));
        foreach (var r in w.LprReads)
            db.Insert("lpr_reads", ("id", r.Id), ("read_at", DbWriter.Ts(r.ReadAt)), ("plate", r.Plate), ("vehicle_id", r.Vehicle?.Id), ("camera_id", r.Camera.Id));

        foreach (var b in w.Bookings)
        {
            db.Insert("bookings", ("id", b.Id), ("booking_number", b.BookingNumber), ("person_id", b.Person.Id), ("booked_at", DbWriter.Ts(b.BookedAt)),
                ("released_at", DbWriter.Ts(b.ReleasedAt)), ("charges", b.Charges), ("incident_id", b.Incident?.Id), ("housing_unit", b.HousingUnit),
                ("classification", b.Classification), ("keep_separate_person_id", b.KeepSeparateFrom?.Id), ("notes", b.Notes));
            foreach (var co in b.CoArrestees) db.Insert("booking_coarrestees", ("booking_id", b.Id), ("person_id", co.Id));
        }

        foreach (var p in w.Pawns)
            db.Insert("pawn_transactions", ("id", p.Id), ("ticket_number", p.TicketNumber), ("transaction_at", DbWriter.Ts(p.TransactionAt)),
                ("shop_location_id", p.Shop.Id), ("person_id", p.Person.Id), ("transaction_type", p.TransactionType), ("item_description", p.ItemDescription),
                ("serial", p.Serial), ("amount", (double)p.Amount), ("seller_phone", p.SellerPhone));

        foreach (var t in w.Truth)
            db.Insert("eval_storyline_truth", ("storyline", t.Storyline), ("entity_type", t.EntityType), ("entity_key", t.EntityKey()), ("note", t.Note));
    }

    private static void WritePolicies(DbWriter db, List<PolicyDoc> policies)
    {
        foreach (var p in policies)
        {
            db.Insert("policies", ("policy_number", p.Number), ("title", p.Title), ("effective", p.Effective), ("revised", p.Revised), ("file_name", p.FileName));
            foreach (var s in p.Sections)
                db.Insert("policy_sections", ("policy_number", s.PolicyNumber), ("section_number", s.SectionNumber), ("heading", s.Heading),
                    ("level", s.Level), ("parent_section", s.ParentSection), ("ordinal", s.Ordinal), ("text", s.Text));
        }
    }

    // ------------------------------------------------------------------ embeddings
    private static async Task<int> EmbedAsync(SqliteConnection conn, World w, List<PolicyDoc> policies, GeneratorOptions o, CancellationToken ct)
    {
        using var embedder = new OllamaEmbedder(o.OllamaUrl, o.EmbedModel);
        if (!await embedder.IsAvailableAsync())
        {
            o.Log($"WARNING: Ollama not reachable at {o.OllamaUrl}; embeddings skipped. Re-run when Ollama is running.");
            return 0;
        }

        var docs = new List<(string Type, string Id, string Text)>();
        foreach (var i in w.Incidents)
            docs.Add(("INCIDENT", i.CaseNumber,
                $"{i.OffenseDescription} at {i.Location.BusinessType?.ToLowerInvariant() ?? i.PremiseType.ToLowerInvariant()}. {NarrativeBuilder.ContentForEmbedding(i.Narrative)}"));
        foreach (var p in policies)
            foreach (var s in p.Sections)
                docs.Add(("POLICY", s.SectionNumber, $"Policy {p.Number} {p.Title} — {s.SectionNumber} {s.Heading}. {s.Text}"));

        o.Log($"Embedding {docs.Count} documents with {o.EmbedModel} (local Ollama)...");
        var sw = Stopwatch.StartNew();
        const int batch = 32;
        for (var start = 0; start < docs.Count; start += batch)
        {
            var slice = docs.Skip(start).Take(batch).ToList();
            var vectors = await embedder.EmbedDocumentsAsync(slice.Select(d => d.Text).ToList(), ct);
            using var tx = conn.BeginTransaction();
            using var db = new DbWriter(conn, tx);
            for (var n = 0; n < slice.Count; n++)
                db.Insert("embeddings", ("doc_type", slice[n].Type), ("doc_id", slice[n].Id), ("model", o.EmbedModel), ("dim", vectors[n].Length), ("vector", OllamaEmbedder.ToBytes(vectors[n])));
            tx.Commit();
            if ((start / batch) % 10 == 0) o.Log($"  {Math.Min(start + batch, docs.Count)}/{docs.Count} ({sw.Elapsed.TotalSeconds:F0}s)");
        }
        o.Log($"Embedded {docs.Count} documents in {sw.Elapsed.TotalSeconds:F1}s");
        return docs.Count;
    }
}
