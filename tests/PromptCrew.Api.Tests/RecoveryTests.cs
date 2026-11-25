using System.Net;
using System.Net.Http.Json;
using PromptCrew.Core;

namespace PromptCrew.Api.Tests;

public sealed class RecoveryTests
{
    [Fact]
    public async Task NewDraftContextIncludesExplicitCrossChainReferencesAndRejectsOtherProjects()
    {
        using var app = new CrewFixture(); using var http = app.Admin();
        var project = await http.Project(); var employee = await http.Employee(project.Id); var chain = await http.Chain(project.Id);
        var otherChain = await http.Chain(project.Id, "美术成果");
        var art = await http.AddRound(project.Id, otherChain.Id, employee.Id, "青蓝色护盾素材");
        await http.AddResult(art.Round, "素材规格：256×256");
        var context = await (await http.GetAsync($"/api/projects/{project.Id}/chains/{chain.Id}/context?employeeId={employee.Id}&referenceId={art.Round.Id}")).Read<ContextDto>();
        Assert.Empty(context.Ancestors); Assert.Equal(art.Round.Id, Assert.Single(context.References).Id);
        Assert.Contains("256×256", context.CompiledContext);
        var outsider = await http.Project("另一个项目"); var outsiderEmployee = await http.Employee(outsider.Id); var outsiderChain = await http.Chain(outsider.Id);
        var foreign = await http.AddRound(outsider.Id, outsiderChain.Id, outsiderEmployee.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/api/projects/{project.Id}/chains/{chain.Id}/context?employeeId={employee.Id}&referenceId={foreign.Round.Id}")).StatusCode);
    }
    [Fact]
    public async Task FullyStoppedHostReopensImmutableRoundsAndOutputHistory()
    {
        using var directoryOwner = new CrewFixture();
        Guid p, r; int version;
        using (var first = new CrewFixture(directoryOwner.DirectoryPath))
        {
            using var http = first.Admin(); var project = await http.Project(); p = project.Id;
            var employee = await http.Employee(p); var chain = await http.Chain(p);
            var created = await http.AddRound(p, chain.Id, employee.Id, "重启前输入"); r = created.Round.Id;
            var result = await http.AddResult(created.Round, "结果一");
            var corrected = await http.AddResult(result.Round, "结果二"); version = corrected.Round.Version;
        }
        using var restarted = new CrewFixture(directoryOwner.DirectoryPath); using var reopened = restarted.Admin();
        var row = await (await reopened.GetAsync($"/api/projects/{p}/rounds/{r}")).Read<RoundDto>();
        Assert.Equal("重启前输入", row.Input); Assert.Equal(version, row.Version);
        Assert.Equal(new[] { "结果一", "结果二" }, row.Results.Select(x => x.Output));
    }
}
