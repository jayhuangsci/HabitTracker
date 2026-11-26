using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PromptCrew.Api.Data;
using PromptCrew.Core;

namespace PromptCrew.Api.Services;

public sealed partial class CrewService(CrewDbContext db, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static DateTime Utc(DateTime date) => DateTime.SpecifyKind(date, DateTimeKind.Utc);
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, CrewJson.Options);
    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, CrewJson.Options)!;
    private static CrewException Missing(string name) => new(404, "not_found", $"{name}不存在或不属于该项目。");
    private void Touch(Entity entity) { entity.Version++; entity.UpdatedUtc = Now; }
    private void Stamp(Entity entity) { entity.CreatedUtc = Now; entity.UpdatedUtc = Now; }

    public async Task<ProjectDto[]> ListProjects(AccessScope actor, CancellationToken ct)
    {
        var query = db.Projects.AsNoTracking();
        if (!actor.IsAdmin) { var ids = actor.ProjectIds.ToArray(); query = query.Where(x => ids.Contains(x.Id)); }
        return (await query.OrderByDescending(x => x.UpdatedUtc).ToArrayAsync(ct)).Select(Map).ToArray();
    }
    public async Task<ProjectDto> GetProject(Guid projectId, AccessScope actor, CancellationToken ct) =>
        Map(await Project(projectId, actor, ct));

    private async Task<CrewProject> Project(Guid id, AccessScope actor, CancellationToken ct)
    {
        actor.Require(id);
        return await db.Projects.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing("项目");
    }
    public async Task<ProjectDto> SaveProject(Guid? id, ProjectInput input, AccessScope actor, CancellationToken ct)
    {
        Rules.EnumValue(input.Status);
        var name = Rules.Text(input.Name, "项目名称", 120, true, true);
        CrewProject row;
        if (id.HasValue) { row = await Project(id.Value, actor, ct); Rules.MatchVersion(row.Version, input.ExpectedVersion); Touch(row); }
        else { actor.RequireAdmin(); row = new(); Stamp(row); db.Projects.Add(row); }
        row.Name = name;
        row.Goal = Rules.Text(input.Goal, "项目目标", 10000);
        row.Background = Rules.Text(input.Background, "项目背景", 30000);
        row.Constraints = Rules.Text(input.Constraints, "项目约束", 10000);
        row.Status = input.Status;
        await db.SaveChangesAsync(ct);
        return Map(row);
    }
    public async Task<EmployeeDto[]> ListEmployees(Guid projectId, AccessScope actor, CancellationToken ct)
    {
        await Project(projectId, actor, ct);
        return (await db.Employees.AsNoTracking().Where(x => x.ProjectId == projectId).OrderBy(x => x.Name).ToArrayAsync(ct)).Select(Map).ToArray();
    }
    public async Task<EmployeeDto> GetEmployee(Guid projectId, Guid id, AccessScope actor, CancellationToken ct)
    { await Project(projectId, actor, ct); return Map(await Employee(projectId, id, ct)); }
    private async Task<Employee> Employee(Guid projectId, Guid id, CancellationToken ct) =>
        await db.Employees.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == id, ct) ?? throw Missing("员工");

    public async Task<EmployeeDto> SaveEmployee(Guid projectId, Guid? id, EmployeeInput input, AccessScope actor, CancellationToken ct)
    {
        var project = await Project(projectId, actor, ct);
        if (project.Status == ProjectState.Archived) throw new CrewException(409, "project_archived", "请先恢复已归档的项目。");
        var name = Rules.Text(input.Name, "员工名称", 100, true, true);
        Employee row;
        if (id.HasValue) { row = await Employee(projectId, id.Value, ct); Rules.MatchVersion(row.Version, input.ExpectedVersion); Touch(row); }
        else { row = new() { ProjectId = projectId }; Stamp(row); db.Employees.Add(row); }
        row.Name = name; row.NormalizedName = name.ToUpperInvariant();
        row.Role = Rules.Text(input.Role, "角色", 100, true, true);
        row.Responsibilities = Rules.Text(input.Responsibilities, "职责", 10000, true);
        row.BasePrompt = Rules.Text(input.BasePrompt, "基础提示词", 30000, true);
        row.SkillsJson = Json(Rules.Items(input.Skills, "Skill"));
        row.IsActive = input.IsActive;
        await db.SaveChangesAsync(ct);
        return Map(row);
    }
    public async Task<ChainDto[]> ListChains(Guid projectId, AccessScope actor, CancellationToken ct, Guid? employeeId = null)
    {
        await Project(projectId, actor, ct);
        var query = db.Chains.AsNoTracking().Where(x => x.ProjectId == projectId);
        if (employeeId.HasValue)
        {
            await Employee(projectId, employeeId.Value, ct);
            query = query.Where(x => db.Rounds.Any(r => r.ProjectId == projectId && r.ChainId == x.Id && r.EmployeeId == employeeId));
        }
        return (await query.OrderByDescending(x => x.CreatedUtc).ToArrayAsync(ct)).Select(Map).ToArray();
    }
    private async Task<TaskChain> Chain(Guid projectId, Guid id, CancellationToken ct) =>
        await db.Chains.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == id, ct) ?? throw Missing("任务链");
    public async Task<ChainDto> GetChain(Guid projectId, Guid id, AccessScope actor, CancellationToken ct)
    { await Project(projectId, actor, ct); return Map(await Chain(projectId, id, ct)); }
    public async Task<ChainDto> SaveChain(Guid projectId, Guid? id, ChainInput input, AccessScope actor, CancellationToken ct)
    {
        var project = await Project(projectId, actor, ct);
        Rules.EnumValue(input.Status);
        var creationHash = Hash(new { Name = Rules.Text(input.Name, "任务链名称", 120, true, true),
            Goal = Rules.Text(input.Goal, "任务目标", 10000, true), Acceptance = Rules.Text(input.AcceptanceCriteria, "完成标准", 10000), input.Status });
        if (!id.HasValue && input.CreateId.HasValue)
        {
            if (input.CreateId == Guid.Empty) throw new CrewException(400, "invalid_id", "任务创建 ID 不能为空。");
            var existing = await db.Chains.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.CreateId, ct);
            if (existing != null)
            {
                if (existing.ProjectId != projectId || existing.CreateFingerprint != creationHash)
                    throw new CrewException(409, "create_conflict", "此创建 ID 已被用于其他任务内容。");
                return Map(existing);
            }
        }
        if (project.Status == ProjectState.Archived) throw new CrewException(409, "project_archived", "请先恢复已归档的项目。");
        TaskChain row;
        if (id.HasValue) { row = await Chain(projectId, id.Value, ct); Rules.MatchVersion(row.Version, input.ExpectedVersion); Touch(row); }
        else { row = new() { Id = input.CreateId ?? Guid.NewGuid(), ProjectId = projectId, CreateFingerprint = input.CreateId.HasValue ? creationHash : null }; Stamp(row); db.Chains.Add(row); }
        row.Name = Rules.Text(input.Name, "任务链名称", 120, true, true);
        row.Goal = Rules.Text(input.Goal, "任务目标", 10000, true);
        row.AcceptanceCriteria = Rules.Text(input.AcceptanceCriteria, "完成标准", 10000);
        row.Status = input.Status;
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<AccessKeyDto[]> ListKeys(AccessScope actor, CancellationToken ct)
    { actor.RequireAdmin(); return (await db.AccessKeys.AsNoTracking().OrderByDescending(x => x.CreatedUtc).ToArrayAsync(ct)).Select(Map).ToArray(); }
    public async Task<IssuedKey> IssueKey(AccessKeyInput input, AccessScope actor, CancellationToken ct)
    {
        actor.RequireAdmin();
        var ids = input.ProjectIds?.Distinct().ToArray() ?? [];
        if (ids.Length is < 1 or > 32) throw new CrewException(400, "scope_required", "密钥须限定在 1–32 个已有项目内。");
        if (await db.Projects.CountAsync(x => ids.Contains(x.Id), ct) != ids.Length) throw Missing("授权项目");
        var token = AdminCredential.NewToken();
        var row = new AccessKey { Name = Rules.Text(input.Name, "密钥名称", 100, true, true),
            TokenHash = AdminCredential.TokenHash(token), ProjectsJson = Json(ids), CreatedUtc = Now };
        db.AccessKeys.Add(row); await db.SaveChangesAsync(ct);
        return new(token, Map(row));
    }
    public async Task RevokeKey(Guid id, AccessScope actor, CancellationToken ct)
    {
        actor.RequireAdmin();
        var row = await db.AccessKeys.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing("密钥");
        row.RevokedUtc ??= Now; await db.SaveChangesAsync(ct);
    }
    private static ProjectDto Map(CrewProject x) => new(x.Id, x.Name, x.Goal, x.Background, x.Constraints, x.Status, x.Version, Utc(x.CreatedUtc), Utc(x.UpdatedUtc));
    private static EmployeeDto Map(Employee x) => new(x.Id, x.ProjectId, x.Name, x.Role, x.Responsibilities, x.BasePrompt, Read<string[]>(x.SkillsJson), x.IsActive, x.Version, Utc(x.CreatedUtc));
    private static ChainDto Map(TaskChain x) => new(x.Id, x.ProjectId, x.Name, x.Goal, x.AcceptanceCriteria, x.Status, x.Version, Utc(x.CreatedUtc));
    private static AccessKeyDto Map(AccessKey x) => new(x.Id, x.Name, Read<Guid[]>(x.ProjectsJson), Utc(x.CreatedUtc), x.RevokedUtc.HasValue ? Utc(x.RevokedUtc.Value) : null);
}
