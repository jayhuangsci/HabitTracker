using HabitTracker.Core;

namespace HabitTracker.Api.Entities;

public sealed class WorkoutSet
{
    public int Id { get; set; }
    public int WorkoutSessionId { get; set; }
    public WorkoutSession WorkoutSession { get; set; } = null!;
    public Guid ClientId { get; set; }
    public string ExerciseId { get; set; } = "";
    public WeightUnit Unit { get; set; }
    public decimal BarWeight { get; set; }
    public string LeftPlatesJson { get; set; } = "[]";
    public string RightPlatesJson { get; set; } = "[]";
    public int Reps { get; set; }
    public decimal? Rpe { get; set; }
    public SetKind Kind { get; set; }
    public string? Notes { get; set; }
}
