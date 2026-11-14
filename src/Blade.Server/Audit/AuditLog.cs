using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Blade.Core;
using Microsoft.Data.Sqlite;

namespace Blade.Server.Audit;

public sealed record AuditWrite(
    UserContext User, string Action, string? Question = null, string? Detail = null, IEnumerable<string>? RecordsReturned = null,
    IEnumerable<string>? RecordsCited = null, string? Provider = null, string? Model = null, long LatencyMs = 0,
    int PromptTokens = 0, int CompletionTokens = 0, string Outcome = "OK");

/// <summary>
/// Append-only, hash-chained audit trail of every AI query, generation, record view and access denial.
/// Each entry's SHA-256 covers its own content plus the previous entry's hash, so any edit or deletion breaks the
/// chain and is detected by <see cref="Verify"/>. Production: write-once storage + SIEM forwarding, retained per the
/// records schedule; this file stands in for that.
/// </summary>
public sealed class AuditLog
{
    private readonly string _cs;
    private readonly Lock _gate = new();

    public AuditLog(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS audit_events (
                id INTEGER PRIMARY KEY, ts_utc TEXT NOT NULL, user_name TEXT NOT NULL, role TEXT NOT NULL, workstation TEXT NOT NULL,
                action TEXT NOT NULL, question TEXT, detail TEXT, records_returned TEXT, records_cited TEXT, provider TEXT, model TEXT,
                latency_ms INTEGER NOT NULL, prompt_tokens INTEGER NOT NULL, completion_tokens INTEGER NOT NULL, outcome TEXT NOT NULL,
                prev_hash TEXT NOT NULL, hash TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_audit_user ON audit_events (user_name, ts_utc);
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_cs);
        c.Open();
        return c;
    }

    private static string Join(IEnumerable<string>? ids) => ids is null ? "" : string.Join(",", ids.Distinct());

    public long Write(AuditWrite w)
    {
        lock (_gate)
        {
            using var c = Open();
            using var tx = c.BeginTransaction();
            string prev;
            using (var p = c.CreateCommand())
            {
                p.Transaction = tx;
                p.CommandText = "SELECT hash FROM audit_events ORDER BY id DESC LIMIT 1";
                prev = p.ExecuteScalar() as string ?? "GENESIS";
            }
            var ts = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            var question = w.Question is null ? null : PiiFilter(w.Question);
            var fields = new object?[]
            {
                ts, w.User.Name, w.User.Role, w.User.Workstation, w.Action, question, w.Detail, Join(w.RecordsReturned), Join(w.RecordsCited),
                w.Provider, w.Model, w.LatencyMs, w.PromptTokens, w.CompletionTokens, w.Outcome,
            };
            var hash = Hash(prev, fields);
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO audit_events (ts_utc, user_name, role, workstation, action, question, detail, records_returned, records_cited,
                    provider, model, latency_ms, prompt_tokens, completion_tokens, outcome, prev_hash, hash)
                VALUES ($p0, $p1, $p2, $p3, $p4, $p5, $p6, $p7, $p8, $p9, $p10, $p11, $p12, $p13, $p14, $prev, $hash);
                SELECT last_insert_rowid();
                """;
            for (var i = 0; i < fields.Length; i++) cmd.Parameters.AddWithValue($"$p{i}", fields[i] ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$prev", prev);
            cmd.Parameters.AddWithValue("$hash", hash);
            var id = (long)cmd.ExecuteScalar()!;
            tx.Commit();
            return id;
        }
    }

    private static string PiiFilter(string s) => Answer.PiiRedactor.Redact(s).Text;

    private static string Hash(string prev, object?[] fields)
    {
        var canonical = prev + "|" + string.Join("|", fields.Select(f => Convert.ToString(f, CultureInfo.InvariantCulture) ?? "∅"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public List<AuditEntry> Query(string? user = null, string? action = null, int take = 200)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(user)) { where.Add("user_name = $u"); cmd.Parameters.AddWithValue("$u", user); }
        if (!string.IsNullOrWhiteSpace(action)) { where.Add("action = $a"); cmd.Parameters.AddWithValue("$a", action); }
        cmd.CommandText = $"SELECT * FROM audit_events{(where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "")} ORDER BY id DESC LIMIT $take";
        cmd.Parameters.AddWithValue("$take", Math.Clamp(take, 1, 2000));
        var list = new List<AuditEntry>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new AuditEntry(r.GetInt64(0), DateTime.Parse(r.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8),
                r.IsDBNull(9) ? null : r.GetString(9), r.IsDBNull(10) ? null : r.GetString(10), r.IsDBNull(11) ? null : r.GetString(11),
                r.GetInt64(12), r.GetInt32(13), r.GetInt32(14), r.GetString(15), r.GetString(17)));
        return list;
    }

    public List<UsageRow> Usage()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT user_name, MAX(role),
                   SUM(action IN ('ASK', 'POLICY_QUERY', 'GRAPH_QUERY')), SUM(action = 'RECORD_VIEW'), SUM(outcome = 'DENIED'),
                   SUM(prompt_tokens + completion_tokens), MAX(ts_utc)
            FROM audit_events GROUP BY user_name ORDER BY MAX(ts_utc) DESC
            """;
        var list = new List<UsageRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new UsageRow(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetInt64(5),
                DateTime.Parse(r.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        return list;
    }

    public AuditVerifyResult Verify()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT id, ts_utc, user_name, role, workstation, action, question, detail, records_returned, records_cited, provider, model,
                   latency_ms, prompt_tokens, completion_tokens, outcome, prev_hash, hash FROM audit_events ORDER BY id
            """;
        using var r = cmd.ExecuteReader();
        var prev = "GENESIS";
        long n = 0;
        while (r.Read())
        {
            n++;
            var fields = new object?[15];
            for (var i = 0; i < 15; i++) fields[i] = r.IsDBNull(i + 1) ? null : r.GetValue(i + 1);
            var expected = Hash(prev, fields);
            if (r.GetString(16) != prev || r.GetString(17) != expected)
                return new AuditVerifyResult(false, n, r.GetInt64(0), $"Chain broken at entry {r.GetInt64(0)}: the record was altered or a prior record was removed.");
            prev = expected;
        }
        return new AuditVerifyResult(true, n, null, $"All {n} audit entries verified. Hash chain intact.");
    }
}

public sealed record UserContext(string Name, string Role, string Workstation)
{
    /// <summary>
    /// POC identity comes from request headers set by the desktop's role switcher. Production: Windows/Entra ID
    /// authentication with MFA, roles from AD groups, and no client-asserted identity.
    /// </summary>
    public static UserContext From(HttpContext http)
    {
        var h = http.Request.Headers;
        var role = h[BladeHeaders.Role].ToString().ToUpperInvariant();
        return new UserContext(
            string.IsNullOrWhiteSpace(h[BladeHeaders.User]) ? "demo.user" : h[BladeHeaders.User].ToString(),
            Roles.All.Contains(role) ? role : Roles.Deputy,
            string.IsNullOrWhiteSpace(h[BladeHeaders.Workstation]) ? (http.Connection.RemoteIpAddress?.ToString() ?? "unknown") : h[BladeHeaders.Workstation].ToString());
    }
}
