using HabitTracker.Mobile.Core;

namespace HabitTracker.Mobile.Services;

public interface IMobileDevice
{
    IJournalStore Store { get; }
    bool LaunchDemo { get; }
    Task ExportCsvAsync(string fileName, string csv);
}

public static class MobilePlatform
{
    public static IMobileDevice? Device { get; set; }
    public static Action? Resuming { get; set; }
    public static Action? Suspending { get; set; }
    public static Action? GoBack { get; set; }
    public static bool CanGoBack { get; set; }
}
