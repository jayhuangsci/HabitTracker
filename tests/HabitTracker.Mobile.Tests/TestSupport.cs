using HabitTracker.Mobile.Core;

namespace HabitTracker.Mobile.Tests;

internal sealed class MemoryJournalStore : IJournalStore
{
    private readonly Dictionary<string, StoredDocument> _values = [];
    public async Task<StoredDocument?> ReadAsync(string key, CancellationToken ct = default)
    {
        await Task.Yield();
        lock (_values) return _values.GetValueOrDefault(key);
    }
    public Task<bool> TryWriteAsync(string key, long expectedVersion, string json, CancellationToken ct = default)
    {
        lock (_values)
        {
            if ((_values.GetValueOrDefault(key)?.Version ?? 0) != expectedVersion) return Task.FromResult(false);
            _values[key] = new(expectedVersion + 1, json);
            return Task.FromResult(true);
        }
    }
}

internal sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 17, 18, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan amount) => _now += amount;
}

internal sealed class TemporaryDirectory : IDisposable
{
    public string PathName { get; } = Path.Combine(Path.GetTempPath(), "form-mobile-" + Guid.NewGuid().ToString("N"));
    public TemporaryDirectory() => Directory.CreateDirectory(PathName);
    public void Dispose()
    {
        var full = Path.GetFullPath(PathName);
        var expectedPrefix = Path.Combine(Path.GetTempPath(), "form-mobile-");
        if (!full.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected test cleanup path.");
        Directory.Delete(full, recursive: true);
    }
}
