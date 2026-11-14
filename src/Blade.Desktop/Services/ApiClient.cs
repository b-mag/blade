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
        if (!resp.IsSuccessStatusCode)
        {
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
        return (await resp.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    public Task<SystemInfo> SystemAsync(CancellationToken ct = default) => Send<SystemInfo>(Req(HttpMethod.Get, "/api/system"), ct);
    public Task<AskResponse> AskAsync(AskRequest r, CancellationToken ct = default) => Send<AskResponse>(Req(HttpMethod.Post, "/api/ask", r), ct);
    public Task<PolicyResponse> PolicyAsync(PolicyRequest r, CancellationToken ct = default) => Send<PolicyResponse>(Req(HttpMethod.Post, "/api/policy", r), ct);
    public Task<IncidentDetail> IncidentAsync(string caseNumber) => Send<IncidentDetail>(Req(HttpMethod.Get, $"/api/incidents/{Uri.EscapeDataString(caseNumber)}"));
    public Task<PolicySectionHit> PolicySectionAsync(string section) => Send<PolicySectionHit>(Req(HttpMethod.Get, $"/api/policy/sections/{Uri.EscapeDataString(section)}"));
    public Task<GraphView> GraphAsync(string q, int depth = 2) => Send<GraphView>(Req(HttpMethod.Get, $"/api/graph?q={Uri.EscapeDataString(q)}&depth={depth}"));
    public Task<List<AuditEntry>> AuditAsync(string? user, string? action, int take = 300) =>
        Send<List<AuditEntry>>(Req(HttpMethod.Get, $"/api/audit?take={take}{(string.IsNullOrWhiteSpace(user) ? "" : "&user=" + Uri.EscapeDataString(user))}{(string.IsNullOrWhiteSpace(action) ? "" : "&action=" + Uri.EscapeDataString(action))}"));
    public Task<List<UsageRow>> UsageAsync() => Send<List<UsageRow>>(Req(HttpMethod.Get, "/api/audit/usage"));
    public Task<AuditVerifyResult> VerifyAsync() => Send<AuditVerifyResult>(Req(HttpMethod.Get, "/api/audit/verify"));
}
