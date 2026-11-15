using System.ComponentModel.DataAnnotations;

namespace HabitTracker.Core;

public sealed record Exercise(string Id, string Name, string Muscle, string Equipment);
public sealed record Routine(string Id, string Name, string Description, string[] ExerciseIds);

public static class TrainingCatalog
{
    public static IReadOnlyList<Exercise> Exercises { get; } = Array.AsReadOnly<Exercise>([
        new("bench-press", "Barbell bench press", "Chest", "Barbell"),
        new("back-squat", "Barbell back squat", "Legs", "Barbell"),
        new("deadlift", "Conventional deadlift", "Back", "Barbell"),
        new("overhead-press", "Standing overhead press", "Shoulders", "Barbell"),
        new("barbell-row", "Bent-over barbell row", "Back", "Barbell"),
        new("romanian-deadlift", "Romanian deadlift", "Legs", "Barbell"),
        new("incline-press", "Incline barbell press", "Chest", "Barbell"),
        new("front-squat", "Barbell front squat", "Legs", "Barbell"),
        new("barbell-curl", "Barbell curl", "Arms", "Barbell"),
        new("hip-thrust", "Barbell hip thrust", "Glutes", "Barbell"),
        new("close-grip-bench", "Close-grip bench press", "Arms", "Barbell"),
        new("pendlay-row", "Pendlay row", "Back", "Barbell")
    ]);

    public static IReadOnlyList<Routine> Routines { get; } = Array.AsReadOnly<Routine>([
        new("upper", "Upper body", "Press, pull, and build a stronger foundation.", ["bench-press", "barbell-row", "overhead-press", "barbell-curl"]),
        new("lower", "Lower body", "Squat, hinge, and put in the groundwork.", ["back-squat", "romanian-deadlift", "hip-thrust"]),
        new("full", "Full body", "The big lifts. One focused session.", ["back-squat", "bench-press", "deadlift"])
    ]);

    public static Exercise FindExercise(string? id) => Exercises.FirstOrDefault(x => x.Id == id)
        ?? throw new ArgumentException("Choose an exercise from the library.");
}

public sealed record CreateWorkoutRequest
{
    [Required, StringLength(100, MinimumLength = 1)] public string Name { get; init; } = "Strength session";
    public DateOnly Date { get; init; } = DateOnly.FromDateTime(DateTime.Today);
    [StringLength(1000)] public string? Notes { get; init; }
}

public sealed record LogSetRequest
{
    public Guid ClientId { get; init; } = Guid.NewGuid();
    [Required] public string ExerciseId { get; init; } = "bench-press";
    public WeightUnit Unit { get; init; }
    public decimal BarWeight { get; init; } = 45;
    public decimal[] LeftPlates { get; init; } = [];
    public decimal[] RightPlates { get; init; } = [];
    [Range(1, 100)] public int Reps { get; init; } = 5;
    [Range(1, 10)] public decimal? Rpe { get; init; }
    public SetKind Kind { get; init; }
    [StringLength(500)] public string? Notes { get; init; }

    public PlateLoad Validate()
    {
        TrainingCatalog.FindExercise(ExerciseId);
        if (ClientId == Guid.Empty) throw new ArgumentException("A client set ID is required.");
        if (Reps is < 1 or > 100) throw new ArgumentException("Reps must be between 1 and 100.");
        if (Rpe is < 1 or > 10) throw new ArgumentException("RPE must be between 1 and 10, or blank.");
        if (!Enum.IsDefined(Kind)) throw new ArgumentException("Choose working or warmup.");
        if (Notes?.Length > 500) throw new ArgumentException("Set notes must not exceed 500 characters.");
        return new(BarWeight, LeftPlates, RightPlates, Unit);
    }
}

public sealed record WorkoutSetResponse(
    int Id, Guid ClientId, string ExerciseId, string ExerciseName, string Muscle,
    WeightUnit Unit, decimal BarWeight, decimal[] LeftPlates, decimal[] RightPlates,
    decimal TotalWeight, int Reps, decimal? Rpe, SetKind Kind, string? Notes)
{
    public string UnitSymbol => Unit == WeightUnit.Pounds ? "lb" : "kg";
    public string LoadLabel => $"{PlateLoad.Format(TotalWeight)} {UnitSymbol}";
    public string PlateLabel => $"L: {PlateLoad.FormatStack(LeftPlates)}  /  R: {PlateLoad.FormatStack(RightPlates)}";
    public decimal VolumePounds => PlateLoad.Convert(TotalWeight * Reps, Unit, WeightUnit.Pounds);
    public decimal EstimatedOneRepMaxPounds => PlateLoad.Convert(TrainingAnalytics.EstimateOneRepMax(TotalWeight, Reps), Unit, WeightUnit.Pounds);
}

public sealed record WorkoutResponse(int Id, string Name, DateOnly Date, string? Notes,
    DateTime StartedAt, DateTime? CompletedAt, IReadOnlyList<WorkoutSetResponse> Sets)
{
    public bool IsCompleted => CompletedAt.HasValue;
    public int SetCount => Sets.Count;
    public decimal VolumePounds => Sets.Where(s => s.Kind == SetKind.Working).Sum(s => s.VolumePounds);
    public string StatusLabel => IsCompleted ? "Completed" : "In progress";
    public string VolumeLabel => $"{VolumePounds:N0} lb";
    public string DateLabel => Date.ToString("MMM dd, yyyy");
}
