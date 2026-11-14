using Blade.DataGen;
using Microsoft.Data.Sqlite;

namespace Blade.Tests;

/// <summary>Generates one database (no embeddings) shared by all data tests.</summary>
public sealed class DataGenFixture : IAsyncLifetime
{
    public string RepoRoot { get; } = FindRepoRoot();
    public string DbPath { get; } = Path.Combine(Path.GetTempPath(), $"blade-test-{Guid.NewGuid():N}.db");
    public GeneratorResult Result { get; private set; } = null!;

    public GeneratorOptions Options(string path) => new()
    {
        OutputPath = path,
        PolicyDirectory = Path.Combine(RepoRoot, "data", "policies"),
        Embed = false,
    };

    public async Task InitializeAsync() => Result = await DataGenerator.RunAsync(Options(DbPath));

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(DbPath); } catch { /* best effort */ }
        return Task.CompletedTask;
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection($"Data Source={DbPath};Mode=ReadOnly;Pooling=False");
        c.Open();
        return c;
    }

    public static List<object?[]> Rows(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var rows = new List<object?[]>();
        while (r.Read())
        {
            var row = new object?[r.FieldCount];
            for (var i = 0; i < r.FieldCount; i++) row[i] = r.IsDBNull(i) ? null : r.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    public static T Scalar<T>(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return (T)Convert.ChangeType(cmd.ExecuteScalar()!, typeof(T));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Blade.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Blade.slnx not found");
    }
}
