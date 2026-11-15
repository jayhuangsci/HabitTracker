using HabitTracker.Core;

namespace HabitTracker.Core.Tests;

public class AnalyticsTests
{
    private static WorkoutSetResponse Set(decimal load, SetKind kind = SetKind.Working, WeightUnit unit = WeightUnit.Pounds) =>
        new(1, Guid.NewGuid(), "bench-press", "Barbell bench press", "Chest", unit, 45, [45], [45], load, 5, 8, kind, null);
    private static WorkoutResponse Workout(DateOnly date, bool done, params WorkoutSetResponse[] sets) =>
        new(1, "Upper body", date, null, DateTime.UtcNow, done ? DateTime.UtcNow : null, sets);

    [Fact]
    public void WeeklyVolumeFillsEmptyWeeksAndExcludesDraftsWarmupsAndFutureDates()
    {
        var today = new DateOnly(2026, 11, 15);
        var rows = new[] {
            Workout(today, true, Set(205), Set(45, SetKind.Warmup)),
            Workout(today.AddDays(-7), false, Set(205)),
            Workout(today.AddDays(1), true, Set(205)) };
        var points = TrainingAnalytics.WeeklyVolume(rows, today, 3);
        Assert.Equal(3, points.Count);
        Assert.Equal(new DateOnly(2026, 11, 9), points[^1].Week);
        Assert.Equal(1025, points[^1].Volume);
        Assert.Equal(0, points[0].Volume);
        Assert.Equal(1, points.Sum(p => p.Sessions));
    }

    [Fact]
    public void RecordsCompareUnitsAndExcludeWarmups()
    {
        var rows = new[] { Workout(new(2026, 11, 15), true, Set(205), Set(100, unit: WeightUnit.Kilograms), Set(500, SetKind.Warmup)) };
        var record = Assert.Single(TrainingAnalytics.PersonalRecords(rows));
        Assert.InRange(record.BestWeightPounds, 220.46m, 220.47m);
    }

    [Theory]
    [InlineData(100, 1, 100)]
    [InlineData(100, 6, 120)]
    [InlineData(100, 13, 0)]
    public void EpleyLimitsExtrapolation(decimal weight, int reps, decimal expected) =>
        Assert.Equal(expected, TrainingAnalytics.EstimateOneRepMax(weight, reps));

    [Fact]
    public void CsvPreservesPlateStacksAndEscapesQuotesNewlinesAndFormulaInjection()
    {
        var workout = Workout(new(2026, 11, 15), true, Set(205) with { Notes = "line 1\n\"line 2\"" }) with { Name = " =HYPERLINK(\"bad\")" };
        var csv = WorkoutCsvExporter.Export([workout]);
        Assert.Contains("\"' =HYPERLINK(\"\"bad\"\")\"", csv);
        Assert.Contains("\"line 1\n\"\"line 2\"\"\"", csv);
        Assert.Contains("Left plates,Right plates", csv);
    }
}
