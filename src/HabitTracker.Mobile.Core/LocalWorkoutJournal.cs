using System.Text.Json;
using HabitTracker.Core;

namespace HabitTracker.Mobile.Core;

/// <summary>Local and demo journals share lifecycle rules but use distinct durable document keys.</summary>
public sealed class LocalWorkoutJournal(IJournalStore store, JournalMode mode, TimeProvider clock) : IWorkoutJournal
{
    private readonly string _key = mode switch
    {
        JournalMode.Local => "journal/local/v1",
        JournalMode.Demo => "journal/demo/v1",
        _ => throw new ArgumentException("The connected journal uses the HTTP adapter.")
    };

    public async Task EnsureDemoAsync(DateOnly today, CancellationToken ct = default)
    {
        if (mode != JournalMode.Demo) return;
        if (await store.ReadAsync(_key, ct) != null) return;
        var json = JsonSerializer.Serialize(MobileDemoData.Create(today), JournalJsonContext.Default.JournalDocument);
        await store.TryWriteAsync(_key, 0, json, ct);
    }

    public async Task<IReadOnlyList<WorkoutResponse>> ListAsync(CancellationToken ct = default) =>
        (await ReadAsync(ct)).Document.Workouts.OrderByDescending(w => w.Date).ThenByDescending(w => w.Id).ToArray();

    public Task<WorkoutResponse> CreateAsync(CreateWorkoutRequest request, CancellationToken ct = default)
    {
        var name = RequiredText(request.Name, 100, "Session name");
        if (request.Date == default) throw new ArgumentException("Choose a workout date.");
        if (request.Notes?.Length > 1000) throw new ArgumentException("Session notes must not exceed 1,000 characters.");
        return ChangeAsync(document =>
        {
            var row = new WorkoutResponse(document.NextWorkoutId++, name, request.Date, OptionalText(request.Notes),
                clock.GetUtcNow().UtcDateTime, null, []);
            document.Workouts.Add(row);
            return row;
        }, ct);
    }

    public Task<WorkoutResponse> AddSetAsync(int id, LogSetRequest request, CancellationToken ct = default)
    {
        var load = request.Validate();
        return ChangeAsync(document =>
        {
            var workout = FindWorkout(document, id);
            RequireOpen(workout);
            if (document.Workouts.SelectMany(w => w.Sets).Any(s => s.ClientId == request.ClientId))
                throw new JournalConflictException("This set is already saved. Refresh the session before retrying.");
            var exercise = TrainingCatalog.FindExercise(request.ExerciseId);
            var set = new WorkoutSetResponse(document.NextSetId++, request.ClientId, exercise.Id, exercise.Name, exercise.Muscle,
                request.Unit, load.BarWeight, load.LeftPlates.ToArray(), load.RightPlates.ToArray(), load.TotalWeight,
                request.Reps, request.Rpe, request.Kind, OptionalText(request.Notes));
            return ReplaceWorkout(document, workout with { Sets = workout.Sets.Append(set).ToArray() });
        }, ct);
    }

    public Task<WorkoutResponse> RemoveSetAsync(int id, int setId, CancellationToken ct = default) =>
        ChangeAsync(document =>
        {
            var workout = FindWorkout(document, id);
            RequireOpen(workout);
            if (!workout.Sets.Any(s => s.Id == setId)) throw new JournalNotFoundException("That set is no longer in this session.");
            return ReplaceWorkout(document, workout with { Sets = workout.Sets.Where(s => s.Id != setId).ToArray() });
        }, ct);

    public Task<WorkoutResponse> CompleteAsync(int id, CancellationToken ct = default) =>
        ChangeAsync(document =>
        {
            var workout = FindWorkout(document, id);
            if (workout.IsCompleted) return workout;
            if (workout.Sets.Count == 0) throw new JournalConflictException("Log at least one set before finishing.");
            return ReplaceWorkout(document, workout with { CompletedAt = clock.GetUtcNow().UtcDateTime });
        }, ct);

    public async Task<IReadOnlyList<HabitRecord>> ListHabitsAsync(CancellationToken ct = default) =>
        (await ReadAsync(ct)).Document.Habits.OrderBy(h => h.Name).ToArray();

