using Microsoft.EntityFrameworkCore;
using PromptCrew.Core;

namespace PromptCrew.Api.Services;

public sealed partial class CrewService
{
    public async Task<ActivityDto[]> Activity(Guid projectId, AccessScope actor, CancellationToken ct)
    {
        await Project(projectId, actor, ct);
        var query = from round in db.Rounds.AsNoTracking()
                    join chain in db.Chains.AsNoTracking()
                        on new { round.ProjectId, Id = round.ChainId } equals new { chain.ProjectId, chain.Id }
                    where round.ProjectId == projectId
                    orderby round.CreatedUtc, round.Id
                    select new ActivityDto(round.Id, round.ProjectId, round.ChainId, chain.Name, round.EmployeeId,
                        round.Number, round.Input.Length > 160 ? round.Input.Substring(0, 160) : round.Input,
                        round.Results.OrderByDescending(x => x.Version)
                            .Select(x => x.Output.Length > 220 ? x.Output.Substring(0, 220) : x.Output).FirstOrDefault() ?? "",
                        round.Status, round.Version, round.CreatedUtc, round.ParentRoundId, round.RevisesRoundId);
        var rows = await query.ToArrayAsync(ct);
        return rows.Select(x => x with { CreatedUtc = Utc(x.CreatedUtc) }).ToArray();
    }
}
