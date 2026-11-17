using HabitTracker.Mobile.Core;
using HabitTracker.Core;

namespace HabitTracker.Mobile.Tests;

public class WorkspaceTests
{
    private static MobileWorkspace Open(IJournalStore store, ManualClock clock, IJournalFactory? factory = null) =>
        new(factory ?? new JournalFactory(store, clock), store, clock, (_, _) => Task.CompletedTask);

    [Fact]
    public async Task WorkoutCommandsRecoverCompleteAndReuseTheExactSetup()
    {
        var store = new MemoryJournalStore(); var clock = new ManualClock();
        using (var first = Open(store, clock))
        {
            await first.InitializeAsync();
            first.Left = "45lbs+25lbs+10"; first.SetNotes = "Steady tempo";
            await first.LogSetCommand.ExecuteAsync();
            Assert.False(first.HasError, first.Status);
            Assert.Equal(205, Assert.Single(first.SessionSets).Set.TotalWeight);
            await first.FlushAsync();
        }
        clock.Advance(TimeSpan.FromSeconds(30));
        using var restored = Open(store, clock);
        await restored.InitializeAsync();
        Assert.Equal("01:00", restored.TimerLabel);
        Assert.True(restored.HasActiveWorkout);
        restored.Mirror = false; restored.Right = "45+10";
        Assert.Equal("180", restored.TotalLoad);
        await restored.LogSetCommand.ExecuteAsync();
        await restored.FinishCommand.ExecuteAsync();
        Assert.False(restored.HasError, restored.Status);
        Assert.Equal("1", restored.Sessions28);
        Assert.Equal("1.9k", restored.Volume28);
        Assert.False(restored.HasActiveWorkout);
        restored.Search = "bench";
        var session = Assert.Single(restored.History);
        restored.Reuse(session.Sets[0].Set);
        Assert.True(restored.IsWorkout);
        Assert.Equal("205", restored.TotalLoad);
        Assert.Equal("45 + 25 + 10", restored.Left);
        Assert.Equal("Steady tempo", restored.SetNotes);
        Assert.True(restored.Mirror);
        await restored.FlushAsync();
    }

    [Fact]
    public async Task MirroredExpressionsStayEqualWhenSwitchingToIndependentSides()
    {
        using var workspace = Open(new MemoryJournalStore(), new ManualClock());
        await workspace.InitializeAsync();
        workspace.Left = "45+10";
        Assert.Equal("45+10", workspace.Right);
        Assert.Equal("155", workspace.TotalLoad);
        workspace.Mirror = false;
        workspace.Left = "45+25+10";
        Assert.Equal("45+10", workspace.Right);
        Assert.Equal("180", workspace.TotalLoad);
        workspace.Mirror = true;
        Assert.Equal(workspace.Left, workspace.Right);
        Assert.Equal("205", workspace.TotalLoad);
    }

    [Fact]
    public async Task ChangingJournalModeKeepsPersonalDataAndTimerSeparate()
    {
        var store = new MemoryJournalStore(); var clock = new ManualClock();
        using var workspace = Open(store, clock);
        await workspace.InitializeAsync();
        await workspace.LogSetCommand.ExecuteAsync();
        workspace.SelectedMode = JournalMode.Demo;
        await workspace.ApplySettingsCommand.ExecuteAsync();
        Assert.True(workspace.IsDemo);
        Assert.False(workspace.HasActiveWorkout);
        Assert.Equal(3, workspace.RecentWorkouts.Count);
        workspace.SelectedMode = JournalMode.Local;
        await workspace.ApplySettingsCommand.ExecuteAsync();
        Assert.False(workspace.IsDemo);
        Assert.Equal(205, Assert.Single(workspace.SessionSets).Set.TotalWeight);
        Assert.Equal("LOCAL JOURNAL", workspace.SourceLabel);
        Assert.Equal("01:30", workspace.TimerLabel);
    }

