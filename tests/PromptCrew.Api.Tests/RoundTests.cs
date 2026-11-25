using System.Net;
using System.Net.Http.Json;
using PromptCrew.Core;

namespace PromptCrew.Api.Tests;

public class RoundTests
{
    [Fact]
    public async Task RawInputAndEmployeeDefinitionSnapshotsRemainUnchangedAfterEmployeeEdit()
    {
        using var app = new CrewFixture(); using var c = app.Admin();
        var p = await c.Project(); var e = await c.Employee(p.Id); var chain = await c.Chain(p.Id);
        const string original = "  原始提示词\r\n保留空格与换行\n";
        var round = (await c.AddRound(p.Id, chain.Id, e.Id, original)).Round;
        await (await c.PutAsJsonAsync($"/api/projects/{p.Id}/employees/{e.Id}", new EmployeeInput
        { Name = "新版设计师", Role = "新角色", Responsibilities = "新的职责", BasePrompt = "新的基础提示词", Skills = ["new-skill"], ExpectedVersion = e.Version }, CrewJson.Options)).Read<EmployeeDto>();
        var recovered = (await c.GetFromJsonAsync<RoundDto>($"/api/projects/{p.Id}/rounds/{round.Id}", CrewJson.Options))!;
        Assert.Equal(original, recovered.Input); Assert.Equal(e.BasePrompt, recovered.EmployeeSnapshot.BasePrompt);
        Assert.Equal(e.Name, recovered.EmployeeSnapshot.Name); Assert.Equal(1, recovered.EmployeeSnapshot.DefinitionVersion);
        Assert.Equal(e.Skills, recovered.EmployeeSnapshot.Skills);
    }
    [Fact]
    public async Task DuplicateRoundReturnsExistingButDifferentPayloadOrChainConflicts()
    {
        using var app = new CrewFixture(); using var c = app.Admin();
        var p = await c.Project(); var e = await c.Employee(p.Id); var chain = await c.Chain(p.Id); var other = await c.Chain(p.Id, "另一链");
        var input = new RoundInput { ClientRequestId = Guid.NewGuid(), EmployeeId = e.Id, Input = "保存一次" };
        var url = $"/api/projects/{p.Id}/chains/{chain.Id}/rounds";
        var first = await (await c.PostAsJsonAsync(url, input, CrewJson.Options)).Read<RoundSaved>();
        var second = await (await c.PostAsJsonAsync(url, input, CrewJson.Options)).Read<RoundSaved>();
        Assert.Equal(first.Round.Id, second.Round.Id); Assert.True(second.Replayed);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync(url, input with { Input = "不同内容" }, CrewJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/projects/{p.Id}/chains/{other.Id}/rounds", input, CrewJson.Options)).StatusCode);
        Assert.Equal(first.Round.Id, (await c.GetFromJsonAsync<RoundDto>($"/api/projects/{p.Id}/rounds/by-request/{input.ClientRequestId}", CrewJson.Options))!.Id);
    }
    [Fact]
    public async Task AncestorContextExcludesSiblingBranchAndIncludesExplicitCrossChainReferences()
    {
        using var app = new CrewFixture(); using var c = app.Admin();
        var p = await c.Project(); var designer = await c.Employee(p.Id); var programmer = await c.Employee(p.Id, 1);
        var chain = await c.Chain(p.Id); var artChain = await c.Chain(p.Id, "素材规范");
        var asset = (await c.AddRound(p.Id, artChain.Id, designer.Id, "画风规范")).Round;
        asset = (await c.AddResult(asset, "像素风，32 像素网格")).Round;
        var root = (await c.AddRound(p.Id, chain.Id, designer.Id, "根规则")).Round;
        root = (await c.AddResult(root, "设计成果")).Round;
        var turn = (await c.AddRound(p.Id, chain.Id, programmer.Id, "实现回合制", root.Id, [asset.Id])).Round;
        var realtime = (await c.AddRound(p.Id, chain.Id, programmer.Id, "实现即时制", root.Id)).Round;
        var context = (await c.GetFromJsonAsync<ContextDto>($"/api/projects/{p.Id}/chains/{chain.Id}/context?employeeId={programmer.Id}&roundId={turn.Id}", CrewJson.Options))!;
        Assert.Equal(new[] { root.Id, turn.Id }, context.Ancestors.Select(x => x.Id));
        Assert.Equal(asset.Id, Assert.Single(context.References).Id);
        Assert.DoesNotContain(realtime.Id.ToString(), context.CompiledContext);
        Assert.Contains("像素风", context.CompiledContext); Assert.Equal(programmer.Id, context.Employee.Id);
        var graph = (await c.GetFromJsonAsync<GraphDto>($"/api/projects/{p.Id}/chains/{chain.Id}/graph", CrewJson.Options))!;
        Assert.Equal(3, graph.Nodes.Length); Assert.Equal(2, graph.Nodes.Count(x => x.ParentRoundId == root.Id));
        Assert.Single(graph.Nodes.Single(x => x.Id == turn.Id).ReferenceRoundIds);
    }
    [Fact]
    public async Task CrossProjectEmployeesParentsReferencesAndCrossChainParentsAreRejected()
    {
        using var app = new CrewFixture(); using var c = app.Admin();
        var p = await c.Project(); var q = await c.Project("另一个项目");
        var e = await c.Employee(p.Id); var foreign = await c.Employee(q.Id);
        var chain = await c.Chain(p.Id); var other = await c.Chain(p.Id, "同项目其他链"); var foreignChain = await c.Chain(q.Id);
        var foreignRound = (await c.AddRound(q.Id, foreignChain.Id, foreign.Id)).Round;
        var otherRound = (await c.AddRound(p.Id, other.Id, e.Id)).Round;
        var url = $"/api/projects/{p.Id}/chains/{chain.Id}/rounds";
        var input = new RoundInput { ClientRequestId = Guid.NewGuid(), EmployeeId = e.Id, Input = "尝试非法关联" };
        foreach (var bad in new[] { input with { EmployeeId = foreign.Id }, input with { ParentRoundId = foreignRound.Id },
            input with { ParentRoundId = otherRound.Id }, input with { ReferenceRoundIds = [foreignRound.Id] },
            input with { ParentRoundId = input.ClientRequestId }, input with { RevisesRoundId = foreignRound.Id } })
            Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync(url, bad, CrewJson.Options)).StatusCode);
        var graph = (await c.GetFromJsonAsync<GraphDto>($"/api/projects/{p.Id}/chains/{chain.Id}/graph", CrewJson.Options))!;
        Assert.Empty(graph.Nodes);
    }
    [Fact]
    public async Task PublishedInputsAndParentLinksHaveNoMutationEndpointAndRevisionsCreateNewRounds()
    {
        using var app = new CrewFixture(); using var c = app.Admin();
        var p = await c.Project(); var e = await c.Employee(p.Id); var chain = await c.Chain(p.Id);
        var original = (await c.AddRound(p.Id, chain.Id, e.Id, "原始输入")).Round;
        var edited = await (await c.PostAsJsonAsync($"/api/projects/{p.Id}/chains/{chain.Id}/rounds", new RoundInput
        { ClientRequestId = Guid.NewGuid(), EmployeeId = e.Id, RevisesRoundId = original.Id, Input = "修订输入" }, CrewJson.Options)).Read<RoundSaved>();
        Assert.NotEqual(original.Id, edited.Round.Id); Assert.Equal(original.Id, edited.Round.RevisesRoundId);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await c.PutAsJsonAsync($"/api/projects/{p.Id}/rounds/{original.Id}", new { parentRoundId = edited.Round.Id, input = "覆盖" })).StatusCode);
        var compare = (await c.GetFromJsonAsync<ComparisonDto>($"/api/projects/{p.Id}/compare?left={original.Id}&right={edited.Round.Id}", CrewJson.Options))!;
        Assert.Equal("原始输入", compare.Left.Input); Assert.Contains("− 原始输入", compare.InputDiff); Assert.Contains("+ 修订输入", compare.InputDiff);
    }
    [Fact]
    public async Task OutputCorrectionsAppendVersionsAndReplayDoesNotAppendAgain()
    {
        using var app = new CrewFixture(); using var c = app.Admin();
        var p = await c.Project(); var e = await c.Employee(p.Id); var chain = await c.Chain(p.Id);
        var round = (await c.AddRound(p.Id, chain.Id, e.Id)).Round;
        var url = $"/api/projects/{p.Id}/rounds/{round.Id}/results";
        var first = new ResultInput { ClientRequestId = Guid.NewGuid(), ExpectedVersion = 1, Status = RoundState.Failed, Note = "工具执行失败" };
        var failed = await (await c.PostAsJsonAsync(url, first, CrewJson.Options)).Read<RoundSaved>();
        Assert.Equal(2, failed.Round.Version); Assert.Equal(RoundState.Failed, failed.Round.Status);
        var second = new ResultInput { ClientRequestId = Guid.NewGuid(), ExpectedVersion = 2, Output = "完成的代码", Artifacts = ["src/Battle.cs"], Note = "重试完成" };
        var completed = await (await c.PostAsJsonAsync(url, second, CrewJson.Options)).Read<RoundSaved>();
        Assert.Equal(3, completed.Round.Version); Assert.Equal(2, completed.Round.Results.Length);
        Assert.Equal("工具执行失败", completed.Round.Results[0].Note);
        Assert.Equal("完成的代码", completed.Round.LatestOutput);
        var replay = await (await c.PostAsJsonAsync(url, second, CrewJson.Options)).Read<RoundSaved>();
        Assert.True(replay.Replayed); Assert.Equal(2, replay.Round.Results.Length);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync(url, second with { Output = "悄悄覆盖" }, CrewJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync(url, second with { ClientRequestId = Guid.NewGuid(), ExpectedVersion = 1 }, CrewJson.Options)).StatusCode);
    }
    [Fact]
    public async Task ConcurrentRoundAppendsKeepEveryRecordAndAllocateDistinctNumbers()
    {
        using var app = new CrewFixture(); using var c = app.Admin();
        var p = await c.Project(); var e = await c.Employee(p.Id); var chain = await c.Chain(p.Id);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(i => c.AddRound(p.Id, chain.Id, e.Id, "并发轮次 " + i)));
        var graph = (await c.GetFromJsonAsync<GraphDto>($"/api/projects/{p.Id}/chains/{chain.Id}/graph", CrewJson.Options))!;
        Assert.Equal(4, graph.Nodes.Length); Assert.Equal(new[] { 1, 2, 3, 4 }, graph.Nodes.Select(x => x.Number));
    }
    [Fact]
    public async Task ConcurrentRetriesWithOneRequestIdCreateOnlyOneRound()
    {
        using var app = new CrewFixture(); using var c = app.Admin();
        var p = await c.Project(); var e = await c.Employee(p.Id); var chain = await c.Chain(p.Id);
        var input = new RoundInput { ClientRequestId = Guid.NewGuid(), EmployeeId = e.Id, Input = "网络重试" };
        var replies = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
            await (await c.PostAsJsonAsync($"/api/projects/{p.Id}/chains/{chain.Id}/rounds", input, CrewJson.Options)).Read<RoundSaved>()));
        Assert.Single(replies.Select(x => x.Round.Id).Distinct());
    }
    [Fact]
    public async Task SearchCombinesTextEmployeeTagsStatusAndPaginationAcrossOutputHistory()
    {
        using var app = new CrewFixture(); using var c = app.Admin();
        var p = await c.Project(); var e = await c.Employee(p.Id); var other = await c.Employee(p.Id, 1); var chain = await c.Chain(p.Id);
        var a = (await c.AddRound(p.Id, chain.Id, e.Id, "战斗提示词")).Round;
        a = (await c.AddResult(a, "伤害倍率 1.5")).Round;
        a = (await c.AddResult(a, "修正为 1.2")).Round;
        await c.AddRound(p.Id, chain.Id, other.Id, "另一个输入");
        var page = (await c.GetFromJsonAsync<PageDto<RoundDto>>($"/api/projects/{p.Id}/rounds?q={Uri.EscapeDataString("倍率")}&employeeId={e.Id}&tag={Uri.EscapeDataString("战斗")}&status=Completed&take=1", CrewJson.Options))!;
        Assert.Equal(a.Id, Assert.Single(page.Items).Id); Assert.Equal(1, page.Total);
        var all = (await c.GetFromJsonAsync<PageDto<RoundDto>>($"/api/projects/{p.Id}/rounds?skip=1&take=1", CrewJson.Options))!;
        Assert.Single(all.Items); Assert.Equal(2, all.Total);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/api/projects/{p.Id}/rounds?take=201")).StatusCode);
    }
    [Fact]
    public async Task RestartRetainsBranchesOutputsAndIdempotencyIdentity()
    {
        using var owner = new CrewFixture(); Guid projectId, chainId, employeeId, rootId;
        var inputId = Guid.NewGuid();
        using (var c = owner.Admin())
        {
            var p = await c.Project(); projectId = p.Id;
            var e = await c.Employee(p.Id); employeeId = e.Id;
            var chain = await c.Chain(p.Id); chainId = chain.Id;
            var root = (await c.AddRound(p.Id, chain.Id, e.Id)).Round; rootId = root.Id;
            await c.AddResult(root, "持久化结果");
            await c.AddRound(p.Id, chain.Id, e.Id, "恢复后仍在的分支", root.Id);
            await (await c.PostAsJsonAsync($"/api/projects/{p.Id}/chains/{chain.Id}/rounds", new RoundInput
            { ClientRequestId = inputId, EmployeeId = e.Id, ParentRoundId = root.Id, Input = "未收到确认" }, CrewJson.Options)).Read<RoundSaved>();
        }
        using var restarted = new CrewFixture(owner.DirectoryPath); using var next = restarted.Admin();
        var restored = (await next.GetFromJsonAsync<RoundDto>($"/api/projects/{projectId}/rounds/{rootId}", CrewJson.Options))!;
        Assert.Equal("持久化结果", restored.LatestOutput);
        var saved = await (await next.PostAsJsonAsync($"/api/projects/{projectId}/chains/{chainId}/rounds", new RoundInput
        { ClientRequestId = inputId, EmployeeId = employeeId, ParentRoundId = rootId, Input = "未收到确认" }, CrewJson.Options)).Read<RoundSaved>();
        Assert.True(saved.Replayed);
        Assert.Equal(3, (await next.GetFromJsonAsync<GraphDto>($"/api/projects/{projectId}/chains/{chainId}/graph", CrewJson.Options))!.Nodes.Length);
    }
    [Fact]
    public async Task ProjectScopesApplyToSearchGraphContextAndResultWrites()
    {
        using var app = new CrewFixture(); using var admin = app.Admin();
        var p = await admin.Project(); var q = await admin.Project("私有项目"); var e = await admin.Employee(q.Id); var chain = await admin.Chain(q.Id);
        var row = (await admin.AddRound(q.Id, chain.Id, e.Id)).Round;
        var issued = await (await admin.PostAsJsonAsync("/api/access-keys", new AccessKeyInput { Name = "受限", ProjectIds = [p.Id] }, CrewJson.Options)).Read<IssuedKey>();
        using var agent = app.WithKey(issued.Token);
        foreach (var route in new[] { $"/api/projects/{q.Id}/rounds", $"/api/projects/{q.Id}/rounds/{row.Id}",
            $"/api/projects/{q.Id}/chains/{chain.Id}/graph", $"/api/projects/{q.Id}/chains/{chain.Id}/context?employeeId={e.Id}&roundId={row.Id}" })
            Assert.Equal(HttpStatusCode.Forbidden, (await agent.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PostAsJsonAsync($"/api/projects/{q.Id}/rounds/{row.Id}/results",
            new ResultInput { ClientRequestId = Guid.NewGuid(), ExpectedVersion = 1, Output = "越权" }, CrewJson.Options)).StatusCode);
    }
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankPromptInputsAreRejected(string input)
    {
        using var app = new CrewFixture(); using var c = app.Admin();
        var p = await c.Project(); var e = await c.Employee(p.Id); var chain = await c.Chain(p.Id);
        var response = await c.PostAsJsonAsync($"/api/projects/{p.Id}/chains/{chain.Id}/rounds",
            new RoundInput { ClientRequestId = Guid.NewGuid(), EmployeeId = e.Id, Input = input }, CrewJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Fact]
    public void TextComparisonPreservesReadableUnicodeChangesAndHandlesEqualInputs()
    {
        Assert.Equal("内容相同", TextComparison.Diff("相同\r\n内容", "相同\n内容"));
        var diff = TextComparison.Diff("共同\n旧规则\n尾部", "共同\n新规则\n尾部");
        Assert.Contains("− 旧规则", diff); Assert.Contains("+ 新规则", diff); Assert.Contains("  共同", diff);
    }
}