    public Task<HabitRecord> SaveHabitAsync(int? id, SaveHabitRequest request, CancellationToken ct = default)
    {
        var name = RequiredText(request.Name, 100, "Habit name");
        var unit = RequiredText(request.Unit, 20, "Habit unit");
        if (request.Description?.Length > 300) throw new ArgumentException("Habit description must not exceed 300 characters.");
        return ChangeAsync(document =>
        {
            if (document.Habits.Any(h => h.Id != id && string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new JournalConflictException("A habit with that name already exists.");
            HabitRecord row;
            if (id.HasValue)
            {
                var existing = document.Habits.FirstOrDefault(h => h.Id == id) ?? throw new JournalNotFoundException("Habit not found.");
                row = existing with { Name = name, Unit = unit, Description = OptionalText(request.Description) };
                document.Habits[document.Habits.IndexOf(existing)] = row;
            }
            else
            {
                row = new HabitRecord(document.NextHabitId++, name, unit, OptionalText(request.Description), clock.GetUtcNow().UtcDateTime);
                document.Habits.Add(row);
            }
            return row;
        }, ct);
    }

    public async Task DeleteHabitAsync(int id, CancellationToken ct = default) =>
        await ChangeAsync(document =>
        {
            if (document.Habits.RemoveAll(h => h.Id == id) == 0) throw new JournalNotFoundException("Habit not found.");
            document.HabitLogs.RemoveAll(l => l.HabitId == id);
            return true;
        }, ct);

    public async Task<IReadOnlyList<HabitLogRecord>> ListHabitLogsAsync(int id, CancellationToken ct = default) =>
        (await ReadAsync(ct)).Document.HabitLogs.Where(l => l.HabitId == id).OrderByDescending(l => l.Date).ToArray();

    public Task<HabitLogRecord> SaveHabitLogAsync(SaveHabitLogRequest request, CancellationToken ct = default)
    {
        if (request.Date == default) throw new ArgumentException("Choose a habit date.");
        if (request.Quantity < 0) throw new ArgumentException("Quantity cannot be negative.");
        if (request.Comment?.Length > 500) throw new ArgumentException("Habit notes must not exceed 500 characters.");
        return ChangeAsync(document =>
        {
            if (!document.Habits.Any(h => h.Id == request.HabitId)) throw new JournalNotFoundException("Habit not found.");
            var existing = document.HabitLogs.FirstOrDefault(l => l.HabitId == request.HabitId && l.Date == request.Date);
            var row = new HabitLogRecord(existing?.Id ?? document.NextHabitLogId++, request.HabitId, request.Date,
                request.Quantity, OptionalText(request.Comment), existing?.CreatedAt ?? clock.GetUtcNow().UtcDateTime);
            if (existing == null) document.HabitLogs.Add(row);
            else document.HabitLogs[document.HabitLogs.IndexOf(existing)] = row;
            return row;
        }, ct);
    }

    public async Task DeleteHabitLogAsync(int id, CancellationToken ct = default) =>
        await ChangeAsync(document =>
        {
            if (document.HabitLogs.RemoveAll(l => l.Id == id) == 0) throw new JournalNotFoundException("Habit log not found.");
            return true;
        }, ct);

    private async Task<(JournalDocument Document, long Version)> ReadAsync(CancellationToken ct)
    {
        var saved = await store.ReadAsync(_key, ct);
        if (saved == null) return (new JournalDocument(), 0);
        JournalDocument document;
        try { document = JsonSerializer.Deserialize(saved.Json, JournalJsonContext.Default.JournalDocument) ?? throw new JsonException("Empty journal."); }
        catch (JsonException ex) { throw new InvalidDataException("Saved journal data is unreadable. It has been retained unchanged.", ex); }
        if (document.SchemaVersion != 1) throw new InvalidDataException("This journal needs a newer app version. Its saved data has been retained.");
        if (document.Workouts == null || document.Habits == null || document.HabitLogs == null)
            throw new InvalidDataException("The saved journal is incomplete. It has been retained unchanged.");
        return (document, saved.Version);
    }

    private async Task<T> ChangeAsync<T>(Func<JournalDocument, T> change, CancellationToken ct)
    {
        // Re-read and compare versions inside the store transaction; another tab cannot silently lose a write.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var (document, version) = await ReadAsync(ct);
            var result = change(document);
            var json = JsonSerializer.Serialize(document, JournalJsonContext.Default.JournalDocument);
            if (await store.TryWriteAsync(_key, version, json, ct)) return result;
        }
        throw new JournalConflictException("The journal changed in another window. Refresh before saving again.");
    }

    private static WorkoutResponse FindWorkout(JournalDocument document, int id) =>
        document.Workouts.FirstOrDefault(w => w.Id == id) ?? throw new JournalNotFoundException("Workout not found.");

    private static WorkoutResponse ReplaceWorkout(JournalDocument document, WorkoutResponse workout)
    {
        var index = document.Workouts.FindIndex(w => w.Id == workout.Id);
        document.Workouts[index] = workout;
        return workout;
    }
    private static void RequireOpen(WorkoutResponse workout)
    {
        if (workout.IsCompleted) throw new JournalConflictException("This session is complete. Reuse its setup in a new workout.");
    }
    private static string RequiredText(string? value, int maximum, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximum)
            throw new ArgumentException(name + " is required and must be at most " + maximum + " characters.");
        return value.Trim();
    }
    private static string? OptionalText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
