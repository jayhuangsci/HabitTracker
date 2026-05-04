namespace HabitTracker.Api.DTOs;

public class HabitStatsResponse
{
    public int HabitId { get; set; }

    public string HabitName { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public string Range { get; set; } = string.Empty;

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public List<StatPointResponse> Points { get; set; } = [];
}
