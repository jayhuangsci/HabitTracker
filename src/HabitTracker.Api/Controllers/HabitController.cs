using HabitTracker.Api.Data;
using HabitTracker.Api.DTOs;
using HabitTracker.Api.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Xml.Linq;

namespace HabitTracker.Api.Controllers;

[ApiController]
[Route("api/habits")]

public class HabitController:ControllerBase
{
    private readonly HabitTrackerDbContext _dbContext;

    public HabitController(HabitTrackerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult <List <HabitResponse> > > GetHabits()
    {
        var habits = await _dbContext.Habits
            .OrderBy(x => x.Id)
            .Select(x => new HabitResponse
            {
                Id = x.Id,
                Name = x.Name,
                Unit = x.Unit,
                Description = x.Description,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync();
        return Ok(habits);
    }
    [HttpPost]
    public async Task<ActionResult<HabitResponse>> CreateHabit(CreateHabitRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Name is required" });
        }

        if (string.IsNullOrWhiteSpace(request.Unit))
        {
            return BadRequest(new { message = "Unit is required." });
        }

        var normalizedName = request.Name.Trim();
        var normalizedUnit = request.Unit.Trim();
        var normalizedDescription = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description.Trim();
        var exists = await _dbContext.Habits
            .AnyAsync(x => x.Name == normalizedName);
        if (exists)
        {
            return Conflict(new { message = "Habit name already exists." });
        }

        var habit = new Habit
        {
            Name = normalizedName,
            Unit = normalizedUnit,
            Description = normalizedDescription,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Habits.Add(habit);
        await _dbContext.SaveChangesAsync();

        var response = new HabitResponse
        {
            Id = habit.Id,
            Name = habit.Name,
            Unit = habit.Unit,
            Description = habit.Description,
            CreatedAt = habit.CreatedAt
        };

        return Created($"/api/habits/{habit.Id}", response);
    }
}