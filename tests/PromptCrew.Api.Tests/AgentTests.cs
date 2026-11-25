using System.Net;
using System.Net.Http.Json;
using PromptCrew.Core;

namespace PromptCrew.Api.Tests;

public sealed class AgentTests
{
    [Fact]
    public async Task AgentPinsTheDefinitionItUsedAndOriginalRetrySurvivesRoleChanges()
    {
        using var app = new CrewFixture(); using var c = app.Admin();
        var p = await c.Project(); var e = await c.Employee(p.Id); var chain = await c.Chain(p.Id);
        var input = new RoundInput { ClientRequestId = Guid.NewGuid(), EmployeeId = e.Id, Input = "实际采用旧角色定义的输入", Source = EntrySource.Agent };
        var url = $"/api/projects/{p.Id}/chains/{chain.Id}/rounds";
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync(url, input, CrewJson.Options)).StatusCode);
        input = input with { ExpectedEmployeeVersion = e.Version };
        var saved = await (await c.PostAsJsonAsync(url, input, CrewJson.Options)).Read<RoundSaved>();
        await (await c.PutAsJsonAsync($"/api/projects/{p.Id}/employees/{e.Id}", new EmployeeInput { Name = e.Name, Role = e.Role,
            Responsibilities = e.Responsibilities, BasePrompt = "新版基础提示词", ExpectedVersion = e.Version }, CrewJson.Options)).Read<EmployeeDto>();
        var retry = await (await c.PostAsJsonAsync(url, input, CrewJson.Options)).Read<RoundSaved>();
        Assert.True(retry.Replayed); Assert.Equal(saved.Round.Id, retry.Round.Id); Assert.Equal(e.BasePrompt, retry.Round.EmployeeSnapshot.BasePrompt);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync(url, input with { ClientRequestId = Guid.NewGuid() }, CrewJson.Options)).StatusCode);
    }
    [Fact]
    public async Task SimultaneousResultsReportOneConflictInsteadOfReplacingEitherSubmission()
    {
        using var app = new CrewFixture(); using var c = app.Admin();
        var p = await c.Project(); var e = await c.Employee(p.Id); var chain = await c.Chain(p.Id);
        var round = (await c.AddRound(p.Id, chain.Id, e.Id)).Round;
        var posts = new[] { "Agent A", "Agent B" }.Select(output => c.PostAsJsonAsync($"/api/projects/{p.Id}/rounds/{round.Id}/results",
            new ResultInput { ClientRequestId = Guid.NewGuid(), ExpectedVersion = round.Version, Output = output }, CrewJson.Options));
        var responses = await Task.WhenAll(posts);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var row = await (await c.GetAsync($"/api/projects/{p.Id}/rounds/{round.Id}")).Read<RoundDto>();
        Assert.Single(row.Results); Assert.Equal(2, row.Version);
        var correction = await c.AddResult(row, "核对两份结果后的合并版本");
        Assert.Equal(2, correction.Round.Results.Length);
        Assert.Equal(row.LatestOutput, correction.Round.Results[0].Output);
    }
}
