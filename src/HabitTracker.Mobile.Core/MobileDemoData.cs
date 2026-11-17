using System.Security.Cryptography;
using System.Text;
using HabitTracker.Core;

namespace HabitTracker.Mobile.Core;

public static class MobileDemoData
{
    public static JournalDocument Create(DateOnly today)
    {
        var document = new JournalDocument();
        for (var ago = 55; ago >= 0; ago--)
        {
            var date = today.AddDays(-ago);
            if (date.DayOfWeek is not (DayOfWeek.Monday or DayOfWeek.Wednesday or DayOfWeek.Friday or DayOfWeek.Saturday)) continue;
            var lower = date.DayOfWeek is DayOfWeek.Wednesday or DayOfWeek.Saturday;
            var week = (55 - ago) / 7;
            var sets = new List<WorkoutSetResponse>();
            var exercises = lower ? new[] { "back-squat", "romanian-deadlift", "hip-thrust" } : ["bench-press", "barbell-row", "overhead-press"];
            foreach (var (exerciseId, exerciseIndex) in exercises.Select((id, index) => (id, index)))
            for (var index = 0; index < 4; index++)
            {
                var exercise = TrainingCatalog.FindExercise(exerciseId);
                decimal[] plates = index == 0 ? [] : lower ? [45, 25, 5 + week * 2.5m] : [25, 10 + week * 2.5m];
                if (!lower && exerciseIndex == 0 && week >= 5 && index > 0) plates = [45, 25, 10];
                if (exerciseIndex == 2 && index > 0) plates = [10, 5 + week * 1.25m];
                var load = new PlateLoad(45, plates, plates, WeightUnit.Pounds);
                var stable = SHA256.HashData(Encoding.UTF8.GetBytes(date + "|" + exerciseId + "|" + index));
                sets.Add(new WorkoutSetResponse(document.NextSetId++, new Guid(stable.AsSpan(0, 16)), exerciseId, exercise.Name, exercise.Muscle,
                    WeightUnit.Pounds, 45, plates.ToArray(), plates.ToArray(), load.TotalWeight, index == 0 ? 10 : 8 - index,
                    index == 0 ? null : 7 + index * .5m, index == 0 ? SetKind.Warmup : SetKind.Working, null));
            }
            document.Workouts.Add(new WorkoutResponse(document.NextWorkoutId++, lower ? "Lower body" : "Upper body", date,
                "Synthetic demo session", date.ToDateTime(new TimeOnly(17, 30), DateTimeKind.Utc),
                date.ToDateTime(new TimeOnly(18, 20), DateTimeKind.Utc), sets));
        }
        foreach (var (name, unit, quantity) in new[] { ("Daily walk", "km", 4m), ("Sleep", "hours", 7.5m), ("Hydration", "liters", 2.5m) })
        {
            var habit = new HabitRecord(document.NextHabitId++, name, unit, "Demo recovery habit", today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            document.Habits.Add(habit);
            for (var ago = 0; ago < 7; ago++) document.HabitLogs.Add(new HabitLogRecord(document.NextHabitLogId++, habit.Id, today.AddDays(-ago),
                quantity, "Synthetic demo record", today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
        }
        return document;
    }
}
