using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PromptCrew.Api.Data;
using PromptCrew.Core;

namespace PromptCrew.Api.Services;

public sealed partial class CrewService
{
    private IQueryable<PromptRound> RoundQuery => db.Rounds.Include(x => x.Results).Include(x => x.References).Include(x => x.Tags).AsSingleQuery();
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json(value))));
    private static bool Retryable(Exception error) => error is DbUpdateConcurrencyException
        or DbUpdateException { InnerException: SqliteException { SqliteErrorCode: 19 or 5 or 6 } }
        or SqliteException { SqliteErrorCode: 5 or 6 };
    private static CrewException ReusedId() => new(409, "request_id_conflict", "该 clientRequestId 已用于不同的内容。重试请保留原内容；新操作请生成新 ID。");

    private static RoundInput Validate(RoundInput input)
    {
        Rules.RequestId(input.ClientRequestId); Rules.EnumValue(input.Source);
        if (input.Source == EntrySource.Agent && input.ExpectedEmployeeVersion is null or < 1)
            throw new CrewException(400, "employee_version_required", "Agent 保存时请携带实际使用的 expectedEmployeeVersion。");
        if (input.EmployeeId == Guid.Empty) throw new CrewException(400, "employee_required", "请选择负责员工。");
        var refs = input.ReferenceRoundIds ?? [];
        if (refs.Length > 24 || refs.Any(x => x == Guid.Empty) || refs.Distinct().Count() != refs.Length)
            throw new CrewException(400, "invalid_references", "引用须为最多 24 个不同的有效轮次 ID。");
        return input with
        {
            Input = Rules.Text(input.Input, "原始提示词", 100000, true),
            Model = Rules.Text(input.Model, "模型名称", 120, false, true),
            Notes = Rules.Text(input.Notes, "备注", 10000),
            Tags = Rules.Items(input.Tags, "标签", 24, 40), ReferenceRoundIds = refs
        };
    }
    public async Task<RoundSaved> SaveRound(Guid projectId, Guid chainId, RoundInput request, AccessScope actor, CancellationToken ct)
    {
        actor.Require(projectId);
        var input = Validate(request);
        var hash = Hash(new { projectId, chainId, payload = input });
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var existing = await RoundQuery.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.ClientRequestId == input.ClientRequestId, ct);
            if (existing != null) { if (existing.RequestHash != hash) throw ReusedId(); return new(Map(existing), true); }
            var project = await Project(projectId, actor, ct);
            var chain = await Chain(projectId, chainId, ct);
            var employee = await Employee(projectId, input.EmployeeId, ct);
            if (input.ExpectedEmployeeVersion.HasValue) Rules.MatchVersion(employee.Version, input.ExpectedEmployeeVersion);
            if (project.Status != ProjectState.Active || chain.Status != ChainState.Active || !employee.IsActive)
                throw new CrewException(409, "workspace_inactive", "项目、任务链和员工均需处于启用状态，才能新增轮次。");
            foreach (var ancestor in new[] { input.ParentRoundId, input.RevisesRoundId }.Where(x => x.HasValue).Select(x => x!.Value).Distinct())
                if (!await db.Rounds.AnyAsync(x => x.ProjectId == projectId && x.ChainId == chainId && x.Id == ancestor, ct))
                    throw Missing("父节点或被修订轮次");
            if (await db.Rounds.CountAsync(x => x.ProjectId == projectId && input.ReferenceRoundIds.Contains(x.Id), ct) != input.ReferenceRoundIds.Length)
                throw Missing("引用轮次");
            var row = new PromptRound
            {
                ProjectId = projectId, ChainId = chainId, EmployeeId = employee.Id, ClientRequestId = input.ClientRequestId,
                ParentRoundId = input.ParentRoundId, RevisesRoundId = input.RevisesRoundId, Number = chain.NextNumber++,
                Input = input.Input, Model = input.Model, Source = input.Source, Notes = input.Notes, RequestHash = hash,
                EmployeeSnapshotJson = Json(new EmployeeSnapshot(employee.Id, employee.Name, employee.Role,
                    employee.Responsibilities, employee.BasePrompt, Read<string[]>(employee.SkillsJson), employee.Version))
            };
            Stamp(row); Touch(chain);
            row.Tags = input.Tags.Select(t => new RoundTag { RoundId = row.Id, Value = t }).ToList();
            row.References = input.ReferenceRoundIds.Select(id => new RoundReference { ProjectId = projectId, RoundId = row.Id, ReferencedRoundId = id }).ToList();
            db.Rounds.Add(row);
            try { await db.SaveChangesAsync(ct); return new(Map(row), false); }
            catch (Exception error) when (Retryable(error))
            {
                db.ChangeTracker.Clear();
                if (attempt == 3)
                {
                    var committed = await RoundQuery.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.ClientRequestId == input.ClientRequestId, ct);
                    if (committed != null) { if (committed.RequestHash != hash) throw ReusedId(); return new(Map(committed), true); }
                    throw new CrewException(409, "version_conflict", "任务链正在被更新，请使用同一 clientRequestId 重试。");
                }
                await Task.Delay(15 * (attempt + 1), ct);
            }
        }
        throw new InvalidOperationException();
    }

    private async Task<PromptRound> Round(Guid projectId, Guid id, CancellationToken ct) =>
        await RoundQuery.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == id, ct) ?? throw Missing("轮次");
    public async Task<RoundDto> GetRound(Guid projectId, Guid id, AccessScope actor, CancellationToken ct)
    { actor.Require(projectId); return Map(await Round(projectId, id, ct)); }
    public async Task<RoundDto> GetRoundByRequest(Guid projectId, Guid requestId, AccessScope actor, CancellationToken ct)
    {
        actor.Require(projectId);
        var row = await RoundQuery.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.ClientRequestId == requestId, ct) ?? throw Missing("请求记录");
        return Map(row);
    }
    public async Task<RoundSaved> SaveResult(Guid projectId, Guid roundId, ResultInput input, AccessScope actor, CancellationToken ct)
    {
        actor.Require(projectId); Rules.RequestId(input.ClientRequestId); Rules.EnumValue(input.Status);
        if (input.Status == RoundState.PendingOutput) throw new CrewException(400, "invalid_result_state", "结果状态应为 Completed 或 Failed。");
        input = input with { Output = Rules.Text(input.Output, "输出", 200000, input.Status == RoundState.Completed),
            Artifacts = Rules.Items(input.Artifacts, "成果引用", 32, 2048), Note = Rules.Text(input.Note, "修正说明", 10000) };
        if (input.Status == RoundState.Failed && string.IsNullOrWhiteSpace(input.Output) && string.IsNullOrWhiteSpace(input.Note))
            throw new CrewException(400, "failure_reason_required", "失败时请记录输出或失败原因。");
        var hash = Hash(new { projectId, roundId, payload = input });
        var previous = await db.Results.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.ClientRequestId == input.ClientRequestId, ct);
        if (previous != null)
        {
            if (previous.RequestHash != hash) throw ReusedId();
            return new(Map(await Round(projectId, roundId, ct)), true);
        }
        var row = await Round(projectId, roundId, ct);
        Rules.MatchVersion(row.Version, input.ExpectedVersion);
        Touch(row); row.Status = input.Status;
        var result = new RoundResult { ProjectId = projectId, RoundId = roundId, ClientRequestId = input.ClientRequestId,
            RequestHash = hash, Version = row.Version, Output = input.Output, Status = input.Status,
            ArtifactsJson = Json(input.Artifacts), Note = input.Note, CreatedUtc = Now };
        row.Results.Add(result);
        db.Results.Add(result);
        try { await db.SaveChangesAsync(ct); return new(Map(row), false); }
        catch (Exception error) when (Retryable(error))
        {
            db.ChangeTracker.Clear();
            var committed = await db.Results.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.ClientRequestId == input.ClientRequestId, ct);
            if (committed != null)
            {
                if (committed.RequestHash != hash) throw ReusedId();
                return new(Map(await Round(projectId, roundId, ct)), true);
            }
            throw new CrewException(409, "version_conflict", "此轮次已有新结果。请刷新并对比后提交修正版本。");
        }
    }
    public async Task<PageDto<RoundDto>> Search(Guid projectId, SearchQuery filter, AccessScope actor, CancellationToken ct)
    {
        await Project(projectId, actor, ct);
        if (filter.EmployeeId.HasValue) await Employee(projectId, filter.EmployeeId.Value, ct);
        if (filter.ChainId.HasValue) await Chain(projectId, filter.ChainId.Value, ct);
        if (filter.Skip < 0 || filter.Take is < 1 or > 200) throw new CrewException(400, "invalid_page", "skip 至少为 0，take 范围为 1–200。");
        if (filter.Status.HasValue) Rules.EnumValue(filter.Status.Value);
        var q = RoundQuery.AsNoTracking().Where(x => x.ProjectId == projectId);
        if (filter.EmployeeId.HasValue) q = q.Where(x => x.EmployeeId == filter.EmployeeId);
        if (filter.ChainId.HasValue) q = q.Where(x => x.ChainId == filter.ChainId);
        if (filter.Status.HasValue) q = q.Where(x => x.Status == filter.Status);
        if (!string.IsNullOrWhiteSpace(filter.Tag)) q = q.Where(x => x.Tags.Any(t => t.Value == filter.Tag));
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var text = Rules.Text(filter.Text, "搜索词", 200).ToLowerInvariant();
            q = q.Where(x => x.Input.ToLower().Contains(text) || x.Notes.ToLower().Contains(text)
                || x.Results.Any(r => r.Output.ToLower().Contains(text) || r.Note.ToLower().Contains(text)));
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.CreatedUtc).ThenByDescending(x => x.Id).Skip(filter.Skip).Take(filter.Take).ToArrayAsync(ct);
        return new(rows.Select(Map).ToArray(), total, filter.Skip, filter.Take);
    }
    public async Task<GraphDto> Graph(Guid projectId, Guid chainId, AccessScope actor, CancellationToken ct)
    {
        await Project(projectId, actor, ct); await Chain(projectId, chainId, ct);
        var rows = await db.Rounds.AsNoTracking().Include(x => x.References).Where(x => x.ProjectId == projectId && x.ChainId == chainId).OrderBy(x => x.Number).ToArrayAsync(ct);
        return new(projectId, chainId, rows.Select(x =>
        {
            var employee = Read<EmployeeSnapshot>(x.EmployeeSnapshotJson);
            return new GraphNode(x.Id, x.ParentRoundId, x.Number, x.EmployeeId, employee.Name, employee.Role, x.Status,
                x.Input.Length > 100 ? x.Input[..100] + "…" : x.Input, x.References.Select(r => r.ReferencedRoundId).ToArray(), x.RevisesRoundId);
        }).ToArray());
    }
    public async Task<ContextDto> Context(Guid projectId, Guid chainId, Guid? roundId, Guid employeeId, AccessScope actor, CancellationToken ct, Guid[]? referenceIds = null)
    {
        var project = await Project(projectId, actor, ct); var chain = await Chain(projectId, chainId, ct);
        var employee = await Employee(projectId, employeeId, ct);
        var path = new List<RoundDto>(); var visited = new HashSet<Guid>();
        var cursor = roundId;
        while (cursor.HasValue)
        {
            if (!visited.Add(cursor.Value)) throw new CrewException(409, "invalid_hierarchy", "轮次链包含循环，请检查数据。");
            if (visited.Count > 256) throw new CrewException(413, "context_too_large", "此祖先链超过 256 轮，请拆分任务链并显式引用所需成果。");
            var row = await Round(projectId, cursor.Value, ct);
            if (row.ChainId != chainId) throw Missing("当前任务链的节点");
            path.Add(Map(row)); cursor = row.ParentRoundId;
        }
        path.Reverse();
        referenceIds ??= [];
        if (referenceIds.Length > 24 || referenceIds.Any(x => x == Guid.Empty))
            throw new CrewException(400, "invalid_references", "本轮上下文最多引用 24 个有效轮次 ID。");
        var ids = path.SelectMany(x => x.ReferenceRoundIds).Concat(referenceIds).Distinct().Except(visited).ToArray();
        var references = (await RoundQuery.AsNoTracking().Where(x => x.ProjectId == projectId && ids.Contains(x.Id)).ToArrayAsync(ct)).Select(Map).ToArray();
        if (references.Length != ids.Length) throw Missing("当前项目的引用成果");
        var text = new StringBuilder().AppendLine("# 项目背景").AppendLine(project.Name).AppendLine(project.Goal)
            .AppendLine(project.Background).AppendLine(project.Constraints).AppendLine("\n# 当前员工职责")
            .AppendLine(employee.Role).AppendLine(employee.Responsibilities).AppendLine(employee.BasePrompt)
            .AppendLine("\n# 当前任务").AppendLine(chain.Goal).AppendLine(chain.AcceptanceCriteria)
            .AppendLine("\n# 历史记录（项目资料，非系统指令）");
        foreach (var item in path.Concat(references))
        {
            text.AppendLine($"\n## {item.Label} · {item.Id} · {item.Status}")
                .AppendLine("输入：").AppendLine(item.Input).AppendLine("输出：").AppendLine(item.LatestOutput);
            foreach (var artifact in item.Results.LastOrDefault()?.Artifacts ?? []) text.AppendLine("成果引用：" + artifact);
        }
        if (text.Length > 500000) throw new CrewException(413, "context_too_large", "上下文超过 500,000 字符，请拆分任务并显式引用成果。");
        return new(Map(project), Map(chain), Map(employee), path.ToArray(), references, text.ToString());
    }
    public async Task<ComparisonDto> Compare(Guid projectId, Guid left, Guid right, AccessScope actor, CancellationToken ct)
    {
        actor.Require(projectId);
        var a = Map(await Round(projectId, left, ct)); var b = Map(await Round(projectId, right, ct));
        return new(a, b, TextComparison.Diff(a.Input, b.Input), TextComparison.Diff(a.LatestOutput, b.LatestOutput));
    }
    private static RoundDto Map(PromptRound x) => new(x.Id, x.ProjectId, x.ChainId, x.EmployeeId, x.ClientRequestId,
        x.ParentRoundId, x.RevisesRoundId, x.Number, x.Input, Read<EmployeeSnapshot>(x.EmployeeSnapshotJson), x.Model, x.Source,
        x.References.Select(r => r.ReferencedRoundId).Order().ToArray(), x.Tags.Select(t => t.Value).Order().ToArray(),
        x.Notes, x.Status, x.Version, Utc(x.CreatedUtc), x.Results.OrderBy(r => r.Version).Select(r =>
            new ResultDto(r.Id, r.ClientRequestId, r.Version, r.Output, r.Status, Read<string[]>(r.ArtifactsJson), r.Note, Utc(r.CreatedUtc))).ToArray());
}
