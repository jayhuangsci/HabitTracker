using System.IO;
using System.Text.Json;

namespace HabitTracker.Services;

public static class PreferenceStore
{
    private static readonly string PathName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HabitTracker", "preferences.json");
    public static int ReadGoal()
    {
        try
        {
            if (File.Exists(PathName))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(PathName));
                var value = doc.RootElement.GetProperty("weeklyGoal").GetInt32();
                if (value is >= 1 and <= 7) return value;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException) { }
        return 4;
    }
    public static void SaveGoal(int value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        File.WriteAllText(PathName, JsonSerializer.Serialize(new { weeklyGoal = value }));
    }
}
