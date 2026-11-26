using System.Text.Json;
using PromptCrew.Core;

namespace PromptCrew.Desktop;

public sealed record OutputDraft(string Output, string Artifacts, string Note, RoundState Status);
public sealed class EditorDraft
{
    public Dictionary<Guid, OutputDraft> Outputs { get; set; } = [];
    public Guid? EmployeeId { get; set; }
    public Guid? ParentId { get; set; }
    public Guid? RevisesId { get; set; }
    public string Input { get; set; } = "";
    public string Model { get; set; } = "";
    public string Tags { get; set; } = "";
    public string References { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Output { get; set; } = "";
    public string Artifacts { get; set; } = "";
    public string ResultNote { get; set; } = "";
    public RoundState ResultStatus { get; set; } = RoundState.Completed;
    public Guid? ResultRoundId { get; set; }
    public RoundInput? PendingRound { get; set; }
    public ResultInput? PendingResult { get; set; }
    public Guid? PendingResultRoundId { get; set; }
    public string NewTaskName { get; set; } = "";
    public Guid? NewTaskId { get; set; }
    public bool RoundAttempted { get; set; } = true;
    public bool RoundRejected { get; set; }
    public bool ResultAttempted { get; set; } = true;
    public bool ResultRejected { get; set; }
    public bool NewTaskAttempted { get; set; } = true;
    public bool NewTaskRejected { get; set; }
}
public sealed class DesktopSettings
{
    public string ApiUrl { get; set; } = "http://127.0.0.1:5283/";
    public string KeyFile { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PromptCrew", "personal", "admin.key");
    public Guid? ProjectId { get; set; }
    public Guid? ChainId { get; set; }
    public Guid? EmployeeId { get; set; }
    public int TimeoutSeconds { get; set; } = 20;
    public bool ReduceMotion { get; set; }
    public Dictionary<string, EditorDraft> Drafts { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore] public string Warning { get; internal set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string FilePath { get; set; } =
        Environment.GetEnvironmentVariable("PROMPTCREW_SETTINGS_FILE") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PromptCrew", "desktop-settings.json");
    public static DesktopSettings Load(string? filePath = null)
    {
        var defaults = new DesktopSettings();
        if (filePath != null) defaults.FilePath = filePath;
        try
        {
            if (File.Exists(defaults.FilePath))
            {
                var loaded = JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(defaults.FilePath), CrewJson.Options);
                if (loaded == null || loaded.Drafts == null) throw new JsonException();
                loaded.TimeoutSeconds = Math.Clamp(loaded.TimeoutSeconds, 1, 120);
                loaded.FilePath = defaults.FilePath; return loaded;
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            defaults.Warning = "原草稿设置无法读取，文件已保留。本次恢复将使用新的设置文件。";
            defaults.FilePath += ".recovered-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".json";
        }
        return defaults;
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(this, CrewJson.Options)); File.Move(temp, FilePath, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
