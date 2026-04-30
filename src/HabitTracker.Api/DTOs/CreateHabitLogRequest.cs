namespace HabitTracker.Api.DTOs;

public class CreateHabitLogRequest
{
    public int HabitId { get; set; }

    public DateOnly Date { get; set; }

    public decimal Quantity { get; set; }

    public string? Comment { get; set; }
}
