using HabitTracker.Mobile.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HabitTracker.Mobile.Tests;

public class ConnectedJournalTests
{
    [Fact]
    public async Task AdapterWorksAgainstExistingWorkoutAndHabitEndpoints()
    {
        using var directory = new TemporaryDirectory();
        using var server = new WebApplicationFactory<global::Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing")
            .UseSetting("ConnectionStrings:DefaultConnection", "Data Source=" + Path.Combine(directory.PathName, "api.db") + ";Pooling=False")
            .UseSetting("DemoMode", "false"));
        using var client = server.CreateClient();
        var journal = new ApiWorkoutJournal(client);
        await journal.PingAsync();
        var workout = await journal.CreateAsync(new() { Name = "Phone via API", Date = new(2026, 11, 17) });
        var saved = await journal.AddSetAsync(workout.Id, new() { LeftPlates = [45, 25, 10], RightPlates = [45, 25, 10] });
        Assert.Equal(205, Assert.Single(saved.Sets).TotalWeight);
        await journal.CompleteAsync(workout.Id);
        Assert.True(Assert.Single(await journal.ListAsync()).IsCompleted);
        var habit = await journal.SaveHabitAsync(null, new("Daily walk", "km", "Recovery"));
        var first = await journal.SaveHabitLogAsync(new(habit.Id, new(2026, 11, 17), 5, null));
        var updated = await journal.SaveHabitLogAsync(new(habit.Id, first.Date, 6, "Updated"));
        Assert.Equal(first.Id, updated.Id);
        Assert.Equal(6, Assert.Single(await journal.ListHabitLogsAsync(habit.Id)).Quantity);
        await journal.DeleteHabitLogAsync(updated.Id);
        Assert.Empty(await journal.ListHabitLogsAsync(habit.Id));
        await journal.DeleteHabitAsync(habit.Id);
        Assert.Empty(await journal.ListHabitsAsync());
    }

    [Fact]
    public async Task BrowserCorsAllowsOnlyConfiguredPreviewOrigins()
    {
        using var directory = new TemporaryDirectory();
        using var server = new WebApplicationFactory<global::Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing")
            .UseSetting("ConnectionStrings:DefaultConnection", "Data Source=" + Path.Combine(directory.PathName, "api.db") + ";Pooling=False")
            .UseSetting("DemoMode", "false"));
        using var client = server.CreateClient();
        foreach (var origin in new[] { "http://localhost:5274", "https://untrusted.example" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Options, "/api/workouts");
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "POST");
            using var response = await client.SendAsync(request);
            var allowed = response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? values.Single() : null;
            Assert.Equal(origin == "http://localhost:5274" ? origin : null, allowed);
        }
    }
}
