using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HabitTracker.Api.Data;
using HabitTracker.Api.Entities;
using HabitTracker.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HabitTracker.Api.Tests;

public sealed class ApiFactory(string? database = null) : WebApplicationFactory<Program>
{
    public string DatabasePath { get; } = database ?? Path.Combine(Path.GetTempPath(), $"habittracker-test-{Guid.NewGuid():N}.db");
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing")
        .UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={DatabasePath};Pooling=False")
        .UseSetting("DemoMode", "false");
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) foreach (var path in new[] { DatabasePath, DatabasePath + "-wal", DatabasePath + "-shm" }) File.Delete(path);
    }
}

public class WorkoutApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static LogSetRequest SignatureSet() => new() { LeftPlates = [45, 25, 10], RightPlates = [45, 25, 10], Reps = 5, Rpe = 8 };
    private static async Task<WorkoutResponse> Create(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/workouts", new CreateWorkoutRequest { Name = "Upper body", Date = new(2026, 11, 15) }, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return (await response.Content.ReadFromJsonAsync<WorkoutResponse>(Json))!;
    }

    [Fact]
    public async Task SignatureLoadSurvivesSaveAndReload()
    {
        using var app = new ApiFactory();
        using var client = app.CreateClient();
        var workout = await Create(client);
        var response = await client.PostAsJsonAsync($"/api/workouts/{workout.Id}/sets", SignatureSet(), Json);
        response.EnsureSuccessStatusCode();
        var reloaded = await client.GetFromJsonAsync<WorkoutResponse>($"/api/workouts/{workout.Id}", Json);
        var set = Assert.Single(reloaded!.Sets);
        Assert.Equal(205, set.TotalWeight);
        Assert.Equal(new decimal[] { 45, 25, 10 }, set.LeftPlates);
        Assert.Equal(set.LeftPlates, set.RightPlates);
        Assert.Equal(1025, reloaded.VolumePounds);
    }

    [Fact]
    public async Task DuplicateClientSetReturnsConflictWithoutDoubleCounting()
    {
        using var app = new ApiFactory();
        using var client = app.CreateClient();
        var workout = await Create(client);
        var set = SignatureSet();
        (await client.PostAsJsonAsync($"/api/workouts/{workout.Id}/sets", set, Json)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/workouts/{workout.Id}/sets", set, Json)).StatusCode);
        Assert.Single((await client.GetFromJsonAsync<WorkoutResponse>($"/api/workouts/{workout.Id}", Json))!.Sets);
    }

    [Theory]
    [InlineData(0, 8, 45)]
    [InlineData(101, 8, 45)]
    [InlineData(5, 11, 45)]
    [InlineData(5, 8, -1)]
    public async Task InvalidSetIsRejected(int reps, decimal rpe, decimal plate)
    {
        using var app = new ApiFactory();
        using var client = app.CreateClient();
        var workout = await Create(client);
        var set = SignatureSet() with { Reps = reps, Rpe = rpe, LeftPlates = [plate] };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/workouts/{workout.Id}/sets", set, Json)).StatusCode);
    }

    [Fact]
    public async Task CompletedSessionsAreImmutableAndEmptySessionsCannotFinish()
    {
        using var app = new ApiFactory();
        using var client = app.CreateClient();
        var workout = await Create(client);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/workouts/{workout.Id}/complete", null)).StatusCode);
        var saved = await client.PostAsJsonAsync($"/api/workouts/{workout.Id}/sets", SignatureSet(), Json);
        var set = Assert.Single((await saved.Content.ReadFromJsonAsync<WorkoutResponse>(Json))!.Sets);
        (await client.PostAsync($"/api/workouts/{workout.Id}/complete", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/workouts/{workout.Id}/sets", SignatureSet(), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/workouts/{workout.Id}/sets/{set.Id}")).StatusCode);
    }

    [Fact]
    public async Task RemoveSetAndFilterAndExportWork()
    {
        using var app = new ApiFactory();
        using var client = app.CreateClient();
        var workout = await Create(client);
        var saved = await client.PostAsJsonAsync($"/api/workouts/{workout.Id}/sets", SignatureSet(), Json);
        var row = (await saved.Content.ReadFromJsonAsync<WorkoutResponse>(Json))!;
        var csv = await client.GetStringAsync("/api/workouts/export.csv");
        Assert.Contains("45 + 25 + 10", csv);
        Assert.Contains("\"205\"", csv);
        Assert.Empty((await client.GetFromJsonAsync<WorkoutResponse[]>("/api/workouts?from=2026-11-16", Json))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/workouts?from=2026-12-01&to=2026-11-01")).StatusCode);
        var removed = await client.DeleteAsync($"/api/workouts/{workout.Id}/sets/{row.Sets[0].Id}");
        Assert.Empty((await removed.Content.ReadFromJsonAsync<WorkoutResponse>(Json))!.Sets);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/workouts/999999")).StatusCode);
    }

    [Fact]
    public async Task UpgradePreservesExistingHabitData()
    {
        var path = Path.Combine(Path.GetTempPath(), $"habittracker-upgrade-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<HabitTrackerDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
        await using (var db = new HabitTrackerDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync("20260429062603_InitHabitTables");
            db.Habits.Add(new Habit { Name = "Original running habit", Unit = "km", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        using var app = new ApiFactory(path);
        using var client = app.CreateClient();
        var habits = await client.GetStringAsync("/api/habits");
        Assert.Contains("Original running habit", habits);
        await Create(client);
        var stats = await client.GetAsync("/api/stats/habits/1?range=week");
        stats.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await stats.Content.ReadAsStringAsync());
        Assert.Equal(7, payload.RootElement.GetProperty("points").GetArrayLength());
    }
}
