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
    public CrewClient(string baseUrl, Func<string> readKey)
    {
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = Address(baseUrl), Timeout = TimeSpan.FromSeconds(20) };
        _readKey = readKey;
    }
    public CrewClient(HttpClient http) => _http = http;
    public static Uri Address(string value)
    {
        if (!Uri.TryCreate(value.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var address) ||
            address.Scheme is not ("http" or "https") || address.UserInfo.Length != 0 || address.Query.Length != 0 || address.Fragment.Length != 0)
            throw new ArgumentException("请输入不含凭据、查询参数的 http(s) API 根地址。");
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
    public Task<SessionDto> Session() => Send<SessionDto>(HttpMethod.Get, "api/session");
    public Task<ProjectDto[]> Projects() => Send<ProjectDto[]>(HttpMethod.Get, "api/projects");
    public Task<RoleTemplate[]> Templates() => Send<RoleTemplate[]>(HttpMethod.Get, "api/employee-templates");
    public Task<ProjectDto> SaveProject(Guid? id, ProjectInput body) => Send<ProjectDto>(id.HasValue ? HttpMethod.Put : HttpMethod.Post, id.HasValue ? $"api/projects/{id}" : "api/projects", body);
    public Task<EmployeeDto[]> Employees(Guid p) => Send<EmployeeDto[]>(HttpMethod.Get, $"api/projects/{p}/employees");
    public Task<EmployeeDto> SaveEmployee(Guid p, Guid? id, EmployeeInput body) => Send<EmployeeDto>(id.HasValue ? HttpMethod.Put : HttpMethod.Post, $"api/projects/{p}/employees" + (id.HasValue ? "/" + id : ""), body);
    public Task<ChainDto[]> Chains(Guid p) => Send<ChainDto[]>(HttpMethod.Get, $"api/projects/{p}/chains");
    public Task<ChainDto> SaveChain(Guid p, Guid? id, ChainInput body) => Send<ChainDto>(id.HasValue ? HttpMethod.Put : HttpMethod.Post, $"api/projects/{p}/chains" + (id.HasValue ? "/" + id : ""), body);
    public Task<GraphDto> Graph(Guid p, Guid c) => Send<GraphDto>(HttpMethod.Get, $"api/projects/{p}/chains/{c}/graph");
    public Task<RoundDto> Round(Guid p, Guid r) => Send<RoundDto>(HttpMethod.Get, $"api/projects/{p}/rounds/{r}");
    public Task<RoundDto> ByRequest(Guid p, Guid r) => Send<RoundDto>(HttpMethod.Get, $"api/projects/{p}/rounds/by-request/{r}");
    public Task<RoundSaved> SaveRound(Guid p, Guid c, RoundInput body) => Send<RoundSaved>(HttpMethod.Post, $"api/projects/{p}/chains/{c}/rounds", body);
    public Task<RoundSaved> SaveResult(Guid p, Guid r, ResultInput body) => Send<RoundSaved>(HttpMethod.Post, $"api/projects/{p}/rounds/{r}/results", body);
    public Task<ContextDto> Context(Guid p, Guid c, Guid employee, Guid? r, Guid[]? referenceIds = null) => Send<ContextDto>(HttpMethod.Get,
        $"api/projects/{p}/chains/{c}/context?employeeId={employee}" + (r.HasValue ? "&roundId=" + r : "")
        + string.Concat((referenceIds ?? []).Select(x => "&referenceId=" + x)));
    public Task<ComparisonDto> Compare(Guid p, Guid a, Guid b) => Send<ComparisonDto>(HttpMethod.Get, $"api/projects/{p}/compare?left={a}&right={b}");
    public Task<PageDto<RoundDto>> Search(Guid p, SearchQuery q) => Send<PageDto<RoundDto>>(HttpMethod.Get, $"api/projects/{p}/rounds?skip={q.Skip}&take={q.Take}"
        + (string.IsNullOrWhiteSpace(q.Text) ? "" : "&q=" + Uri.EscapeDataString(q.Text))
        + (q.EmployeeId.HasValue ? "&employeeId=" + q.EmployeeId : "") + (q.ChainId.HasValue ? "&chainId=" + q.ChainId : "")
        + (string.IsNullOrWhiteSpace(q.Tag) ? "" : "&tag=" + Uri.EscapeDataString(q.Tag)) + (q.Status.HasValue ? "&status=" + q.Status : ""));
    public Task<AccessKeyDto[]> Keys() => Send<AccessKeyDto[]>(HttpMethod.Get, "api/access-keys");
    public Task<IssuedKey> IssueKey(AccessKeyInput body) => Send<IssuedKey>(HttpMethod.Post, "api/access-keys", body);
    public Task<bool> RevokeKey(Guid id) => Send<bool>(HttpMethod.Post, $"api/access-keys/{id}/revoke");
    public void Dispose() => _http.Dispose();
}
