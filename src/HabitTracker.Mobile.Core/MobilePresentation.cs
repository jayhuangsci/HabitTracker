using System.Windows.Input;
using HabitTracker.Core;

namespace HabitTracker.Mobile.Core;

public sealed record ModeOption(JournalMode Value, string Label)
{
    public override string ToString() => Label;
}
public sealed record PlateOption(decimal Weight)
{
    public string Label => "+" + PlateLoad.Format(Weight);
}
public sealed record PlateChip(decimal Weight, int Index, ICommand RemoveCommand)
{
    public string Label => PlateLoad.Format(Weight) + " ×";
}
public sealed record RoutineTile(Routine Routine, ICommand StartCommand)
{
    public string Name => Routine.Name;
    public string Detail => Routine.ExerciseIds.Length + " exercises · barbell";
}
public sealed record DayChip(string Label, bool Trained);
public sealed record ChartBar(string Label, double Height, bool Current, string Detail);
public sealed record MuscleSummary(string Name, int Sets, double Fraction);
public sealed record SetTile(WorkoutSetResponse Set, ICommand ReuseCommand, ICommand RemoveCommand, bool CanRemove)
{
    public string Name => Set.ExerciseName;
    public string MainLine => Set.Reps + " reps × " + Set.LoadLabel;
    public string Detail => Set.Kind + (Set.Rpe.HasValue ? " · RPE " + PlateLoad.Format(Set.Rpe.Value) : "");
    public string PlateLine => Set.PlateLabel;
    public string BarLine => "Bar: " + PlateLoad.Format(Set.BarWeight) + " " + Set.UnitSymbol;
    public bool HasNotes => !string.IsNullOrWhiteSpace(Set.Notes);
}
public sealed class WorkoutTile : ObservableModel
{
    private bool _expanded;
    public WorkoutResponse Workout { get; }
    public string Name => Workout.Name;
    public string DateLabel => Workout.Date.ToString("ddd, MMM d");
    public string Detail => Workout.SetCount + (Workout.SetCount == 1 ? " set · " : " sets · ") + Workout.VolumeLabel;
    public string Status => Workout.StatusLabel;
    public bool CanResume => !Workout.IsCompleted;
    public IReadOnlyList<SetTile> Sets { get; }
    public ICommand ToggleCommand { get; }
    public ICommand ResumeCommand { get; }
    public IReadOnlyList<SetTile> VisibleSets => Expanded ? Sets : [];
    public bool Expanded { get => _expanded; private set { if (Set(ref _expanded, value)) Notify(nameof(VisibleSets)); } }
    public WorkoutTile(WorkoutResponse workout, IReadOnlyList<SetTile> sets, Action resume)
    {
        Workout = workout; Sets = sets;
        ToggleCommand = new MobileCommand(_ => Expanded = !Expanded);
        ResumeCommand = new MobileCommand(_ => resume(), () => CanResume);
    }
}
