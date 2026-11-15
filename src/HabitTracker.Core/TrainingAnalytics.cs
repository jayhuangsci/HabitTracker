using System.Globalization;
using System.Text;

namespace HabitTracker.Core;

public sealed record VolumePoint(DateOnly Week, decimal Volume, int Sessions)
{
    public string Label => Week.ToString("MMM d", CultureInfo.InvariantCulture);
}
public sealed record PersonalRecord(string ExerciseId, string ExerciseName, decimal BestWeightPounds,
    decimal EstimatedOneRepMaxPounds, DateOnly Date)
{
    public string WeightLabel => $"{BestWeightPounds:N1} lb";
    public string EstimateLabel => $"{EstimatedOneRepMaxPounds:N1} lb estimated 1RM";
}

public static class TrainingAnalytics
{
    // Epley is a training estimate; avoid extrapolating high-rep sets.
    public static decimal EstimateOneRepMax(decimal weight, int reps) =>
        reps is < 1 or > 12 || weight <= 0 ? 0 : reps == 1 ? weight : weight * (1 + reps / 30m);

    public static DateOnly StartOfWeek(DateOnly date) => date.AddDays(-((7 + (int)date.DayOfWeek - (int)DayOfWeek.Monday) % 7));

    public static IReadOnlyList<VolumePoint> WeeklyVolume(IEnumerable<WorkoutResponse> workouts, DateOnly today, int weeks = 8)
    {
        if (weeks is < 1 or > 52) throw new ArgumentOutOfRangeException(nameof(weeks));
        var first = StartOfWeek(today).AddDays(-7 * (weeks - 1));
        var groups = workouts.Where(w => w.IsCompleted && w.Date <= today && w.Date >= first)
            .GroupBy(w => StartOfWeek(w.Date)).ToDictionary(g => g.Key, g => g.ToList());
        return Enumerable.Range(0, weeks).Select(i =>
        {
            var week = first.AddDays(i * 7);
            var rows = groups.GetValueOrDefault(week) ?? [];
            return new VolumePoint(week, rows.Sum(w => w.VolumePounds), rows.Count);
        }).ToArray();
    }

    public static IReadOnlyList<PersonalRecord> PersonalRecords(IEnumerable<WorkoutResponse> workouts) =>
        workouts.Where(w => w.IsCompleted).SelectMany(w => w.Sets.Where(s => s.Kind == SetKind.Working).Select(s => (Workout: w, Set: s)))
            .GroupBy(x => x.Set.ExerciseId).Select(g =>
            {
                var best = g.OrderByDescending(x => PlateLoad.Convert(x.Set.TotalWeight, x.Set.Unit, WeightUnit.Pounds))
                    .ThenByDescending(x => x.Workout.Date).First();
                return new PersonalRecord(g.Key, best.Set.ExerciseName,
                    PlateLoad.Convert(best.Set.TotalWeight, best.Set.Unit, WeightUnit.Pounds),
                    g.Max(x => x.Set.EstimatedOneRepMaxPounds), best.Workout.Date);
            }).OrderByDescending(p => p.BestWeightPounds).ToArray();
}

public static class WorkoutCsvExporter
{
    public static string Export(IEnumerable<WorkoutResponse> workouts)
    {
        var csv = new StringBuilder("Date,Workout,Status,Exercise,Kind,Bar,Left plates,Right plates,Unit,Total,Reps,RPE,Notes\r\n");
        foreach (var workout in workouts.OrderBy(w => w.Date).ThenBy(w => w.Id))
        foreach (var set in workout.Sets)
        {
            string[] cells = [workout.Date.ToString("yyyy-MM-dd"), workout.Name, workout.StatusLabel,
                set.ExerciseName, set.Kind.ToString(), PlateLoad.Format(set.BarWeight),
                PlateLoad.FormatStack(set.LeftPlates), PlateLoad.FormatStack(set.RightPlates), set.UnitSymbol,
                PlateLoad.Format(set.TotalWeight), set.Reps.ToString(CultureInfo.InvariantCulture),
                set.Rpe?.ToString(CultureInfo.InvariantCulture) ?? "", set.Notes ?? ""];
            csv.AppendLine(string.Join(",", cells.Select(Escape)));
        }
        return csv.ToString();
    }

    private static string Escape(string value)
    {
        // Neutralize spreadsheet formulas in free-text fields, including leading whitespace.
        var trimmed = value.TrimStart();
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0]) || value.StartsWith('\t') || value.StartsWith('\r')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
