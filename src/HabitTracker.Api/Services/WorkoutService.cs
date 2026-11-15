using System.Text.Json;
using HabitTracker.Api.Data;
using HabitTracker.Api.Entities;
using HabitTracker.Core;
using Microsoft.EntityFrameworkCore;

namespace HabitTracker.Api.Services;

public sealed class ResourceNotFoundException(string message) : Exception(message);
public sealed class WorkoutConflictException(string message) : Exception(message);

public sealed class WorkoutService(HabitTrackerDbContext db, TimeProvider clock)
{
    public async Task<IReadOnlyList<WorkoutResponse>> ListAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (from > to) throw new ArgumentException("The start date must not follow the end date.");
        var rows = await db.Workouts.AsNoTracking().Include(w => w.Sets)
            .Where(w => (!from.HasValue || w.Date >= from) && (!to.HasValue || w.Date <= to))
            .OrderByDescending(w => w.Date).ThenByDescending(w => w.Id).ToListAsync(ct);
        return rows.Select(Map).ToArray();
    }

    public async Task<WorkoutResponse> GetAsync(int id, CancellationToken ct) => Map(await FindAsync(id, ct));

    public async Task<WorkoutResponse> CreateAsync(CreateWorkoutRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100)
            throw new ArgumentException("Give your session a name of at most 100 characters.");
        if (request.Date == default) throw new ArgumentException("Choose a workout date.");
        if (request.Notes?.Length > 1000) throw new ArgumentException("Session notes must not exceed 1,000 characters.");
        var workout = new WorkoutSession { Name = request.Name.Trim(), Date = request.Date,
            Notes = request.Notes?.Trim(), StartedAt = clock.GetUtcNow().UtcDateTime };
        db.Workouts.Add(workout);
        await db.SaveChangesAsync(ct);
        return Map(workout);
    }

    public async Task<WorkoutResponse> AddSetAsync(int id, LogSetRequest request, CancellationToken ct)
    {
        request.Validate();
        var workout = await FindAsync(id, ct);
        RequireOpen(workout);
        if (await db.WorkoutSets.AnyAsync(s => s.ClientId == request.ClientId, ct))
            throw new WorkoutConflictException("This set was already saved. Refresh before retrying.");
        workout.Sets.Add(new WorkoutSet { ClientId = request.ClientId, ExerciseId = request.ExerciseId,
            Unit = request.Unit, BarWeight = request.BarWeight, LeftPlatesJson = JsonSerializer.Serialize(request.LeftPlates),
            RightPlatesJson = JsonSerializer.Serialize(request.RightPlates), Reps = request.Reps,
            Rpe = request.Rpe, Kind = request.Kind, Notes = request.Notes?.Trim() });
        workout.Revision++;
        await db.SaveChangesAsync(ct);
        return Map(workout);
    }

    public async Task<WorkoutResponse> DeleteSetAsync(int id, int setId, CancellationToken ct)
    {
        var workout = await FindAsync(id, ct);
        RequireOpen(workout);
        var set = workout.Sets.SingleOrDefault(s => s.Id == setId)
            ?? throw new ResourceNotFoundException("Set not found in this session.");
        workout.Sets.Remove(set);
        db.WorkoutSets.Remove(set);
        workout.Revision++;
        await db.SaveChangesAsync(ct);
        return Map(workout);
    }

    public async Task<WorkoutResponse> CompleteAsync(int id, CancellationToken ct)
    {
        var workout = await FindAsync(id, ct);
        if (workout.CompletedAt.HasValue) return Map(workout);
        if (workout.Sets.Count == 0) throw new WorkoutConflictException("Log at least one set before finishing.");
        workout.CompletedAt = clock.GetUtcNow().UtcDateTime;
        workout.Revision++;
        await db.SaveChangesAsync(ct);
        return Map(workout);
    }

    private async Task<WorkoutSession> FindAsync(int id, CancellationToken ct) =>
        await db.Workouts.Include(w => w.Sets).SingleOrDefaultAsync(w => w.Id == id, ct)
        ?? throw new ResourceNotFoundException("Workout not found.");

    private static void RequireOpen(WorkoutSession workout)
    {
        if (workout.CompletedAt.HasValue) throw new WorkoutConflictException("This workout is completed. Start a new session to log more sets.");
    }

    public static WorkoutResponse Map(WorkoutSession row) => new(row.Id, row.Name, row.Date, row.Notes,
        DateTime.SpecifyKind(row.StartedAt, DateTimeKind.Utc), row.CompletedAt is { } completed ? DateTime.SpecifyKind(completed, DateTimeKind.Utc) : null,
        row.Sets.OrderBy(s => s.Id).Select(s =>
        {
            var exercise = TrainingCatalog.FindExercise(s.ExerciseId);
            var left = JsonSerializer.Deserialize<decimal[]>(s.LeftPlatesJson)!;
            var right = JsonSerializer.Deserialize<decimal[]>(s.RightPlatesJson)!;
            var load = new PlateLoad(s.BarWeight, left, right, s.Unit);
            return new WorkoutSetResponse(s.Id, s.ClientId, s.ExerciseId, exercise.Name, exercise.Muscle,
                s.Unit, s.BarWeight, left, right, load.TotalWeight, s.Reps, s.Rpe, s.Kind, s.Notes);
        }).ToArray());
}
