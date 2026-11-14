namespace Blade.Server.Data;

/// <summary>
/// Brute-force cosine similarity over pre-normalized vectors. A few thousand 768-d vectors scan in ~1 ms.
/// Production swap: SQL Server 2025 VECTOR type, pgvector, or Qdrant — same interface.
/// </summary>
public sealed class VectorIndex
{
    private readonly Dictionary<string, Dictionary<string, float[]>> _byType = [];

    public int Count => _byType.Values.Sum(d => d.Count);
    public int Dimensions { get; private set; }

    public void Add(string docType, string docId, float[] vector)
    {
        Normalize(vector);
        Dimensions = vector.Length;
        if (!_byType.TryGetValue(docType, out var d)) _byType[docType] = d = [];
        d[docId] = vector;
    }

    public float[]? Get(string docType, string docId) =>
        _byType.TryGetValue(docType, out var d) && d.TryGetValue(docId, out var v) ? v : null;

    public List<(string Id, double Score)> Search(string docType, float[] query, int k, Func<string, bool>? allow = null)
    {
        if (!_byType.TryGetValue(docType, out var d)) return [];
        var q = (float[])query.Clone();
        Normalize(q);
        return d.Where(kv => allow is null || allow(kv.Key))
                .Select(kv => (kv.Key, Score: Dot(q, kv.Value)))
                .OrderByDescending(x => x.Score)
                .Take(k)
                .ToList();
    }

    public static double Dot(float[] a, float[] b)
    {
        double s = 0;
        for (var i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }

    public static void Normalize(float[] v)
    {
        double n = 0;
        foreach (var x in v) n += x * x;
        n = Math.Sqrt(n);
        if (n == 0) return;
        for (var i = 0; i < v.Length; i++) v[i] = (float)(v[i] / n);
    }

    public static float[] Centroid(IEnumerable<float[]> vectors)
    {
        float[]? c = null;
        foreach (var v in vectors)
        {
            c ??= new float[v.Length];
            for (var i = 0; i < v.Length; i++) c[i] += v[i];
        }
        c ??= [];
        Normalize(c);
        return c;
    }
}
