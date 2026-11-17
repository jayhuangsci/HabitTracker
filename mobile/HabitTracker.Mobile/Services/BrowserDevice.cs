using System.Text.Json;
using HabitTracker.Mobile.Core;
using OpenSilver;

namespace HabitTracker.Mobile.Services;

public sealed class BrowserDevice : IMobileDevice
{
    public IJournalStore Store { get; } = new IndexedDbJournalStore();
    public bool LaunchDemo => Convert.ToBoolean(Interop.ExecuteJavaScript("window.formDevice.launchDemo()"));
    public Task ExportCsvAsync(string fileName, string csv)
    {
        Interop.ExecuteJavaScript("window.formDevice.exportCsv($0, $1)", fileName, csv);
        return Task.CompletedTask;
    }
}

public sealed class IndexedDbJournalStore : IJournalStore
{
    public async Task<StoredDocument?> ReadAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var success = Interop.CreateJavascriptCallback((Action<string>)(value => completion.TrySetResult(value)));
        using var failure = Interop.CreateJavascriptCallback((Action<string>)(message => completion.TrySetException(new IOException(message))));
        Interop.ExecuteJavaScript("window.formDevice.read($0, $1, $2)", key, success, failure);
        var result = await completion.Task;
        try { return JsonSerializer.Deserialize(result, JournalJsonContext.Default.StoredDocument); }
        catch (JsonException ex) { throw new InvalidDataException("The saved browser journal is unreadable. It has been kept unchanged.", ex); }
    }

    public async Task<bool> TryWriteAsync(string key, long expectedVersion, string json, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var success = Interop.CreateJavascriptCallback((Action<string>)(value => completion.TrySetResult(value == "true")));
        using var failure = Interop.CreateJavascriptCallback((Action<string>)(message => completion.TrySetException(new IOException(message))));
        Interop.ExecuteJavaScript("window.formDevice.write($0, $1, $2, $3, $4)", key, expectedVersion, json, success, failure);
        return await completion.Task;
    }
}
