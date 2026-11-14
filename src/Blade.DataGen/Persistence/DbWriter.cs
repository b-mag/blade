using Microsoft.Data.Sqlite;

namespace Blade.DataGen.Persistence;

/// <summary>Minimal prepared-statement cache for fast bulk inserts inside one transaction.</summary>
public sealed class DbWriter(SqliteConnection conn, SqliteTransaction tx) : IDisposable
{
    private readonly Dictionary<string, SqliteCommand> _cache = [];

    public void Insert(string table, params (string Column, object? Value)[] values)
    {
        var key = table + "|" + string.Join(',', values.Select(v => v.Column));
        if (!_cache.TryGetValue(key, out var cmd))
        {
            cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"INSERT INTO {table} ({string.Join(',', values.Select(v => v.Column))}) VALUES ({string.Join(',', values.Select(v => "$" + v.Column))})";
            foreach (var v in values) cmd.Parameters.Add(new SqliteParameter("$" + v.Column, null));
            cmd.Prepare();
            _cache[key] = cmd;
        }
        for (var n = 0; n < values.Length; n++) cmd.Parameters[n].Value = values[n].Value ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }

    public static string? Ts(DateTime? t) => t?.ToString("yyyy-MM-dd HH:mm:ss");

    public void Dispose()
    {
        foreach (var c in _cache.Values) c.Dispose();
    }
}
