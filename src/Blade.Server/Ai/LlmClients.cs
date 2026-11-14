using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Blade.Server.Ai;

public sealed record LlmRequest(string System, string User, JsonObject? JsonSchema = null, double Temperature = 0.1, int MaxTokens = 900);

public sealed record LlmResult(string Text, string Provider, string Model, bool IsLocal, int PromptTokens, int CompletionTokens, long Ms);

/// <summary>
/// Provider-neutral chat abstraction. The server never exposes model endpoints or keys to workstations;
/// swapping local ↔ cloud (or a different local runtime such as vLLM) is a configuration change.
/// </summary>
public interface ILlmClient
{
    string Provider { get; }
    string Model { get; }
    bool IsLocal { get; }
    Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct = default);

    /// <summary>
    /// Like <see cref="CompleteAsync"/> but reports text as it is generated. Providers without streaming
    /// deliver the whole answer as a single chunk.
    /// </summary>
    async Task<LlmResult> StreamAsync(LlmRequest request, Func<string, Task> onDelta, CancellationToken ct = default)
    {
        var r = await CompleteAsync(request, ct);
        await onDelta(r.Text);
        return r;
    }
}

public static partial class LlmText
{
    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline)] private static partial Regex ThinkRx();
    public static string Clean(string s) => ThinkRx().Replace(s, "").Trim();
}

/// <summary>Local inference through Ollama's native /api/chat (no data leaves the machine).</summary>
public sealed class OllamaLlmClient(HttpClient http, AiOptions o) : ILlmClient
{
    public string Provider => "Ollama (local)";
    public string Model => o.ChatModel;
    public bool IsLocal => true;

    public async Task<LlmResult> CompleteAsync(LlmRequest req, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        using var resp = await http.PostAsync($"{o.OllamaUrl.TrimEnd('/')}/api/chat",
            new StringContent(Body(req, stream: false).ToJsonString(), Encoding.UTF8, "application/json"), ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"Ollama error {(int)resp.StatusCode}: {text}");
        var doc = JsonNode.Parse(text)!;
        return new LlmResult(
            LlmText.Clean(doc["message"]?["content"]?.GetValue<string>() ?? ""),
            Provider, o.ChatModel, true,
            doc["prompt_eval_count"]?.GetValue<int>() ?? 0,
            doc["eval_count"]?.GetValue<int>() ?? 0,
            sw.ElapsedMilliseconds);
    }

    /// <summary>Ollama streams one JSON object per line; the last one (done=true) carries the token counts.</summary>
    public async Task<LlmResult> StreamAsync(LlmRequest req, Func<string, Task> onDelta, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        using var msg = new HttpRequestMessage(HttpMethod.Post, $"{o.OllamaUrl.TrimEnd('/')}/api/chat")
        {
            Content = new StringContent(Body(req, stream: true).ToJsonString(), Encoding.UTF8, "application/json"),
        };
        using var resp = await http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"Ollama error {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync(ct)}");

        var full = new StringBuilder();
        int promptTokens = 0, completionTokens = 0;
        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(ct));
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0) continue;
            var doc = JsonNode.Parse(line)!;
            if (doc["error"]?.GetValue<string>() is { } err) throw new InvalidOperationException($"Ollama error: {err}");
            var piece = doc["message"]?["content"]?.GetValue<string>() ?? "";
            if (piece.Length > 0)
            {
                full.Append(piece);
                await onDelta(piece);
            }
            if (doc["done"]?.GetValue<bool>() == true)
            {
                promptTokens = doc["prompt_eval_count"]?.GetValue<int>() ?? 0;
                completionTokens = doc["eval_count"]?.GetValue<int>() ?? 0;
                break;
            }
        }
        return new LlmResult(LlmText.Clean(full.ToString()), Provider, o.ChatModel, true, promptTokens, completionTokens, sw.ElapsedMilliseconds);
    }

    private JsonObject Body(LlmRequest req, bool stream)
    {
        var body = new JsonObject
        {
            ["model"] = o.ChatModel,
            ["stream"] = stream,
            ["think"] = false,
            ["keep_alive"] = o.KeepAlive,
            ["options"] = new JsonObject
            {
                ["temperature"] = req.Temperature,
                ["num_predict"] = req.MaxTokens,
                ["num_ctx"] = o.ContextTokens,
            },
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = req.System },
                new JsonObject { ["role"] = "user", ["content"] = req.User },
            },
        };
        if (req.JsonSchema is not null) body["format"] = req.JsonSchema.DeepClone();
        return body;
    }

    /// <summary>Loads the chat and embedding models into GPU memory so the first demo question is fast.</summary>
    public async Task WarmUpAsync(CancellationToken ct = default)
    {
        await CompleteAsync(new LlmRequest("Reply with OK.", "OK?", MaxTokens: 3), ct);
        var embed = new JsonObject { ["model"] = o.EmbeddingModel, ["input"] = "warm up", ["keep_alive"] = o.KeepAlive };
        using var _ = await http.PostAsync($"{o.OllamaUrl.TrimEnd('/')}/api/embed", new StringContent(embed.ToJsonString(), Encoding.UTF8, "application/json"), ct);
    }
}

