using System.Net;
using System.Net.Http.Json;
using PromptCrew.Client;
using PromptCrew.Core;

namespace PromptCrew.Api.Tests;

public sealed class QuickCaptureApiTests
{
    [Fact]
    public async Task NewTaskRetainsItsClientIdAcrossLostRepliesAndRejectsChangedContent()
    {
        using var app = new CrewFixture(); using var api = new CrewClient(app.Admin());
        var project = await api.SaveProject(null, new() { Name = "快速任务" });
        var body = new ChainInput { CreateId = Guid.NewGuid(), Name = "初稿", Goal = "完成初稿" };
        var first = await api.SaveChain(project.Id, null, body);
        var retry = await api.SaveChain(project.Id, null, body);
        Assert.Equal(body.CreateId, first.Id); Assert.Equal(first, retry);
        Assert.Single(await api.Chains(project.Id));
        var conflict = await Assert.ThrowsAsync<CrewApiException>(() => api.SaveChain(project.Id, null, body with { Name = "不同内容" }));
        Assert.Equal(409, conflict.Status);
        var other = await api.SaveProject(null, new() { Name = "其他项目" });
        Assert.Equal(409, (await Assert.ThrowsAsync<CrewApiException>(() => api.SaveChain(other.Id, null, body))).Status);
        using var http = app.Admin();
        var key = await (await http.PostAsJsonAsync("/api/access-keys", new AccessKeyInput { Name = "范围", ProjectIds = [project.Id] }, CrewJson.Options)).Read<IssuedKey>();
        using var scoped = new CrewClient(app.WithKey(key.Token));
        Assert.Equal(403, (await Assert.ThrowsAsync<CrewApiException>(() => scoped.SaveChain(other.Id, null, body))).Status);
    }
    [Fact]
    public async Task EmployeeActivityPagesAreBoundedOrderedAndProjectScoped()
    {
        using var app = new CrewFixture(); using var http = app.Admin(); using var api = new CrewClient(app.Admin());
        var project = await http.Project(); var employee = await http.Employee(project.Id); var other = await http.Employee(project.Id, 1);
        var chain = await http.Chain(project.Id); var ids = new List<Guid>();
        for (var i = 0; i < 7; i++) ids.Add((await http.AddRound(project.Id, chain.Id, employee.Id, $"轮次{i}")).Round.Id);
        await http.AddRound(project.Id, chain.Id, other.Id);
        var newest = await api.ActivityPage(project.Id, employee.Id, take: 3);
        var older = await api.ActivityPage(project.Id, employee.Id, skip: 3, take: 3);
        Assert.Equal(7, newest.Total); Assert.Equal(ids.AsEnumerable().Reverse().Take(3), newest.Items.Select(x => x.Id));
        Assert.Equal(ids.AsEnumerable().Reverse().Skip(3).Take(3), older.Items.Select(x => x.Id));
        Assert.All(newest.Items, x => Assert.Equal(employee.Id, x.EmployeeId));
        Assert.Equal(400, (await Assert.ThrowsAsync<CrewApiException>(() => api.ActivityPage(project.Id, employee.Id, take: 201))).Status);
        var forbidden = await http.Project("另一个项目");
        Assert.Equal(404, (await Assert.ThrowsAsync<CrewApiException>(() => api.ActivityPage(forbidden.Id, employee.Id))).Status);
    }
    [Theory]
    [InlineData("http://example.com")]
    [InlineData("http://192.168.1.8:5283")]
    [InlineData("https://user:secret@example.com")]
    public void RemoteCleartextAndEmbeddedCredentialsAreRejected(string address) => Assert.Throws<ArgumentException>(() => CrewClient.Address(address));
    [Theory]
    [InlineData("http://localhost:5283/")]
    [InlineData("http://127.0.0.1:5283/")]
    [InlineData("http://[::1]:5283/")]
    [InlineData("https://crew.example.com/")]
    public void LoopbackAndRemoteHttpsAreAccepted(string address) => Assert.Equal(address, CrewClient.Address(address).AbsoluteUri);
}
