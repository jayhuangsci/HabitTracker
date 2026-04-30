namespace HabitTracker.Api.DTOs;
public class CreateHabitRequest
{
    public string Name { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public string? Description { get; set; }

}