using System.Text;
using HabitTracker.Mobile.Core;
using HabitTracker.Mobile.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;

namespace HabitTracker.Mobile.MauiHybrid;

public sealed class MauiDevice : IMobileDevice
{
    public IJournalStore Store { get; } = new FileJournalStore(Path.Combine(FileSystem.AppDataDirectory, "journals"));
    public bool LaunchDemo => false;
    public Task ExportCsvAsync(string fileName, string csv) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        var path = Path.Combine(FileSystem.CacheDirectory, fileName);
        await File.WriteAllTextAsync(path, csv, new UTF8Encoding(true));
        await Share.Default.RequestAsync(new ShareFileRequest { Title = "Export FORM workout journal", File = new ShareFile(path, "text/csv") });
    });
}
