using HabitTracker.Api.Data;
using HabitTracker.Api.DTOs;
using HabitTracker.Api.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HabitTracker.Api.Controllers;

[ApiController]
[Route("api/habit-logs")]

public class HabitLogsController: ControllerBase
{
    private readonly HabitTrackerDbContext _dbContext;

    public HabitLogsController(HabitTrackerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult <List<HabitLogResponse>>> GetHabitLogs()
    {
        var habitLogs = await _dbContext.Set<HabitLog>()
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Id)
            .Select(x => new HabitLogResponse
            {
                Id = x.Id,
                HabitId = x.HabitId,
                Date = x.Date,
                Quantity = x.Quantity,
                Comment = x.Comment,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync();

        return Ok(habitLogs);
    }
    [HttpPost]
    public async Task<ActionResult<HabitLogResponse>> CreateHabitLog(CreateHabitLogRequest request)
    {
        if (request.HabitId <= 0)
        {
            return BadRequest(new { message = "HabitId must be greater than 0." });
        }
        if (request.Quantity < 0)
        {
            return BadRequest(new { message = "Quantity must be greater than or equal to 0." });
        }

        var habitExists = await _dbContext.Habits
            .AnyAsync(x => x.Id == request.HabitId);

        if (!habitExists)
        {
            return BadRequest(new { message = "Habit does not exist." });
        }

        var duplicateExists = await _dbContext.Set<HabitLog>()
            .AnyAsync(x => x.HabitId == request.HabitId && x.Date == request.Date);

        if (duplicateExists)
        {
            return Conflict(new { message = "A log for this habit and date already exists." });
        }

        var habitLog = new HabitLog
        {
            HabitId = request.HabitId,
            Date = request.Date,
            Quantity = request.Quantity,
            Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Set<HabitLog>().Add(habitLog);
        await _dbContext.SaveChangesAsync();

        var response = new HabitLogResponse
        {
            Id = habitLog.Id,
            HabitId = habitLog.HabitId,
            Date = habitLog.Date,
            Quantity = habitLog.Quantity,
            Comment = habitLog.Comment,
            CreatedAt = habitLog.CreatedAt
        };

        return Created($"/api/habit-logs/{habitLog.Id}", response);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<HabitLogResponse>> UpdateHabitLog(int id, UpdateHabitLogRequest request)
    {
        if (request.HabitId <= 0)
        {
            return BadRequest(new { message = "HabitId must be greater than 0." });
        }

        if (request.Quantity < 0)
        {
            return BadRequest(new { message = "Quantity must be greater than or equal to 0." });
        }

        var habitLog = await _dbContext.Set<HabitLog>().FindAsync(id);

        if (habitLog is null)
        {
            return NotFound(new { message = "Habit log not found." });
        }

        var habitExists = await _dbContext.Habits
            .AnyAsync(x => x.Id == request.HabitId);

        if (!habitExists)
        {
            return BadRequest(new { message = "Habit does not exist." });
        }

        var duplicateExists = await _dbContext.Set<HabitLog>()
            .AnyAsync(x => x.Id != id && x.HabitId == request.HabitId && x.Date == request.Date);

        if (duplicateExists)
        {
            return Conflict(new { message = "A log for this habit and date already exists." });
        }

        habitLog.HabitId = request.HabitId;
        habitLog.Date = request.Date;
        habitLog.Quantity = request.Quantity;
        habitLog.Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();

        await _dbContext.SaveChangesAsync();

        var response = new HabitLogResponse
        {
            Id = habitLog.Id,
            HabitId = habitLog.HabitId,
            Date = habitLog.Date,
            Quantity = habitLog.Quantity,
            Comment = habitLog.Comment,
            CreatedAt = habitLog.CreatedAt
        };

        return Ok(response);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteHabitLog(int id)
    {
        var habitLog = await _dbContext.Set<HabitLog>().FindAsync(id);

        if (habitLog is null)
        {
            return NotFound(new { message = "Habit log not found." });
        }

        _dbContext.Set<HabitLog>().Remove(habitLog);
        await _dbContext.SaveChangesAsync();

        return NoContent();
    }

}