/// <summary>
/// OpenAI-compatible chat completions (e.g., a free hosted endpoint). POC fallback ONLY — synthetic data only,
/// and the desktop shows an amber "CLOUD (POC ONLY)" badge whenever this provider is active.
/// </summary>
public sealed class OpenAiCompatibleLlmClient(HttpClient http, AiOptions o) : ILlmClient
{
    public string Provider => "OpenAI-compatible (cloud — POC only)";
    public string Model => o.CloudModel;
    public bool IsLocal => false;

    public async Task<LlmResult> CompleteAsync(LlmRequest req, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var key = Environment.GetEnvironmentVariable(o.CloudApiKeyEnvVar)
                  ?? throw new InvalidOperationException($"Cloud provider selected but environment variable {o.CloudApiKeyEnvVar} is not set.");
        var system = req.JsonSchema is null ? req.System
            : req.System + "\n\nRespond with a single JSON object that conforms to this JSON schema:\n" + req.JsonSchema.ToJsonString();
        var body = new JsonObject
        {
            ["model"] = o.CloudModel,
            ["temperature"] = req.Temperature,
            ["max_tokens"] = req.MaxTokens,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = req.User },
            },
        };
        if (req.JsonSchema is not null) body["response_format"] = new JsonObject { ["type"] = "json_object" };

        using var msg = new HttpRequestMessage(HttpMethod.Post, $"{o.CloudBaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var resp = await http.SendAsync(msg, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"Cloud provider error {(int)resp.StatusCode}: {text}");
        var doc = JsonNode.Parse(text)!;
        return new LlmResult(
            LlmText.Clean(doc["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? ""),
            Provider, o.CloudModel, false,
            doc["usage"]?["prompt_tokens"]?.GetValue<int>() ?? 0,
            doc["usage"]?["completion_tokens"]?.GetValue<int>() ?? 0,
            sw.ElapsedMilliseconds);
    }
}

/// <summary>
/// Query embeddings always come from the local embedding model (it must match the model that built the index).
/// If it is unavailable, retrieval degrades to keyword + structured search and the response says so.
/// </summary>
public sealed class QueryEmbedder(HttpClient http, AiOptions o)
{
    public string Model => o.EmbeddingModel;

    public async Task<float[]?> EmbedQueryAsync(string text, CancellationToken ct = default)
    {
        try
        {
            var body = new JsonObject { ["model"] = o.EmbeddingModel, ["input"] = "search_query: " + text, ["keep_alive"] = o.KeepAlive };
            // On an 8 GB laptop GPU the chat model and embedding model evict each other (3-6 s reloads). A one-line query
            // embeds in ~50 ms on CPU, so keep the embedding model off the GPU. A server-class GPU would not need this.
            if (o.EmbeddingOnCpu) body["options"] = new JsonObject { ["num_gpu"] = 0 };
            using var resp = await http.PostAsync($"{o.OllamaUrl.TrimEnd('/')}/api/embed", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), ct);
            if (!resp.IsSuccessStatusCode) return null;
            var doc = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct))!;
            return doc["embeddings"]![0]!.AsArray().Select(n => n!.GetValue<float>()).ToArray();
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
