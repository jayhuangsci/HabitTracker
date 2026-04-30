namespace HabitTracker.Api.DTOs;

public class HabitLogResponse
{
    public int Id { get; set; }

    public int HabitId { get; set; }

    public DateOnly Date { get; set; }

    public decimal Quantity { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; }
}
