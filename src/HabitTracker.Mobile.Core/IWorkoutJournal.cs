using HabitTracker.Core;

namespace HabitTracker.Mobile.Core;

public interface IWorkoutJournal
{
    Task<IReadOnlyList<WorkoutResponse>> ListAsync(CancellationToken ct = default);
    Task<WorkoutResponse> CreateAsync(CreateWorkoutRequest request, CancellationToken ct = default);
    Task<WorkoutResponse> AddSetAsync(int id, LogSetRequest request, CancellationToken ct = default);
    Task<WorkoutResponse> RemoveSetAsync(int id, int setId, CancellationToken ct = default);
    Task<WorkoutResponse> CompleteAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<HabitRecord>> ListHabitsAsync(CancellationToken ct = default);
    Task<HabitRecord> SaveHabitAsync(int? id, SaveHabitRequest request, CancellationToken ct = default);
    Task DeleteHabitAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<HabitLogRecord>> ListHabitLogsAsync(int id, CancellationToken ct = default);
    Task<HabitLogRecord> SaveHabitLogAsync(SaveHabitLogRequest request, CancellationToken ct = default);
    Task DeleteHabitLogAsync(int id, CancellationToken ct = default);
}
