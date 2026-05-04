using HabitTracker.Api.Data;
using HabitTracker.Api.DTOs;
using HabitTracker.Api.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HabitTracker.Api.Controllers;

[ApiController]
[Route("api/stats")]
public class StatsController : ControllerBase
{
    private readonly HabitTrackerDbContext _dbContext;

    public StatsController(HabitTrackerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("habits/{habitId:int}")]
    public async Task<ActionResult<HabitStatsResponse>> GetHabitStats(
        int habitId,
        [FromQuery] string range = "month")
    {
        var normalizedRange = range.Trim().ToLowerInvariant();

        if (!TryGetDateRange(normalizedRange, out var startDate, out var endDate))
        {
            return BadRequest(new { message = "Range must be week, month, or year." });
        }

        var habit = await _dbContext.Habits
            .AsNoTracking()
            .Where(x => x.Id == habitId)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Unit
            })
            .FirstOrDefaultAsync();

        if (habit is null)
        {
            return NotFound(new { message = "Habit not found." });
        }

        var quantitiesByDate = await _dbContext.Set<HabitLog>()
            .AsNoTracking()
            .Where(x => x.HabitId == habitId && x.Date >= startDate && x.Date <= endDate)
            .Select(x => new
            {
                x.Date,
                x.Quantity
            })
            .ToDictionaryAsync(x => x.Date, x => x.Quantity);

        var points = new List<StatPointResponse>();

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            points.Add(new StatPointResponse
            {
                Date = date,
                Quantity = quantitiesByDate.GetValueOrDefault(date, 0m)
            });
        }

        var response = new HabitStatsResponse
        {
            HabitId = habit.Id,
            HabitName = habit.Name,
            Unit = habit.Unit,
            Range = normalizedRange,
            StartDate = startDate,
            EndDate = endDate,
            Points = points
        };

        return Ok(response);
    }

    private static bool TryGetDateRange(string range, out DateOnly startDate, out DateOnly endDate)
    {
        endDate = DateOnly.FromDateTime(DateTime.Today);

        switch (range)
        {
            case "week":
                startDate = endDate.AddDays(-6);
                return true;

            case "month":
                startDate = endDate.AddDays(-29);
                return true;

            case "year":
                startDate = endDate.AddDays(-364);
                return true;

            default:
                startDate = default;
                endDate = default;
                return false;
        }
    }
}
