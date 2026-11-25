using HabitTracker.Core;
using HabitTracker.Mobile.Core;

namespace HabitTracker.Mobile.Tests;

public class JournalTests
{
    [Theory]
    [InlineData(WeightUnit.Pounds, 45, "45lbs+25lbs+10", "", true, 205)]
    [InlineData(WeightUnit.Kilograms, 20, "20+5+1.25", "", true, 72.5)]
    [InlineData(WeightUnit.Pounds, 45, "45+25+10", "45+10", false, 180)]
    public async Task PhysicalPlateSnapshotsSurviveADeviceStoreRestart(WeightUnit unit, decimal bar, string left, string right, bool mirror, decimal total)
    {
        using var directory = new TemporaryDirectory();
        var clock = new ManualClock();
        var load = PlateLoad.Parse(bar, left, right, mirror, unit);
        var journal = new LocalWorkoutJournal(new FileJournalStore(directory.PathName), JournalMode.Local, clock);
        var workout = await journal.CreateAsync(new() { Name = "Phone session", Date = new(2026, 11, 17) });
        await journal.AddSetAsync(workout.Id, new() { Unit = unit, BarWeight = bar, LeftPlates = load.LeftPlates.ToArray(), RightPlates = load.RightPlates.ToArray(), Reps = 5 });
        var restarted = new LocalWorkoutJournal(new FileJournalStore(directory.PathName), JournalMode.Local, clock);
        var recovered = Assert.Single(await restarted.ListAsync());
        Assert.False(recovered.IsCompleted);
        var set = Assert.Single(recovered.Sets);
        Assert.Equal(total, set.TotalWeight);
        Assert.Equal(load.LeftPlates, set.LeftPlates);
        Assert.Equal(load.RightPlates, set.RightPlates);
        Assert.Equal(unit, set.Unit);
    }

    [Fact]
    public async Task DuplicateRequestsAndCompletedMutationsAreRejected()
    {
        var journal = new LocalWorkoutJournal(new MemoryJournalStore(), JournalMode.Local, new ManualClock());
        var workout = await journal.CreateAsync(new() { Name = "Upper", Date = new(2026, 11, 17) });
        await Assert.ThrowsAsync<JournalConflictException>(() => journal.CompleteAsync(workout.Id));
        var request = new LogSetRequest { LeftPlates = [45, 25, 10], RightPlates = [45, 25, 10] };
        var saved = await journal.AddSetAsync(workout.Id, request);
        await Assert.ThrowsAsync<JournalConflictException>(() => journal.AddSetAsync(workout.Id, request));
        Assert.Single((await journal.ListAsync())[0].Sets);
        await journal.CompleteAsync(workout.Id);
        await Assert.ThrowsAsync<JournalConflictException>(() => journal.RemoveSetAsync(workout.Id, saved.Sets[0].Id));
        await Assert.ThrowsAsync<JournalConflictException>(() => journal.AddSetAsync(workout.Id, request with { ClientId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task SeparateJournalInstancesDoNotLoseConcurrentWrites()
    {
        var store = new MemoryJournalStore();
        var writers = Enumerable.Range(0, 4).Select(i => new LocalWorkoutJournal(store, JournalMode.Local, new ManualClock())
            .CreateAsync(new() { Name = "Session " + i, Date = new(2026, 11, 17) }));
        await Task.WhenAll(writers);
        var rows = await new LocalWorkoutJournal(store, JournalMode.Local, new ManualClock()).ListAsync();
        Assert.Equal(4, rows.Count);
        Assert.Equal(4, rows.Select(w => w.Id).Distinct().Count());
        Assert.Equal(4, rows.Select(w => w.Name).Distinct().Count());
    }

    [Fact]
    public async Task DemoNeverSeedsThePersonalJournalAndDoesNotResetExistingDemoData()
    {
        var store = new MemoryJournalStore();
        var demo = new LocalWorkoutJournal(store, JournalMode.Demo, new ManualClock());
        var local = new LocalWorkoutJournal(store, JournalMode.Local, new ManualClock());
        await demo.EnsureDemoAsync(new(2026, 11, 17));
        Assert.Equal(32, (await demo.ListAsync()).Count);
        Assert.Empty(await local.ListAsync());
        await demo.CreateAsync(new() { Name = "Keep my demo edit", Date = new(2026, 11, 17) });
        await demo.EnsureDemoAsync(new(2026, 12, 1));
        Assert.Equal(33, (await demo.ListAsync()).Count);
        Assert.Empty(await local.ListAsync());
    }

    [Theory]
    [InlineData("broken json")]
    [InlineData("{\"schemaVersion\":99}")]
    public async Task UnreadableOrNewerDataIsRetainedInsteadOfOverwritten(string json)
    {
        var store = new MemoryJournalStore();
        await store.TryWriteAsync("journal/local/v1", 0, json);
        var journal = new LocalWorkoutJournal(store, JournalMode.Local, new ManualClock());
        await Assert.ThrowsAsync<InvalidDataException>(() => journal.CreateAsync(new() { Name = "Would overwrite", Date = new(2026, 11, 17) }));
        Assert.Equal(json, (await store.ReadAsync("journal/local/v1"))!.Json);
    }

    [Fact]
    public async Task DeviceStoreEnforcesExpectedVersion()
    {
        using var directory = new TemporaryDirectory();
        var first = new FileJournalStore(directory.PathName);
        var second = new FileJournalStore(directory.PathName);
        Assert.True(await first.TryWriteAsync("same-key", 0, "first"));
        Assert.False(await second.TryWriteAsync("same-key", 0, "stale"));
        Assert.Equal("first", (await second.ReadAsync("same-key"))!.Json);
        Assert.True(await second.TryWriteAsync("same-key", 1, "second"));
        Assert.Equal(2, (await first.ReadAsync("same-key"))!.Version);
    }

    [Fact]
    public async Task DailyHabitLogUpdatesOneDateAndDeletionCascades()
    {
        var journal = new LocalWorkoutJournal(new MemoryJournalStore(), JournalMode.Local, new ManualClock());
        var habit = await journal.SaveHabitAsync(null, new("Walk", "km", null));
        var first = await journal.SaveHabitLogAsync(new(habit.Id, new(2026, 11, 17), 5, null));
        var edited = await journal.SaveHabitLogAsync(new(habit.Id, first.Date, 6, "Updated"));
        Assert.Equal(first.Id, edited.Id);
        Assert.Equal(6, Assert.Single(await journal.ListHabitLogsAsync(habit.Id)).Quantity);
        await journal.DeleteHabitAsync(habit.Id);
        Assert.Empty(await journal.ListHabitLogsAsync(habit.Id));
    }
}
