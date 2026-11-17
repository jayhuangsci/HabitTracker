using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HabitTracker.Mobile.Core;

/// <summary>Atomic device storage with a file lease and version check, independent of UI frameworks.</summary>
public sealed class FileJournalStore(string directory) : IJournalStore
{
    private readonly string _directory = Path.GetFullPath(directory);

    private string FileName(string key) => Path.Combine(_directory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json");

    public async Task<StoredDocument?> ReadAsync(string key, CancellationToken ct = default)
    {
        var path = FileName(key);
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path, ct);
        try { return JsonSerializer.Deserialize(json, JournalJsonContext.Default.StoredDocument) ?? throw new JsonException("Empty document."); }
        catch (JsonException ex) { throw new InvalidDataException("The saved journal file is unreadable. It has been kept unchanged.", ex); }
    }

    public async Task<bool> TryWriteAsync(string key, long expectedVersion, string json, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_directory);
        await using var lease = await AcquireLeaseAsync(FileName(key) + ".lock", ct);
        var current = await ReadAsync(key, ct);
        if ((current?.Version ?? 0) != expectedVersion) return false;
        var next = JsonSerializer.Serialize(new StoredDocument(expectedVersion + 1, json), JournalJsonContext.Default.StoredDocument);
        var temporary = FileName(key) + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, next, Encoding.UTF8, ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, FileName(key), overwrite: true);
            return true;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<FileStream> AcquireLeaseAsync(string path, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 100) { await Task.Delay(20, ct); }
        }
    }
}
