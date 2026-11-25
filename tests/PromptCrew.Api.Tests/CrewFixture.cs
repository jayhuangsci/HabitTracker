using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;
using PromptCrew.Core;

namespace PromptCrew.Api.Tests;

public sealed class CrewFixture : WebApplicationFactory<Program>
{
    public const string TestKey = "test-only-promptcrew-key-not-for-production-123456";
    public string DirectoryPath { get; }
    private readonly bool _ownsDirectory;
    public CrewFixture(string? directory = null)
    {
        DirectoryPath = directory ?? Path.Combine(Path.GetTempPath(), "promptcrew-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath); _ownsDirectory = directory == null;
    }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PromptCrew:DataDirectory"] = DirectoryPath, ["PromptCrew:AdminKey"] = TestKey, ["PromptCrew:DemoMode"] = "false"
        }));
    }
    public HttpClient Admin()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey); return client;
    }
    public HttpClient WithKey(string key)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Api-Key", key); return client;
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && _ownsDirectory)
        {
            SqliteConnection.ClearAllPools();
            var full = Path.GetFullPath(DirectoryPath);
            var prefix = Path.Combine(Path.GetTempPath(), "promptcrew-tests-");
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected cleanup target.");
            if (Directory.Exists(full)) Directory.Delete(full, true);
        }
    }
}
public static class ApiHelpers
{
    public static async Task<T> Read<T>(this HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}");
        return System.Text.Json.JsonSerializer.Deserialize<T>(text, CrewJson.Options)!;
    }
    public static async Task<ProjectDto> Project(this HttpClient client, string name = "星港") =>
        await (await client.PostAsJsonAsync("/api/projects", new ProjectInput { Name = name, Goal = "可验证的游戏原型" }, CrewJson.Options)).Read<ProjectDto>();
    public static async Task<EmployeeDto> Employee(this HttpClient client, Guid projectId, int templateIndex = 0)
    {
        var template = RoleTemplates.All[templateIndex];
        return await (await client.PostAsJsonAsync($"/api/projects/{projectId}/employees", new EmployeeInput
        { Name = template.Name, Role = template.Role, Responsibilities = template.Responsibilities, BasePrompt = template.BasePrompt, Skills = template.Skills }, CrewJson.Options)).Read<EmployeeDto>();
    }
    public static async Task<ChainDto> Chain(this HttpClient client, Guid projectId, string name = "战斗系统") =>
        await (await client.PostAsJsonAsync($"/api/projects/{projectId}/chains",
            new ChainInput { Name = name, Goal = "完成原型", AcceptanceCriteria = "规则明确且可以运行" }, CrewJson.Options)).Read<ChainDto>();
}
