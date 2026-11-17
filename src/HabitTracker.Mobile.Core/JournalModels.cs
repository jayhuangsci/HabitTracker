using System.Text.Json.Serialization;
using HabitTracker.Core;

namespace HabitTracker.Mobile.Core;

public enum JournalMode { Local, Demo, Connected }
public enum MobilePage { Home, Workout, History, Progress, Settings, Habits }

public sealed record StoredDocument(long Version, string Json);

public sealed record HabitRecord(int Id, string Name, string Unit, string? Description, DateTime CreatedAt)
{
    public override string ToString() => Name;
}
public sealed record HabitLogRecord(int Id, int HabitId, DateOnly Date, decimal Quantity, string? Comment, DateTime CreatedAt);
public sealed record SaveHabitRequest(string Name, string Unit, string? Description);
public sealed record SaveHabitLogRequest(int HabitId, DateOnly Date, decimal Quantity, string? Comment);

public sealed class JournalDocument
{
    public int SchemaVersion { get; set; } = 1;
    public int NextWorkoutId { get; set; } = 1;
    public int NextSetId { get; set; } = 1;
    public int NextHabitId { get; set; } = 1;
    public int NextHabitLogId { get; set; } = 1;
    public List<WorkoutResponse> Workouts { get; set; } = [];
    public List<HabitRecord> Habits { get; set; } = [];
    public List<HabitLogRecord> HabitLogs { get; set; } = [];
}

public sealed record DevicePreferences
{
    public JournalMode Mode { get; init; } = JournalMode.Local;
    public string ApiBaseUrl { get; init; } = "http://localhost:5253/";
    public int WeeklyGoal { get; init; } = 4;
}

public sealed record WorkoutDraft
{
    public string Name { get; init; } = "Upper body";
    public DateOnly Date { get; init; } = DateOnly.FromDateTime(DateTime.Today);
    public string RoutineId { get; init; } = "upper";
    public string ExerciseId { get; init; } = "bench-press";
    public WeightUnit Unit { get; init; }
    public string Bar { get; init; } = "45";
    public string Left { get; init; } = "45 + 25 + 10";
    public string Right { get; init; } = "45 + 25 + 10";
    public bool Mirror { get; init; } = true;
    public string Reps { get; init; } = "5";
    public string Rpe { get; init; } = "8";
    public SetKind Kind { get; init; }
    public string Notes { get; init; } = "";
}

public sealed record WorkspaceSnapshot
{
    public int? ActiveWorkoutId { get; init; }
    public LogSetRequest? PendingSet { get; init; }
    public WorkoutDraft Draft { get; init; } = new();
    public RestTimerSnapshot Timer { get; init; } = new();
}

public sealed record RestTimerSnapshot
{
    public DateTimeOffset? DeadlineUtc { get; init; }
    public int PausedSeconds { get; init; } = 90;
}

public sealed class JournalConflictException(string message) : InvalidOperationException(message);
public sealed class JournalNotFoundException(string message) : InvalidOperationException(message);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(StoredDocument))]
[JsonSerializable(typeof(JournalDocument))]
[JsonSerializable(typeof(DevicePreferences))]
[JsonSerializable(typeof(WorkspaceSnapshot))]
[JsonSerializable(typeof(WorkoutResponse))]
[JsonSerializable(typeof(WorkoutResponse[]))]
[JsonSerializable(typeof(CreateWorkoutRequest))]
[JsonSerializable(typeof(LogSetRequest))]
[JsonSerializable(typeof(HabitRecord))]
[JsonSerializable(typeof(HabitRecord[]))]
[JsonSerializable(typeof(HabitLogRecord))]
[JsonSerializable(typeof(HabitLogRecord[]))]
[JsonSerializable(typeof(SaveHabitRequest))]
[JsonSerializable(typeof(SaveHabitLogRequest))]
public partial class JournalJsonContext : JsonSerializerContext;
