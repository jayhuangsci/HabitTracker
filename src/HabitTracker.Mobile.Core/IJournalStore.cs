using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace HabitTracker.Mobile.Core;

/// <summary>Durable versioned documents; compare/exchange prevents lost writes across tabs or processes.</summary>
public interface IJournalStore
{
    Task<StoredDocument?> ReadAsync(string key, CancellationToken ct = default);
    Task<bool> TryWriteAsync(string key, long expectedVersion, string json, CancellationToken ct = default);
}

public sealed class JsonStateStore(IJournalStore store)
{
    public async Task<T?> ReadAsync<T>(string key, JsonTypeInfo<T> type, CancellationToken ct = default) where T : class
    {
        var saved = await store.ReadAsync(key, ct);
        if (saved == null) return null;
        try { return JsonSerializer.Deserialize(saved.Json, type) ?? throw new JsonException("Empty document."); }
        catch (JsonException ex) { throw new InvalidDataException("Saved journal data could not be read. It has been retained; no reset was performed.", ex); }
    }

    public async Task SaveAsync<T>(string key, T value, JsonTypeInfo<T> type, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(value, type);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var existing = await store.ReadAsync(key, ct);
            if (await store.TryWriteAsync(key, existing?.Version ?? 0, json, ct)) return;
        }
        throw new JournalConflictException("This journal changed in another window. Refresh and try again.");
    }
}