    [Fact]
    public async Task FailedConnectionKeepsThePreviouslyOpenedJournal()
    {
        var store = new MemoryJournalStore(); var clock = new ManualClock();
        using var workspace = Open(store, clock, new FailingConnectedFactory(new JournalFactory(store, clock)));
        await workspace.InitializeAsync();
        await workspace.LogSetCommand.ExecuteAsync();
        workspace.SelectedMode = JournalMode.Connected;
        workspace.ApiAddress = "https://unreachable.example/";
        await workspace.ApplySettingsCommand.ExecuteAsync();
        Assert.True(workspace.HasError);
        Assert.Equal("LOCAL JOURNAL", workspace.SourceLabel);
        Assert.Single(workspace.SessionSets);
        Assert.True(workspace.HasActiveWorkout);
    }

    [Fact]
    public async Task FailedModePreferenceWriteKeepsTheVisibleJournalAndItsAdapterTogether()
    {
        var store = new FailingPreferencesStore(new MemoryJournalStore());
        using var workspace = Open(store, new ManualClock());
        await workspace.InitializeAsync();
        await workspace.LogSetCommand.ExecuteAsync();
        store.FailWrites = true;
        workspace.SelectedMode = JournalMode.Demo;
        await workspace.ApplySettingsCommand.ExecuteAsync();
        Assert.True(workspace.HasError);
        Assert.Equal("LOCAL JOURNAL", workspace.SourceLabel);
        Assert.Equal(205, Assert.Single(workspace.SessionSets).Set.TotalWeight);
        store.FailWrites = false;
        await workspace.RefreshCommand.ExecuteAsync();
        Assert.False(workspace.HasError, workspace.Status);
        Assert.Equal("LOCAL JOURNAL", workspace.SourceLabel);
        Assert.Equal(205, Assert.Single(workspace.SessionSets).Set.TotalWeight);
    }

    [Fact]
    public async Task AnAmbiguousCommittedSaveIsRecoveredWithoutAddingADuplicate()
    {
        var store = new CommitThenThrowStore(new MemoryJournalStore()); var clock = new ManualClock();
        using (var first = Open(store, clock))
        {
            await first.InitializeAsync();
            store.FailAfterNextSetCommit = true;
            await first.LogSetCommand.ExecuteAsync();
            Assert.True(first.HasError);
            Assert.True(first.HasPendingSave);
            Assert.False(first.CanEditDraft);
            await first.FlushAsync();
        }
        using var restored = Open(store, clock);
        await restored.InitializeAsync();
        Assert.False(restored.HasError, restored.Status);
        Assert.False(restored.HasPendingSave);
        Assert.Equal(205, Assert.Single(restored.SessionSets).Set.TotalWeight);
    }

    private sealed class FailingPreferencesStore(IJournalStore inner) : IJournalStore
    {
        public bool FailWrites { get; set; }
        public Task<StoredDocument?> ReadAsync(string key, CancellationToken ct = default) => inner.ReadAsync(key, ct);
        public Task<bool> TryWriteAsync(string key, long expectedVersion, string json, CancellationToken ct = default) =>
            FailWrites && key == "device/preferences/v1" ? throw new IOException("Simulated full device storage.")
                : inner.TryWriteAsync(key, expectedVersion, json, ct);
    }

    private sealed class FailingConnectedFactory(IJournalFactory inner) : IJournalFactory
    {
        public Task<JournalHandle> OpenAsync(JournalMode mode, string apiAddress, CancellationToken ct = default) =>
            mode == JournalMode.Connected ? throw new HttpRequestException("Simulated unavailable service.") : inner.OpenAsync(mode, apiAddress, ct);
    }
    private sealed class CommitThenThrowStore(IJournalStore inner) : IJournalStore
    {
        public bool FailAfterNextSetCommit { get; set; }
        public Task<StoredDocument?> ReadAsync(string key, CancellationToken ct = default) => inner.ReadAsync(key, ct);
        public async Task<bool> TryWriteAsync(string key, long expectedVersion, string json, CancellationToken ct = default)
        {
            var written = await inner.TryWriteAsync(key, expectedVersion, json, ct);
            if (written && FailAfterNextSetCommit && key == "journal/local/v1" && json.Contains("\"clientId\"", StringComparison.Ordinal))
            { FailAfterNextSetCommit = false; throw new IOException("Simulated uncertain device write."); }
            return written;
        }
    }
}
