using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Blade.DataGen.Embedding;

/// <summary>
/// Calls the local Ollama /api/embed endpoint. nomic-embed-text expects task prefixes
/// ("search_document: " for indexed text, "search_query: " for questions), which noticeably improves retrieval.
/// </summary>
public sealed class OllamaEmbedder(string baseUrl, string model) : IDisposable
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(5) };
    public string Model => model;

    private sealed record EmbedRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input,
        [property: JsonPropertyName("keep_alive")] string KeepAlive);

    private sealed record EmbedResponse([property: JsonPropertyName("embeddings")] float[][] Embeddings);

    public async Task<float[][]> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/embed",
            new EmbedRequest(model, texts.Select(t => "search_document: " + t).ToList(), "30m"), ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<EmbedResponse>(cancellationToken: ct)
                   ?? throw new InvalidOperationException("Empty embedding response");
        return body.Embeddings;
    }

    public async Task<bool> IsAvailableAsync()
    {
        try { return (await _http.GetAsync("/api/version")).IsSuccessStatusCode; }
        catch { return false; }
    }

    public static byte[] ToBytes(float[] v)
    {
        var bytes = new byte[v.Length * sizeof(float)];
        Buffer.BlockCopy(v, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public void Dispose() => _http.Dispose();
}
