using System.Security.Cryptography;
using System.Text;

namespace HabitTracker.Mobile.Core;

public interface IJournalFactory
{
    Task<JournalHandle> OpenAsync(JournalMode mode, string apiAddress, CancellationToken ct = default);
}

public sealed class JournalHandle(JournalMode mode, string workspaceKey, IWorkoutJournal journal, IDisposable? connection = null) : IDisposable
{
    public JournalMode Mode { get; } = mode;
    public string WorkspaceKey { get; } = workspaceKey;
    public IWorkoutJournal Journal { get; } = journal;
    public void Dispose() => connection?.Dispose();
}

public sealed class JournalFactory(IJournalStore store, TimeProvider clock, Func<Uri, HttpClient>? createClient = null) : IJournalFactory
{
    public async Task<JournalHandle> OpenAsync(JournalMode mode, string apiAddress, CancellationToken ct = default)
    {
        if (mode == JournalMode.Connected)
        {
            var address = ApiWorkoutJournal.NormalizeAddress(apiAddress);
            var client = createClient?.Invoke(address) ?? new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(12) };
            try
            {
                var journal = new ApiWorkoutJournal(client);
                await journal.PingAsync(ct);
                return new(mode, "workspace/api/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(address.AbsoluteUri))), journal, client);
            }
            catch { client.Dispose(); throw; }
        }
        var local = new LocalWorkoutJournal(store, mode, clock);
        if (mode == JournalMode.Demo) await local.EnsureDemoAsync(DateOnly.FromDateTime(clock.GetLocalNow().DateTime), ct);
        return new(mode, "workspace/" + mode.ToString().ToLowerInvariant(), local);
    }
}
