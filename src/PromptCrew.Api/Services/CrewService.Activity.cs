using Microsoft.EntityFrameworkCore;
using PromptCrew.Api.Data;
using PromptCrew.Core;

namespace PromptCrew.Api.Services;

public sealed partial class CrewService
{
    public async Task<ActivityDto[]> Activity(Guid projectId, AccessScope actor, CancellationToken ct)
    {
        await Project(projectId, actor, ct);
        var query = db.Rounds.AsNoTracking().Where(x => x.ProjectId == projectId).OrderBy(x => x.CreatedUtc).ThenBy(x => x.Id);
        var rows = await ProjectActivity(query).ToArrayAsync(ct);
        return rows.Select(x => x with { CreatedUtc = Utc(x.CreatedUtc) }).ToArray();
    }
    private IQueryable<ActivityDto> ProjectActivity(IQueryable<PromptRound> query) =>
        from round in query
        join chain in db.Chains.AsNoTracking() on new { round.ProjectId, Id = round.ChainId } equals new { chain.ProjectId, chain.Id }
        select new ActivityDto(round.Id, round.ProjectId, round.ChainId, chain.Name, round.EmployeeId,
            round.Number, round.Input.Length > 160 ? round.Input.Substring(0, 160) : round.Input,
            round.Results.OrderByDescending(x => x.Version)
                .Select(x => x.Output.Length > 220 ? x.Output.Substring(0, 220) : x.Output).FirstOrDefault() ?? "",
            round.Status, round.Version, round.CreatedUtc, round.ParentRoundId, round.RevisesRoundId);

    public async Task<ActivityPageDto> ActivityPage(Guid projectId, Guid employeeId, int skip, int take, AccessScope actor, CancellationToken ct, Guid? beforeId = null)
    {
        await Project(projectId, actor, ct); await Employee(projectId, employeeId, ct);
        if (skip < 0 || take is < 1 or > 200) throw new CrewException(400, "invalid_page", "每页须为 1–200 条，起点不能为负数。");
        var query = db.Rounds.AsNoTracking().Where(x => x.ProjectId == projectId && x.EmployeeId == employeeId);
        var total = await query.CountAsync(ct);
        if (beforeId.HasValue)
        {
            var anchor = await query.SingleOrDefaultAsync(x => x.Id == beforeId, ct) ?? throw Missing("分页节点");
            query = query.Where(x => x.CreatedUtc < anchor.CreatedUtc || x.CreatedUtc == anchor.CreatedUtc && x.Id.CompareTo(anchor.Id) < 0);
        }
        var page = query.OrderByDescending(x => x.CreatedUtc).ThenByDescending(x => x.Id).Skip(skip).Take(take + 1);
        var rows = await ProjectActivity(page).ToArrayAsync(ct);
        var items = rows.Take(take).Select(x => x with { CreatedUtc = Utc(x.CreatedUtc) }).ToArray();
        var hasMore = rows.Length > take;
        return new(items, total, skip, take, hasMore ? items[^1].Id : null, hasMore);
    }
}
