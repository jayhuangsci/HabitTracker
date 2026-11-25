using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PromptCrew.Core;

namespace PromptCrew.Api.Tests;

public class CatalogTests
{
    [Fact]
    public async Task CatalogCrudRequiresKeyAndRetainsIndependentProjectOwnership()
    {
        using var app = new CrewFixture();
        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/projects")).StatusCode);
        using var client = app.Admin();
        var first = await client.Project(); var second = await client.Project("隔离项目");
        var employee = await client.Employee(first.Id); var chain = await client.Chain(first.Id);
        Assert.Equal(first.Id, employee.ProjectId); Assert.Equal(first.Id, chain.ProjectId);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/projects/{second.Id}/employees/{employee.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/projects/{second.Id}/chains/{chain.Id}")).StatusCode);
        Assert.Equal(4, (await client.GetFromJsonAsync<RoleTemplate[]>("/api/employee-templates", CrewJson.Options))!.Length);
    }

    [Fact]
    public async Task ExpectedVersionsRejectStaleProjectEmployeeAndChainChanges()
    {
        using var app = new CrewFixture(); using var client = app.Admin();
        var project = await client.Project(); var employee = await client.Employee(project.Id); var chain = await client.Chain(project.Id);
        var update = new ProjectInput { Name = "星港新版", ExpectedVersion = project.Version };
        var changed = await (await client.PutAsJsonAsync($"/api/projects/{project.Id}", update, CrewJson.Options)).Read<ProjectDto>();
        Assert.Equal(2, changed.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/projects/{project.Id}", update, CrewJson.Options)).StatusCode);
        var employeeUpdate = new EmployeeInput { Name = employee.Name, Role = employee.Role, Responsibilities = "新职责", BasePrompt = "新基础提示词", ExpectedVersion = 1 };
        await (await client.PutAsJsonAsync($"/api/projects/{project.Id}/employees/{employee.Id}", employeeUpdate, CrewJson.Options)).Read<EmployeeDto>();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/projects/{project.Id}/employees/{employee.Id}", employeeUpdate, CrewJson.Options)).StatusCode);
        var chainUpdate = new ChainInput { Name = chain.Name, Goal = chain.Goal, ExpectedVersion = 1 };
        await (await client.PutAsJsonAsync($"/api/projects/{project.Id}/chains/{chain.Id}", chainUpdate, CrewJson.Options)).Read<ChainDto>();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/projects/{project.Id}/chains/{chain.Id}", chainUpdate, CrewJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/projects/{project.Id}", update with { ExpectedVersion = null }, CrewJson.Options)).StatusCode);
    }

    [Fact]
    public async Task ScopedKeysListOnlyGrantedProjectsAndCannotManageKeysOrCreateProjects()
    {
        using var app = new CrewFixture(); using var admin = app.Admin();
        var first = await admin.Project(); var other = await admin.Project("不授权");
        var key = await (await admin.PostAsJsonAsync("/api/access-keys", new AccessKeyInput { Name = "游戏 Agent", ProjectIds = [first.Id] }, CrewJson.Options)).Read<IssuedKey>();
        using var agent = app.WithKey(key.Token);
        Assert.Equal(first.Id, Assert.Single((await agent.GetFromJsonAsync<ProjectDto[]>("/api/projects", CrewJson.Options))!).Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.GetAsync($"/api/projects/{other.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.GetAsync("/api/access-keys")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PostAsJsonAsync("/api/projects", new ProjectInput { Name = "越权" }, CrewJson.Options)).StatusCode);
        await admin.PostAsync($"/api/access-keys/{key.Key.Id}/revoke", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.GetAsync("/api/projects")).StatusCode);
    }

    [Fact]
    public async Task ReopeningTheSameDatabaseRetainsCatalogDataAndKeys()
    {
        using var owner = new CrewFixture();
        Guid projectId; string token;
        using (var client = owner.Admin())
        {
            var project = await client.Project(); projectId = project.Id;
            await client.Employee(projectId); await client.Chain(projectId);
            token = (await (await client.PostAsJsonAsync("/api/access-keys", new AccessKeyInput { Name = "持久化", ProjectIds = [projectId] }, CrewJson.Options)).Read<IssuedKey>()).Token;
        }
        // A fresh application host and DbContext graph use the same SQLite file.
        using var restarted = new CrewFixture(owner.DirectoryPath); using var agent = restarted.WithKey(token);
        Assert.Equal(projectId, (await agent.GetFromJsonAsync<ProjectDto>($"/api/projects/{projectId}", CrewJson.Options))!.Id);
        Assert.Single((await agent.GetFromJsonAsync<EmployeeDto[]>($"/api/projects/{projectId}/employees", CrewJson.Options))!);
        Assert.Single((await agent.GetFromJsonAsync<ChainDto[]>($"/api/projects/{projectId}/chains", CrewJson.Options))!);
    }

    [Fact]
    public async Task OpenApiDescribesKeyAuthenticationAndInvalidInputsReturnProblems()
    {
        using var app = new CrewFixture(); using var client = app.Admin();
        using var schema = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        Assert.Equal("X-Api-Key", schema.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey").GetProperty("name").GetString());
        var response = await client.PostAsJsonAsync("/api/projects", new ProjectInput(), CrewJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation_failed", error.RootElement.GetProperty("code").GetString());
    }
}
