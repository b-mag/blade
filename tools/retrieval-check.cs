// Retrieval quality check against data/blade.db. Run from the repo root: dotnet run tools/retrieval-check.cs
// Requires Ollama running with nomic-embed-text.
#:package Microsoft.Data.Sqlite@11.0.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;

using var c = new SqliteConnection($"Data Source={Path.GetFullPath(args.FirstOrDefault() ?? "data/blade.db")};Mode=ReadOnly");
c.Open();

Console.WriteLine("=== Fixed narratives (commercial burglary, residential theft, arrest) ===");
foreach (var sql in new[] {
    "SELECT case_number||': '||narrative FROM incidents WHERE offense_type='BURGLARY' AND premise_type='COMMERCIAL' AND case_number NOT IN (SELECT entity_key FROM eval_storyline_truth) LIMIT 1",
    "SELECT case_number||': '||narrative FROM incidents WHERE offense_type='THEFT' AND premise_type='RESIDENTIAL' LIMIT 1",
    "SELECT case_number||': '||narrative FROM incidents WHERE offense_type='THEFT' AND status='CLEARED_ARREST' LIMIT 1" })
{
    using var cmd = c.CreateCommand(); cmd.CommandText = sql;
    Console.WriteLine(cmd.ExecuteScalar()); Console.WriteLine();
}

// Load vectors
var docs = new List<(string Type, string Id, float[] V)>();
using (var cmd = c.CreateCommand())
{
    cmd.CommandText = "SELECT doc_type, doc_id, vector FROM embeddings";
    using var r = cmd.ExecuteReader();
    while (r.Read())
    {
        var bytes = (byte[])r[2]; var v = new float[bytes.Length / 4]; Buffer.BlockCopy(bytes, 0, v, 0, bytes.Length);
        docs.Add((r.GetString(0), r.GetString(1), v));
    }
}
var truth = new HashSet<string>();
using (var cmd = c.CreateCommand()) { cmd.CommandText = "SELECT entity_key FROM eval_storyline_truth"; using var r = cmd.ExecuteReader(); while (r.Read()) truth.Add(r.GetString(0)); }

using var http = new HttpClient { BaseAddress = new Uri("http://localhost:11434") };
async Task<float[]> Embed(string q)
{
    var resp = await http.PostAsJsonAsync("/api/embed", new { model = "nomic-embed-text", input = new[] { "search_query: " + q } });
    var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
    return doc.GetProperty("embeddings")[0].EnumerateArray().Select(e => e.GetSingle()).ToArray();
}
static double Cos(float[] a, float[] b) { double d = 0, na = 0, nb = 0; for (var i = 0; i < a.Length; i++) { d += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; } return d / Math.Sqrt(na * nb); }

foreach (var (q, type) in new[] {
    ("white work van with ladder rack seen behind tool store, rear door pried at night", "INCIDENT"),
    ("catalytic converter cut from parked Prius overnight", "INCIDENT"),
    ("use of force with a knife threat when the subject is non-compliant", "POLICY"),
    ("can I use AI to write my report narrative", "POLICY") })
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var qv = await Embed(q);
    var top = docs.Where(d => d.Type == type).Select(d => (d.Id, S: Cos(qv, d.V))).OrderByDescending(x => x.S).Take(8).ToList();
    Console.WriteLine($"=== [{type}] \"{q}\"  ({sw.ElapsedMilliseconds} ms incl. query embed + scan)");
    foreach (var (id, s) in top) Console.WriteLine($"  {s:F3}  {id}{(truth.Contains(id) ? "  <-- planted" : "")}");
}

// ---- Hybrid check: structured filter first, then vector re-rank inside the filtered set
var filtered = new List<string>();
using (var cmd = c.CreateCommand())
{
    cmd.CommandText = @"SELECT i.case_number FROM incidents i WHERE i.offense_type='BURGLARY' AND i.district='NORTH' AND i.premise_type='COMMERCIAL'
      AND i.occurred_date >= '2025-05-14' AND (i.occurred_hour >= 22 OR i.occurred_hour < 6)
      AND EXISTS (SELECT 1 FROM incident_mo m WHERE m.incident_id=i.id AND m.mo_tag='FORCED_ENTRY')";
    using var r = cmd.ExecuteReader(); while (r.Read()) filtered.Add(r.GetString(0));
}
var series = new HashSet<string>();
using (var cmd = c.CreateCommand()) { cmd.CommandText = "SELECT entity_key FROM eval_storyline_truth WHERE note LIKE 'Series burglary%'"; using var r = cmd.ExecuteReader(); while (r.Read()) series.Add(r.GetString(0)); }
var semantic = await Embed("forced entry commercial burglary after 10pm, similar method");
var rr = docs.Where(d => d.Type == "INCIDENT" && filtered.Contains(d.Id)).Select(d => (d.Id, S: Cos(semantic, d.V))).OrderByDescending(x => x.S).ToList();
Console.WriteLine($"\n=== Filter ({filtered.Count} rows) + re-rank with the model's semantic_query; series in top 7: {rr.Take(7).Count(x => series.Contains(x.Id))}/7, top 10: {rr.Take(10).Count(x => series.Contains(x.Id))}");
foreach (var (id, s) in rr.Take(10)) Console.WriteLine($"  {s:F3}  {id}{(series.Contains(id) ? "  <-- series" : "")}");

// ---- Keyword (FTS5/BM25) check
using (var cmd = c.CreateCommand())
{
    cmd.CommandText = "SELECT case_number, bm25(incidents_fts) FROM incidents_fts WHERE incidents_fts MATCH 'van AND (ladder OR pried OR pry)' ORDER BY 2 LIMIT 10";
    using var r = cmd.ExecuteReader();
    Console.WriteLine("\n=== FTS5 'van AND (ladder OR pried OR pry)'");
    while (r.Read()) Console.WriteLine($"  {r.GetDouble(1):F2}  {r.GetString(0)}{(series.Contains(r.GetString(0)) ? "  <-- series" : truth.Contains(r.GetString(0)) ? "  <-- planted" : "")}");
}

// ---- Series detection: mutual similarity among the filtered results
var fv = docs.Where(d => d.Type == "INCIDENT" && filtered.Contains(d.Id)).ToList();
var cohesion = fv.Select(a => (a.Id, S: fv.Where(b => b.Id != a.Id).Select(b => Cos(a.V, b.V)).OrderByDescending(x => x).Take(4).Average()))
                 .OrderByDescending(x => x.S).ToList();
Console.WriteLine($"\n=== Mutual-similarity (avg cos to 4 nearest neighbours inside result set); series in top 7: {cohesion.Take(7).Count(x => series.Contains(x.Id))}/7");
foreach (var (id, s) in cohesion) Console.WriteLine($"  {s:F3}  {id}{(series.Contains(id) ? "  <-- series" : "")}");
