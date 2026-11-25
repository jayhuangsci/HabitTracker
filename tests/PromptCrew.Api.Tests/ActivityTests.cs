using System.Net;
using System.Net.Http.Json;
using PromptCrew.Core;

namespace PromptCrew.Api.Tests;

public sealed class ActivityTests
{
    [Fact]
    public async Task ProjectActivityIncludesEveryEmployeesWorkAcrossChainsAndOnlyLatestBoundedOutput()
    {
        using var app = new CrewFixture(); using var http = app.Admin();
        var project = await http.Project(); var designer = await http.Employee(project.Id); var programmer = await http.Employee(project.Id, 1);
        var firstChain = await http.Chain(project.Id); var secondChain = await http.Chain(project.Id, "另一项任务");
        var first = (await http.AddRound(project.Id, firstChain.Id, designer.Id, new string('规', 300))).Round;
        first = (await http.AddResult(first, "旧输出")).Round;
        first = (await http.AddResult(first, new string('新', 500))).Round;
        var next = (await http.AddRound(project.Id, firstChain.Id, programmer.Id, "接手实现", first.Id)).Round;
        var otherTask = (await http.AddRound(project.Id, secondChain.Id, designer.Id, "另一个任务的设计")).Round;
        var rows = await (await http.GetAsync($"/api/projects/{project.Id}/activity")).Read<ActivityDto[]>();
        Assert.Equal(new[] { first.Id, next.Id, otherTask.Id }, rows.Select(x => x.Id));
        Assert.Equal(new string('规', 160), rows[0].InputPreview); Assert.Equal(new string('新', 220), rows[0].OutputPreview);
        Assert.Equal(3, rows[0].Version); Assert.Equal(first.Id, rows[1].ParentRoundId);
        Assert.Equal("另一项任务", rows[2].ChainName); Assert.All(rows, x => Assert.Equal(DateTimeKind.Utc, x.CreatedUtc.Kind));
        Assert.Equal(2, rows.Count(x => x.EmployeeId == designer.Id));
        var complete = await (await http.GetAsync($"/api/projects/{project.Id}/rounds/{first.Id}")).Read<RoundDto>();
        Assert.Equal(300, complete.Input.Length); Assert.Equal(500, complete.LatestOutput.Length); Assert.Equal(2, complete.Results.Length);
    }
    [Fact]
    public async Task ActivityUsesProjectScopeAndReturnsEmptyHistoryForAnEmptyProject()
    {
        using var app = new CrewFixture(); using var admin = app.Admin();
        var allowed = await admin.Project(); var forbidden = await admin.Project("无权限项目");
        var key = await (await admin.PostAsJsonAsync("/api/access-keys", new AccessKeyInput { Name = "总览测试", ProjectIds = [allowed.Id] }, CrewJson.Options)).Read<IssuedKey>();
        using var client = app.WithKey(key.Token);
        Assert.Empty(await (await client.GetAsync($"/api/projects/{allowed.Id}/activity")).Read<ActivityDto[]>());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/projects/{forbidden.Id}/activity")).StatusCode);
    }
}
