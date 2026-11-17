using HabitTracker.Mobile.Core;

namespace HabitTracker.Mobile.Tests;

public class RestTimerTests
{
    [Fact]
    public async Task RunningDeadlineRecoversAfterSuspensionAndRestart()
    {
        var clock = new ManualClock();
        var timer = new RestTimer(clock);
        timer.Start();
        clock.Advance(TimeSpan.FromSeconds(35));
        var store = new JsonStateStore(new MemoryJournalStore());
        await store.SaveAsync("workspace", new WorkspaceSnapshot { Timer = timer.Snapshot }, JournalJsonContext.Default.WorkspaceSnapshot);
        clock.Advance(TimeSpan.FromSeconds(20));
        var restarted = new RestTimer(clock);
        restarted.Restore((await store.ReadAsync("workspace", JournalJsonContext.Default.WorkspaceSnapshot))!.Timer);
        Assert.Equal(35, restarted.RemainingSeconds);
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(0, restarted.RemainingSeconds);
        Assert.True(restarted.SettleExpired());
        Assert.False(restarted.SettleExpired());
    }

    [Fact]
    public void PausedTimeIsStableAndResumeAndExtensionUseCurrentTime()
    {
        var clock = new ManualClock();
        var timer = new RestTimer(clock);
        timer.Start();
        clock.Advance(TimeSpan.FromSeconds(30));
        timer.Pause();
        var restored = new RestTimer(clock);
        restored.Restore(timer.Snapshot);
        clock.Advance(TimeSpan.FromHours(3));
        Assert.Equal(60, restored.RemainingSeconds);
        restored.AddSeconds();
        Assert.Equal(75, restored.RemainingSeconds);
        restored.Resume();
        clock.Advance(TimeSpan.FromSeconds(16));
        Assert.Equal(59, restored.RemainingSeconds);
        restored.Reset();
        Assert.Equal(90, restored.RemainingSeconds);
        Assert.False(restored.IsRunning);
    }
}
