namespace HabitTracker.Mobile.Core;

/// <summary>Deadlines survive suspension; paused timers retain an exact number of seconds.</summary>
public sealed class RestTimer(TimeProvider clock)
{
    public RestTimerSnapshot Snapshot { get; private set; } = new();
    public bool IsRunning => Snapshot.DeadlineUtc.HasValue && RemainingSeconds > 0;
    public int RemainingSeconds => Snapshot.DeadlineUtc is { } deadline
        ? Math.Max(0, (int)Math.Ceiling(Math.Min(3600, (deadline - clock.GetUtcNow()).TotalSeconds)))
        : Math.Clamp(Snapshot.PausedSeconds, 0, 3600);

    public void Restore(RestTimerSnapshot snapshot) => Snapshot = snapshot;
    public void Start(int seconds = 90) => Snapshot = new() { DeadlineUtc = clock.GetUtcNow().AddSeconds(Math.Clamp(seconds, 1, 3600)), PausedSeconds = 0 };
    public void Pause() => Snapshot = new() { PausedSeconds = RemainingSeconds };
    public void Resume() => Start(RemainingSeconds == 0 ? 90 : RemainingSeconds);
    public void Reset() => Snapshot = new();
    public void AddSeconds(int seconds = 15)
    {
        var remaining = Math.Clamp(RemainingSeconds + seconds, 0, 3600);
        if (IsRunning) Start(remaining);
        else Snapshot = new() { PausedSeconds = remaining };
    }
    public bool SettleExpired()
    {
        if (Snapshot.DeadlineUtc.HasValue && RemainingSeconds == 0) { Snapshot = new() { PausedSeconds = 0 }; return true; }
        return false;
    }
}
