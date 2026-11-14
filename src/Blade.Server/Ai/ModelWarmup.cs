namespace Blade.Server.Ai;

/// <summary>Loads the local models into GPU memory at startup so the first demo question is not slow (~20–75 s cold).</summary>
public sealed class ModelWarmup(ILlmClient llm, QueryEmbedder embedder, ILogger<ModelWarmup> log) : BackgroundService
{
    public bool IsWarm { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await WarmAsync(stoppingToken); }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            log.LogWarning("Model warm-up failed: {Message}. Is Ollama running?", ex.Message);
        }
    }

    public async Task WarmAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        log.LogInformation("Warming models ({Model} via {Provider})...", llm.Model, llm.Provider);
        await embedder.EmbedQueryAsync("warm up", ct);
        await llm.CompleteAsync(new LlmRequest("Reply with OK.", "OK?", MaxTokens: 3), ct);
        IsWarm = true;
        log.LogInformation("Models warm in {Seconds:F1}s", sw.Elapsed.TotalSeconds);
    }
}
