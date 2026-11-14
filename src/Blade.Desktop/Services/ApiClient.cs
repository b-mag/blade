using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blade.Core;

namespace Blade.Desktop.Services;

public sealed class ApiException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
    public bool Denied => Status == HttpStatusCode.Forbidden;
}

/// <summary>
/// Thin client for the BLADE server. The desktop never talks to a model — every AI call goes through the server,
/// which enforces role filtering and writes the audit trail. Identity headers are a POC stand-in for Windows/Entra auth.
/// </summary>
public sealed class ApiClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly HttpClient _http;

    public ApiClient(string baseUrl)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromMinutes(5) };
    }

    public string BaseUrl { get; }
    public string User { get; set; } = "j.alvarez";
    public string Role { get; set; } = Roles.Analyst;
    public string Workstation { get; } = Environment.MachineName;

    private HttpRequestMessage Req(HttpMethod m, string path, object? body = null)
    {
        var r = new HttpRequestMessage(m, path);
        r.Headers.Add(BladeHeaders.User, User);
        r.Headers.Add(BladeHeaders.Role, Role);
        r.Headers.Add(BladeHeaders.Workstation, Workstation);
        if (body is not null) r.Content = JsonContent.Create(body, options: Json);
        return r;
    }

    private async Task<T> Send<T>(HttpRequestMessage req, CancellationToken ct = default)
    {
        using var resp = await _http.SendAsync(req, ct);
        await EnsureOk(resp, ct);
        return (await resp.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    private static async Task EnsureOk(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        var text = await resp.Content.ReadAsStringAsync(ct);
        string message;
        try
        {
            var doc = JsonDocument.Parse(text).RootElement;
            message = doc.TryGetProperty("error", out var e) ? e.GetString()!
                    : doc.TryGetProperty("detail", out var d) ? d.GetString()!
                    : text;
        }
        catch (JsonException) { message = text; }
        throw new ApiException(resp.StatusCode, string.IsNullOrWhiteSpace(message) ? resp.ReasonPhrase ?? "Request failed" : message);
    }

    /// <summary>
    /// Reads a newline-delimited JSON stream of <see cref="StreamEvent{T}"/>. <paramref name="onEvent"/> runs on the
    /// caller's context (the UI thread) for every event; the method returns the final response from the done event.
    /// </summary>
    private async Task<T> Stream<T>(HttpRequestMessage req, Action<StreamEvent<T>> onEvent, CancellationToken ct) where T : class
    {
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureOk(resp, ct);
        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(ct));
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0) continue;
            var e = JsonSerializer.Deserialize<StreamEvent<T>>(line, Json)!;
            if (e.Type == StreamEventTypes.Error) throw new ApiException(HttpStatusCode.InternalServerError, e.Error ?? "AI service error");
            onEvent(e);
            if (e.Type == StreamEventTypes.Done && e.Response is not null) return e.Response;
        }
        throw new ApiException(HttpStatusCode.InternalServerError, "The server ended the answer stream early.");
    }

    public Task<SystemInfo> SystemAsync(CancellationToken ct = default) => Send<SystemInfo>(Req(HttpMethod.Get, "/api/system"), ct);
    public Task<AskResponse> AskAsync(AskRequest r, CancellationToken ct = default) => Send<AskResponse>(Req(HttpMethod.Post, "/api/ask", r), ct);
    public Task<AskResponse> AskStreamAsync(AskRequest r, Action<StreamEvent<AskResponse>> onEvent, CancellationToken ct = default) =>
        Stream(Req(HttpMethod.Post, "/api/ask/stream", r), onEvent, ct);
    public Task<PolicyResponse> PolicyStreamAsync(PolicyRequest r, Action<StreamEvent<PolicyResponse>> onEvent, CancellationToken ct = default) =>
        Stream(Req(HttpMethod.Post, "/api/policy/stream", r), onEvent, ct);
    public Task<PolicyResponse> PolicyAsync(PolicyRequest r, CancellationToken ct = default) => Send<PolicyResponse>(Req(HttpMethod.Post, "/api/policy", r), ct);
    public Task<IncidentDetail> IncidentAsync(string caseNumber) => Send<IncidentDetail>(Req(HttpMethod.Get, $"/api/incidents/{Uri.EscapeDataString(caseNumber)}"));
    public Task<PolicySectionHit> PolicySectionAsync(string section) => Send<PolicySectionHit>(Req(HttpMethod.Get, $"/api/policy/sections/{Uri.EscapeDataString(section)}"));
    public Task<GraphView> GraphAsync(string q, int depth = 2) => Send<GraphView>(Req(HttpMethod.Get, $"/api/graph?q={Uri.EscapeDataString(q)}&depth={depth}"));
    public Task<List<AuditEntry>> AuditAsync(string? user, string? action, int take = 300) =>
        Send<List<AuditEntry>>(Req(HttpMethod.Get, $"/api/audit?take={take}{(string.IsNullOrWhiteSpace(user) ? "" : "&user=" + Uri.EscapeDataString(user))}{(string.IsNullOrWhiteSpace(action) ? "" : "&action=" + Uri.EscapeDataString(action))}"));
    public Task<List<UsageRow>> UsageAsync() => Send<List<UsageRow>>(Req(HttpMethod.Get, "/api/audit/usage"));
    public Task<AuditVerifyResult> VerifyAsync() => Send<AuditVerifyResult>(Req(HttpMethod.Get, "/api/audit/verify"));
}
