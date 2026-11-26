using System.Net;
using System.Net.Http.Json;
using PromptCrew.Client;
using PromptCrew.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PromptCrew.Api.Data;
using PromptCrew.Api.Services;

namespace PromptCrew.Api.Tests;

public sealed class QuickCaptureApiTests
{
    [Fact]
    public async Task OriginalDatabaseMigratesWithoutChangingInputsSnapshotsOrOutputVersions()
    {
        using var owner = new CrewFixture();
        var path = Path.Combine(owner.DirectoryPath, "promptcrew.db");
        RoundDto row;
        await using (var db = new CrewDbContext(new DbContextOptionsBuilder<CrewDbContext>().UseSqlite("Data Source=" + path).Options))
        {
            await db.GetService<IMigrator>().MigrateAsync("20261125085943_InitialPromptCrew");
            var project = new CrewProject { Name = "旧数据", CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow };
            var employee = new Employee { ProjectId = project.Id, Name = "旧员工", NormalizedName = "旧员工", Role = "程序", Responsibilities = "保留", BasePrompt = "原定义" };
            var chain = new TaskChain { ProjectId = project.Id, Name = "旧任务", Goal = "恢复" };
            // Save against the old schema without the newly introduced optional column.
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Projects (Id,Name,Goal,Background,Constraints,Status,Version,CreatedUtc,UpdatedUtc) VALUES ({project.Id},{project.Name},{""},{""},{""},{0},{1},{project.CreatedUtc},{project.UpdatedUtc})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Employees (Id,ProjectId,Name,NormalizedName,Role,Responsibilities,BasePrompt,SkillsJson,IsActive,Version,CreatedUtc,UpdatedUtc) VALUES ({employee.Id},{project.Id},{employee.Name},{employee.NormalizedName},{employee.Role},{employee.Responsibilities},{employee.BasePrompt},{"[]"},{true},{1},{project.CreatedUtc},{project.UpdatedUtc})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Chains (Id,ProjectId,Name,Goal,AcceptanceCriteria,Status,NextNumber,Version,CreatedUtc,UpdatedUtc) VALUES ({chain.Id},{project.Id},{chain.Name},{chain.Goal},{""},{0},{1},{1},{project.CreatedUtc},{project.UpdatedUtc})");
            var snapshot = new EmployeeSnapshot(employee.Id, employee.Name, employee.Role, employee.Responsibilities, employee.BasePrompt, [], 1);
            var roundId = Guid.NewGuid(); var requestId = Guid.NewGuid(); var resultId = Guid.NewGuid(); var resultRequest = Guid.NewGuid();
            var input = "  旧提示词\n原样保存  "; var snapshotJson = System.Text.Json.JsonSerializer.Serialize(snapshot, CrewJson.Options);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Rounds (Id,ProjectId,ChainId,EmployeeId,ClientRequestId,Number,RequestHash,Input,EmployeeSnapshotJson,Model,Source,Notes,Status,Version,CreatedUtc,UpdatedUtc) VALUES ({roundId},{project.Id},{chain.Id},{employee.Id},{requestId},{1},{"legacy"},{input},{snapshotJson},{""},{0},{""},{1},{2},{project.CreatedUtc},{project.UpdatedUtc})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Results (Id,ProjectId,RoundId,ClientRequestId,RequestHash,Version,Output,Status,ArtifactsJson,Note,CreatedUtc) VALUES ({resultId},{project.Id},{roundId},{resultRequest},{"legacy"},{2},{"旧成果"},{1},{"[]"},{""},{project.CreatedUtc})");
            row = new(roundId, project.Id, chain.Id, employee.Id, requestId, null, null, 1, input, snapshot, "", EntrySource.Manual, [], [], "", RoundState.Completed, 2, project.CreatedUtc,
                [new(resultId, resultRequest, 2, "旧成果", RoundState.Completed, [], "", project.CreatedUtc)]);
        }
        using var migrated = new CrewFixture(owner.DirectoryPath); using var api = new CrewClient(migrated.Admin());
        var restored = await api.Round(row.ProjectId, row.Id);
        Assert.Equal(row.Input, restored.Input); Assert.Equal(System.Text.Json.JsonSerializer.Serialize(row.EmployeeSnapshot, CrewJson.Options), System.Text.Json.JsonSerializer.Serialize(restored.EmployeeSnapshot, CrewJson.Options));
        Assert.Equal(row.Results.Select(x => x.Output), restored.Results.Select(x => x.Output)); Assert.Equal(row.Version, restored.Version);
    }
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
        var edited = await api.SaveChain(project.Id, first.Id, new() { Name = "其他客户端改名", Goal = "继续完善", ExpectedVersion = first.Version });
        Assert.Equal(edited.Id, (await api.SaveChain(project.Id, null, body)).Id);
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
        Assert.True(newest.HasMore);
        await http.AddRound(project.Id, chain.Id, employee.Id, "其他客户端追加的新记录");
        var stableOlder = await api.ActivityPage(project.Id, employee.Id, take: 3, beforeId: newest.NextCursor);
        Assert.Equal(older.Items.Select(x => x.Id), stableOlder.Items.Select(x => x.Id));
        var last = await api.ActivityPage(project.Id, employee.Id, take: 3, beforeId: stableOlder.NextCursor);
        Assert.Single(last.Items); Assert.False(last.HasMore); Assert.Null(last.NextCursor);
        Assert.Equal(chain.Id, Assert.Single(await api.Chains(project.Id, employeeId: employee.Id)).Id);
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
