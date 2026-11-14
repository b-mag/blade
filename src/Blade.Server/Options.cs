namespace Blade.Server;

public sealed class BladeOptions
{
    public DataOptions Data { get; set; } = new();
    public AiOptions AI { get; set; } = new();
}

public sealed class DataOptions
{
    /// <summary>Path to the generated read-only database. Empty = &lt;repo&gt;/data/blade.db.</summary>
    public string Database { get; set; } = "";
    /// <summary>Path to the writable audit database. Empty = &lt;repo&gt;/data/audit.db.</summary>
    public string AuditDatabase { get; set; } = "";
}

public sealed class AiOptions
{
    /// <summary>"Ollama" (local, default) or "OpenAICompatible" (cloud fallback — POC only, disclosed in the UI).</summary>
    public string Provider { get; set; } = "Ollama";
    public string OllamaUrl { get; set; } = "http://localhost:11434";
    public string ChatModel { get; set; } = "qwen3:8b";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    public bool EmbeddingOnCpu { get; set; } = true;
    public int ContextTokens { get; set; } = 8192;
    public string KeepAlive { get; set; } = "60m";

    public string CloudBaseUrl { get; set; } = "";
    public string CloudModel { get; set; } = "";
    /// <summary>Name of the environment variable holding the cloud API key (never stored in config files).</summary>
    public string CloudApiKeyEnvVar { get; set; } = "BLADE_CLOUD_API_KEY";
}
