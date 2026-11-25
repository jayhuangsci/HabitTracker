using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PromptCrew.Api.Data;
using PromptCrew.Core;

namespace PromptCrew.Api.Services;

public sealed record AccessScope(bool IsAdmin, IReadOnlySet<Guid> ProjectIds, string Name)
{
    public static AccessScope Administrator { get; } = new(true, new HashSet<Guid>(), "本机管理员");
    public bool Allows(Guid projectId) => IsAdmin || ProjectIds.Contains(projectId);
    public void Require(Guid projectId)
    { if (!Allows(projectId)) throw new CrewException(403, "project_forbidden", "此 API Key 无权访问该项目。"); }
    public void RequireAdmin()
    { if (!IsAdmin) throw new CrewException(403, "admin_required", "此操作需要本机管理员密钥。"); }
}
public sealed record AdminCredential(string Token)
{
    public string Hash { get; } = TokenHash(Token);
    public static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static string NewToken() => "pc_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
public sealed class CrewAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, CrewDbContext db, AdminCredential admin)
    {
        if (!context.Request.Path.StartsWithSegments("/api")) { await next(context); return; }
        var token = context.Request.Headers["X-Api-Key"].ToString();
        if (string.IsNullOrEmpty(token) && context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = context.Request.Headers.Authorization.ToString()[7..];
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256) throw new CrewException(401, "api_key_required", "请通过 X-Api-Key 提供有效的 API Key。");
        var hash = AdminCredential.TokenHash(token);
        AccessScope? scope = null;
        if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(admin.Hash)))
            scope = AccessScope.Administrator;
        else
        {
            var key = await db.AccessKeys.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash && x.RevokedUtc == null, context.RequestAborted);
            if (key != null) scope = new(false, JsonSerializer.Deserialize<Guid[]>(key.ProjectsJson)!.ToHashSet(), key.Name);
        }
        if (scope == null) throw new CrewException(401, "api_key_invalid", "API Key 无效或已撤销。");
        context.Items[typeof(AccessScope)] = scope;
        await next(context);
    }
}
public static class ScopeExtensions
{
    public static AccessScope Actor(this HttpContext context) => (AccessScope)context.Items[typeof(AccessScope)]!;
}
