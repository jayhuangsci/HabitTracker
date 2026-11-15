using System.Text.Json;
using HabitTracker.Api.Entities;
using HabitTracker.Core;
using Microsoft.EntityFrameworkCore;

namespace HabitTracker.Api.Data;

/// <summary>Sample data for an explicitly selected, separate demo database.</summary>
public static class DemoSeeder
{
    public static async Task SeedAsync(HabitTrackerDbContext db, DateOnly today)
    {
        if (await db.Workouts.AnyAsync()) return;
        for (var daysAgo = 55; daysAgo >= 0; daysAgo--)
        {
            var date = today.AddDays(-daysAgo);
            if (date.DayOfWeek is not (DayOfWeek.Monday or DayOfWeek.Wednesday or DayOfWeek.Friday or DayOfWeek.Saturday)) continue;
            var lower = date.DayOfWeek is DayOfWeek.Wednesday or DayOfWeek.Saturday;
            var week = (55 - daysAgo) / 7;
            var workout = new WorkoutSession { Name = lower ? "Lower body" : "Upper body", Date = date,
                Notes = "Demo session · sample training data", StartedAt = date.ToDateTime(new TimeOnly(17, 30), DateTimeKind.Utc),
                CompletedAt = date.ToDateTime(new TimeOnly(18, 20), DateTimeKind.Utc) };
            var ids = lower ? new[] { "back-squat", "romanian-deadlift", "hip-thrust" } : ["bench-press", "barbell-row", "overhead-press"];
            foreach (var (id, index) in ids.Select((id, i) => (id, i)))
            {
                for (var set = 0; set < 4; set++)
                {
                    decimal[] plates = set == 0 ? [] : lower ? [45, 25, 5 + week * 2.5m] : [25, 10 + week * 2.5m];
                    if (!lower && index == 0 && week >= 5 && set > 0) plates = [45, 25, 10];
                    if (index == 2 && set > 0) plates = [10, 5 + week * 1.25m];
                    workout.Sets.Add(new WorkoutSet { ClientId = Guid.NewGuid(), ExerciseId = id,
                        Unit = WeightUnit.Pounds, BarWeight = 45, LeftPlatesJson = JsonSerializer.Serialize(plates),
                        RightPlatesJson = JsonSerializer.Serialize(plates), Reps = set == 0 ? 10 : 8 - set,
                        Rpe = set == 0 ? null : 7 + set * 0.5m, Kind = set == 0 ? SetKind.Warmup : SetKind.Working });
                }
            }
            db.Workouts.Add(workout);
        }
        if (!await db.Habits.AnyAsync())
        {
            foreach (var (name, unit, quantity) in new[] { ("Daily walk", "km", 4m), ("Sleep", "hours", 7.5m), ("Hydration", "liters", 2.5m) })
            {
                var habit = new Habit { Name = name, Unit = unit, Description = "Demo recovery habit", CreatedAt = DateTime.UtcNow };
                for (var day = 0; day < 7; day++) habit.HabitLogs.Add(new HabitLog { Date = today.AddDays(-day), Quantity = quantity, CreatedAt = DateTime.UtcNow });
                db.Habits.Add(habit);
            }
        }
        await db.SaveChangesAsync();
    }
}
