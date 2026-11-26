using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PromptCrew.Core;

namespace PromptCrew.Client;

public sealed class CrewApiException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
public sealed record SessionDto(bool IsAdmin, string Name, Guid[] ProjectIds);
public sealed class CrewClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly Func<string>? _readKey;
    public CrewClient(string baseUrl, Func<string> readKey, TimeSpan? timeout = null)
    {
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = Address(baseUrl), Timeout = timeout ?? TimeSpan.FromSeconds(20) };
        _readKey = readKey;
    }
    public CrewClient(HttpClient http) => _http = http;
    public static Uri Address(string value)
    {
        if (!Uri.TryCreate(value.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var address) ||
            address.Scheme is not ("http" or "https") || address.UserInfo.Length != 0 || address.Query.Length != 0 || address.Fragment.Length != 0)
            throw new ArgumentException("请输入不含凭据、查询参数的 http(s) API 根地址。");
        if (address.Scheme == "http" && !address.IsLoopback)
            throw new ArgumentException("远端服务必须使用 HTTPS；HTTP 仅允许连接本机 localhost 或回环地址。");
        return address;
    }
    public static string ReadKeyFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new IOException("请选择 API Key 文件，或使用启动脚本配置连接。");
        var key = File.ReadAllText(path).Trim();
        if (key.Length is < 32 or > 256 || key.Any(char.IsControl)) throw new IOException("API Key 文件格式无效。");
        return key;
    }
    public async Task<T> Send<T>(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(method, path);
        if (_readKey != null) request.Headers.Add("X-Api-Key", _readKey());
        if (body != null) request.Content = JsonContent.Create(body, options: CrewJson.Options);
        using var response = await _http.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var code = "api_error"; var message = $"服务返回 HTTP {(int)response.StatusCode}，请检查连接和权限。";
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("code", out var c)) code = c.GetString() ?? code;
                if (doc.RootElement.TryGetProperty("detail", out var d)) message = d.GetString() ?? message;
            }
            catch (JsonException) { }
            throw new CrewApiException((int)response.StatusCode, code, message);
        }
        if (typeof(T) == typeof(bool) && response.StatusCode == HttpStatusCode.NoContent) return (T)(object)true;
        try { return JsonSerializer.Deserialize<T>(json, CrewJson.Options) ?? throw new JsonException(); }
        catch (JsonException) { throw new CrewApiException(502, "invalid_response", "服务返回的内容不是预期的 PromptCrew 数据。"); }
    }
    public Task<SessionDto> Session(CancellationToken ct = default) => Send<SessionDto>(HttpMethod.Get, "api/session", ct: ct);
    public Task<ProjectDto[]> Projects(CancellationToken ct = default) => Send<ProjectDto[]>(HttpMethod.Get, "api/projects", ct: ct);
    public Task<RoleTemplate[]> Templates(CancellationToken ct = default) => Send<RoleTemplate[]>(HttpMethod.Get, "api/employee-templates", ct: ct);
    public Task<ProjectDto> SaveProject(Guid? id, ProjectInput body, CancellationToken ct = default) => Send<ProjectDto>(id.HasValue ? HttpMethod.Put : HttpMethod.Post, id.HasValue ? $"api/projects/{id}" : "api/projects", body, ct: ct);
    public Task<EmployeeDto[]> Employees(Guid p, CancellationToken ct = default) => Send<EmployeeDto[]>(HttpMethod.Get, $"api/projects/{p}/employees", ct: ct);
    public Task<EmployeeDto> SaveEmployee(Guid p, Guid? id, EmployeeInput body, CancellationToken ct = default) => Send<EmployeeDto>(id.HasValue ? HttpMethod.Put : HttpMethod.Post, $"api/projects/{p}/employees" + (id.HasValue ? "/" + id : ""), body, ct: ct);
    public Task<ChainDto[]> Chains(Guid p, CancellationToken ct = default, Guid? employeeId = null) => Send<ChainDto[]>(HttpMethod.Get, $"api/projects/{p}/chains" + (employeeId.HasValue ? "?employeeId=" + employeeId : ""), ct: ct);
    public Task<ChainDto> SaveChain(Guid p, Guid? id, ChainInput body, CancellationToken ct = default) => Send<ChainDto>(id.HasValue ? HttpMethod.Put : HttpMethod.Post, $"api/projects/{p}/chains" + (id.HasValue ? "/" + id : ""), body, ct: ct);
    public Task<GraphDto> Graph(Guid p, Guid c, CancellationToken ct = default) => Send<GraphDto>(HttpMethod.Get, $"api/projects/{p}/chains/{c}/graph", ct: ct);
    public Task<ActivityDto[]> Activity(Guid p, CancellationToken ct = default) => Send<ActivityDto[]>(HttpMethod.Get, $"api/projects/{p}/activity", ct: ct);
    public Task<ActivityPageDto> ActivityPage(Guid p, Guid employee, int skip = 0, int take = 50, CancellationToken ct = default, Guid? beforeId = null) => Send<ActivityPageDto>(HttpMethod.Get, $"api/projects/{p}/activity-page?employeeId={employee}&skip={skip}&take={take}" + (beforeId.HasValue ? "&beforeId=" + beforeId : ""), ct: ct);
    public Task<RoundDto> Round(Guid p, Guid r, CancellationToken ct = default) => Send<RoundDto>(HttpMethod.Get, $"api/projects/{p}/rounds/{r}", ct: ct);
    public Task<RoundDto> ByRequest(Guid p, Guid r, CancellationToken ct = default) => Send<RoundDto>(HttpMethod.Get, $"api/projects/{p}/rounds/by-request/{r}", ct: ct);
    public Task<RoundSaved> SaveRound(Guid p, Guid c, RoundInput body, CancellationToken ct = default) => Send<RoundSaved>(HttpMethod.Post, $"api/projects/{p}/chains/{c}/rounds", body, ct: ct);
    public Task<RoundSaved> SaveResult(Guid p, Guid r, ResultInput body, CancellationToken ct = default) => Send<RoundSaved>(HttpMethod.Post, $"api/projects/{p}/rounds/{r}/results", body, ct: ct);
    public Task<ContextDto> Context(Guid p, Guid c, Guid employee, Guid? r, Guid[]? referenceIds = null, CancellationToken ct = default) => Send<ContextDto>(HttpMethod.Get,
        $"api/projects/{p}/chains/{c}/context?employeeId={employee}" + (r.HasValue ? "&roundId=" + r : "")
        + string.Concat((referenceIds ?? []).Select(x => "&referenceId=" + x)), ct: ct);
    public Task<ComparisonDto> Compare(Guid p, Guid a, Guid b, int? leftVersion = null, int? rightVersion = null, CancellationToken ct = default) => Send<ComparisonDto>(HttpMethod.Get,
        $"api/projects/{p}/compare?left={a}&right={b}" + (leftVersion.HasValue ? "&leftVersion=" + leftVersion : "") + (rightVersion.HasValue ? "&rightVersion=" + rightVersion : ""), ct: ct);
    public Task<PageDto<RoundDto>> Search(Guid p, SearchQuery q, CancellationToken ct = default) => Send<PageDto<RoundDto>>(HttpMethod.Get, $"api/projects/{p}/rounds?skip={q.Skip}&take={q.Take}"
        + (string.IsNullOrWhiteSpace(q.Text) ? "" : "&q=" + Uri.EscapeDataString(q.Text))
        + (q.EmployeeId.HasValue ? "&employeeId=" + q.EmployeeId : "") + (q.ChainId.HasValue ? "&chainId=" + q.ChainId : "")
        + (string.IsNullOrWhiteSpace(q.Tag) ? "" : "&tag=" + Uri.EscapeDataString(q.Tag)) + (q.Status.HasValue ? "&status=" + q.Status : ""), ct: ct);
    public Task<AccessKeyDto[]> Keys(CancellationToken ct = default) => Send<AccessKeyDto[]>(HttpMethod.Get, "api/access-keys", ct: ct);
    public Task<IssuedKey> IssueKey(AccessKeyInput body, CancellationToken ct = default) => Send<IssuedKey>(HttpMethod.Post, "api/access-keys", body, ct: ct);
    public Task<bool> RevokeKey(Guid id, CancellationToken ct = default) => Send<bool>(HttpMethod.Post, $"api/access-keys/{id}/revoke", ct: ct);
    public void Dispose() => _http.Dispose();
}
