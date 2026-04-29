namespace HabitTracker.Api.Entities;

public class Habit
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<HabitLog> HabitLogs { get; set; } = new List<HabitLog>();

}