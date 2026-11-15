namespace HabitTracker.Api.Entities;

public sealed class WorkoutSession
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateOnly Date { get; set; }
    public string? Notes { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public int Revision { get; set; }
    public List<WorkoutSet> Sets { get; set; } = [];
}
