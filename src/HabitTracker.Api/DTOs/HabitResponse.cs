namespace HabitTracker.Api.DTOs;

public class HabitResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }
}