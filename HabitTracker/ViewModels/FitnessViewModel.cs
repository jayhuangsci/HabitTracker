using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using HabitTracker.Core;
using HabitTracker.Services;
using Microsoft.Win32;

namespace HabitTracker.ViewModels;

public sealed record UnitOption(WeightUnit Value, string Label)
{
    public override string ToString() => Label;
}
public sealed record MuscleRow(string Name, int Sets, double Fraction);
public sealed record TrainingDay(string Label, bool Done);

public sealed class FitnessViewModel : ViewModelBase, IDisposable
{
    private readonly FitnessApiClient _api;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly List<WorkoutResponse> _workouts = [];
    private string _page = "Overview", _status = "Connecting to your journal…", _connection = "Connecting", _search = "";
    private bool _busy, _error, _demo, _mirror = true, _loadingSnapshot;
    private string _bar = "45", _left = "45 + 25 + 10", _right = "45 + 25 + 10", _reps = "5", _rpe = "8", _notes = "";
    private string _workoutName = "Upper body", _loadError = "";
    private DateTime _workoutDate = DateTime.Today;
    private WeightUnit _unit;
    private SetKind _kind;
    private PlateLoad? _load;
    private Exercise _exercise = TrainingCatalog.Exercises[0];
    private Routine _routine = TrainingCatalog.Routines[0];
    private WorkoutResponse? _active, _selectedHistory;
    private WorkoutSetResponse? _selectedSet, _selectedHistorySet;
    private DateTimeOffset? _deadline;
    private int _remaining = 90, _weeklyGoal = PreferenceStore.ReadGoal();

    public FitnessViewModel(FitnessApiClient api, HabitListViewModel habits)
    {
        _api = api;
        Habits = habits;
        NavigateCommand = new RelayCommand(p => Navigate(p?.ToString() ?? "Overview"));
        StartRoutineCommand = new RelayCommand(p => StartRoutine(p?.ToString()));
        RefreshCommand = new AsyncRelayCommand(() => RunAsync(RefreshAsync), () => !IsBusy);
        AddSetCommand = new AsyncRelayCommand(() => RunAsync(AddSetAsync), () => !IsBusy && CanLog);
        FinishCommand = new AsyncRelayCommand(() => RunAsync(FinishAsync), () => !IsBusy && ActiveWorkout?.Sets.Count > 0);
        RemoveSetCommand = new AsyncRelayCommand(() => RunAsync(RemoveSetAsync), () => !IsBusy && SelectedSessionSet != null);
        ExportCommand = new RelayCommand(_ => Export(), () => _workouts.Any(w => w.Sets.Count > 0) && !IsBusy);
        CopyLoadCommand = new RelayCommand(_ => CopyLoad(), () => SelectedHistorySet != null && !IsBusy);
        ResumeCommand = new RelayCommand(_ => Resume(), () => SelectedHistory is { IsCompleted: false } && !IsBusy);
        TimerCommand = new RelayCommand(_ => ToggleTimer());
        ResetTimerCommand = new RelayCommand(_ => { _deadline = null; _remaining = 90; NotifyTimer(); });
        AddTimeCommand = new RelayCommand(_ => { if (_deadline.HasValue) _deadline = _deadline.Value.AddSeconds(15); else _remaining += 15; NotifyTimer(); });
        _timer.Tick += OnTick;
        _timer.Start();
        RecalculateLoad();
    }

    public HabitListViewModel Habits { get; }
    public RelayCommand NavigateCommand { get; }
    public RelayCommand StartRoutineCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddSetCommand { get; }
    public AsyncRelayCommand FinishCommand { get; }
    public AsyncRelayCommand RemoveSetCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand CopyLoadCommand { get; }
    public RelayCommand ResumeCommand { get; }
    public RelayCommand TimerCommand { get; }
    public RelayCommand ResetTimerCommand { get; }
    public RelayCommand AddTimeCommand { get; }
    public IReadOnlyList<Exercise> Exercises => TrainingCatalog.Exercises;
    public IReadOnlyList<Routine> Routines => TrainingCatalog.Routines;
    public IReadOnlyList<UnitOption> Units { get; } = [new(WeightUnit.Pounds, "Pounds · lb"), new(WeightUnit.Kilograms, "Kilograms · kg")];
    public SetKind[] SetKinds { get; } = Enum.GetValues<SetKind>();
    public int[] GoalOptions { get; } = Enumerable.Range(1, 7).ToArray();
    public ObservableCollection<WorkoutResponse> RecentWorkouts { get; } = [];
    public ObservableCollection<WorkoutResponse> FilteredWorkouts { get; } = [];
    public ObservableCollection<WorkoutSetResponse> SessionSets { get; } = [];
    public ObservableCollection<WorkoutSetResponse> HistorySets { get; } = [];
    public ObservableCollection<PersonalRecord> Records { get; } = [];
    public ObservableCollection<MuscleRow> Muscles { get; } = [];
    public ObservableCollection<TrainingDay> TrainingDays { get; } = [];
    public IReadOnlyList<VolumePoint> ChartPoints => TrainingAnalytics.WeeklyVolume(_workouts, Today);
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    private IEnumerable<WorkoutResponse> Completed => _workouts.Where(w => w.IsCompleted && w.Date <= Today);
    private IEnumerable<WorkoutResponse> Last28 => Completed.Where(w => w.Date >= Today.AddDays(-27));
    public string SessionsLabel => Last28.Count().ToString(CultureInfo.InvariantCulture);
    public string VolumeLabel => Last28.Sum(w => w.VolumePounds).ToString("N0");
    public string BestWeightLabel => Records.FirstOrDefault()?.WeightLabel ?? "—";
    public string BestLiftCaption => Records.FirstOrDefault()?.ExerciseName ?? "Your next lift starts here";
    public string ThisWeekVolume => ChartPoints.Last().Volume.ToString("N0");
    public int WeeklyCount => Completed.Count(w => w.Date >= TrainingAnalytics.StartOfWeek(Today));
    public string GoalLabel => $"{WeeklyCount} / {WeeklyGoal}";
    public double GoalFraction => Math.Min(1, (double)WeeklyCount / WeeklyGoal);
    public string GoalMessage => WeeklyCount >= WeeklyGoal ? "Weekly goal reached. Make room for recovery." : $"{Math.Max(0, WeeklyGoal - WeeklyCount)} more sessions to your weekly goal.";
    public string TodayLabel => DateTime.Today.ToString("dddd, MMMM d");
    public bool HasNoWorkouts => _workouts.Count == 0;
    public bool HasWorkouts => !HasNoWorkouts;
    public string WorkspaceLabel => _demo ? "DEMO WORKSPACE" : "PERSONAL WORKSPACE";
    public string ConnectionLabel { get => _connection; private set => SetProperty(ref _connection, value); }
    public string StatusMessage { get => _status; private set => SetProperty(ref _status, value); }
    public bool HasError { get => _error; private set => SetProperty(ref _error, value); }
    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) RaiseCommands(); } }
    public bool IsOverview => _page == "Overview";
    public bool IsWorkout => _page == "Workout";
    public bool IsHistory => _page == "History";
    public bool IsProgress => _page == "Progress";
    public bool IsHabits => _page == "Habits";
    public string CurrentPage => _page;
    public string Search { get => _search; set { if (SetProperty(ref _search, value)) FilterHistory(); } }
    public int WeeklyGoal
    {
        get => _weeklyGoal;
        set
        {
            if (value is < 1 or > 7 || !SetProperty(ref _weeklyGoal, value)) return;
            try { PreferenceStore.SaveGoal(value); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusMessage = "Goal updated for this session. Your settings file could not be saved."; }
            NotifyStats();
        }
    }

    public string WorkoutName { get => _workoutName; set => SetProperty(ref _workoutName, value); }
    public DateTime WorkoutDate { get => _workoutDate; set => SetProperty(ref _workoutDate, value); }
    public bool HasNoActiveWorkout => _active == null;
    public string SessionCaption => _active == null ? "A fresh page. Make the first set count." : $"{SessionSets.Count} sets saved · {_active.VolumeLabel} working volume";
    public WorkoutResponse? ActiveWorkout => _active;
    public WorkoutResponse? SelectedHistory
    {
        get => _selectedHistory;
        set { if (SetProperty(ref _selectedHistory, value)) { Replace(HistorySets, value?.Sets ?? []); OnPropertyChanged(nameof(HasSelectedHistory)); RaiseCommands(); } }
    }
    public bool HasSelectedHistory => SelectedHistory != null;
    public WorkoutSetResponse? SelectedSessionSet { get => _selectedSet; set { SetProperty(ref _selectedSet, value); RaiseCommands(); } }
    public WorkoutSetResponse? SelectedHistorySet { get => _selectedHistorySet; set { SetProperty(ref _selectedHistorySet, value); RaiseCommands(); } }
    public Exercise SelectedExercise { get => _exercise; set => SetProperty(ref _exercise, value); }
    public Routine SelectedRoutine
    {
        get => _routine;
        set { if (value != null && SetProperty(ref _routine, value)) { if (_active == null) WorkoutName = value.Name; OnPropertyChanged(nameof(RoutineExerciseNames)); } }
    }
    public string RoutineExerciseNames => string.Join("  ·  ", SelectedRoutine.ExerciseIds.Select(id => TrainingCatalog.FindExercise(id).Name));
    public string BarWeight { get => _bar; set { if (SetProperty(ref _bar, value)) RecalculateLoad(); } }
    public string LeftStack { get => _left; set { if (SetProperty(ref _left, value)) RecalculateLoad(); } }
    public string RightStack { get => _right; set { if (SetProperty(ref _right, value)) RecalculateLoad(); } }
    public bool Mirror { get => _mirror; set { if (SetProperty(ref _mirror, value)) { OnPropertyChanged(nameof(IndependentSides)); RecalculateLoad(); } } }
    public bool IndependentSides => !Mirror;
    public WeightUnit Unit
    {
        get => _unit;
        set
        {
            if (!SetProperty(ref _unit, value)) return;
            if (!_loadingSnapshot)
            {
                _bar = value == WeightUnit.Pounds ? "45" : "20";
                _left = _right = value == WeightUnit.Pounds ? "45 + 25 + 10" : "20 + 5 + 1.25";
                OnPropertyChanged(nameof(BarWeight)); OnPropertyChanged(nameof(LeftStack)); OnPropertyChanged(nameof(RightStack));
            }
            RecalculateLoad();
        }
    }
    public string Reps { get => _reps; set { if (SetProperty(ref _reps, value)) ValidateEntry(); } }
    public string Rpe { get => _rpe; set { if (SetProperty(ref _rpe, value)) ValidateEntry(); } }
    public string SetNotes { get => _notes; set { if (SetProperty(ref _notes, value)) ValidateEntry(); } }
    public SetKind Kind { get => _kind; set => SetProperty(ref _kind, value); }
    public PlateLoad? LoadPreview => _load;
    public string TotalLoadLabel => _load == null ? "—" : PlateLoad.Format(_load.TotalWeight);
    public string UnitSymbol => Unit == WeightUnit.Pounds ? "lb" : "kg";
    public string LoadFormula => _load?.Formula ?? "Check your plate entries below.";
    public string BalanceLabel => _load == null ? "Check plate input" : _load.IsBalanced ? "Balanced load" : "Uneven sides · verify your setup";
    public bool HasImbalance => _load is { IsBalanced: false };
    public string LoadError { get => _loadError; private set => SetProperty(ref _loadError, value); }
    public bool CanLog => _load != null && int.TryParse(Reps, out var reps) && reps is >= 1 and <= 100
        && (string.IsNullOrWhiteSpace(Rpe) || decimal.TryParse(Rpe, NumberStyles.Number, CultureInfo.InvariantCulture, out var rpe) && rpe is >= 1 and <= 10)
        && SetNotes.Length <= 500;

    public Task InitializeAsync() => RunAsync(RefreshAsync);
    public void Navigate(string page)
    {
        if (page is not ("Overview" or "Workout" or "History" or "Progress" or "Habits")) return;
        _page = page;
        foreach (var name in new[] { nameof(CurrentPage), nameof(IsOverview), nameof(IsWorkout), nameof(IsHistory), nameof(IsProgress), nameof(IsHabits) }) OnPropertyChanged(name);
        if (IsHabits) _ = Habits.LoadHabitsAsync();
    }

    private async Task RefreshAsync()
    {
        var rows = await _api.ListAsync();
        _demo = await _api.IsDemoAsync();
        _workouts.Clear(); _workouts.AddRange(rows);
        SetActive(_workouts.FirstOrDefault(w => w.Id == _active?.Id && !w.IsCompleted) ?? _workouts.FirstOrDefault(w => !w.IsCompleted));
        Replace(RecentWorkouts, rows.Take(5));
        Replace(Records, TrainingAnalytics.PersonalRecords(Completed));
        var muscleGroups = Last28.SelectMany(w => w.Sets).Where(s => s.Kind == SetKind.Working).GroupBy(s => s.Muscle).ToArray();
        var total = muscleGroups.Sum(g => g.Count());
        Replace(Muscles, muscleGroups.OrderByDescending(g => g.Count()).Select(g => new MuscleRow(g.Key, g.Count(), total == 0 ? 0 : (double)g.Count() / total)));
        var monday = TrainingAnalytics.StartOfWeek(Today);
        Replace(TrainingDays, Enumerable.Range(0, 7).Select(i => new TrainingDay(monday.AddDays(i).ToString("ddd")[..1], Completed.Any(w => w.Date == monday.AddDays(i)))));
        FilterHistory();
        NotifyStats();
        ConnectionLabel = "Journal connected";
        OnPropertyChanged(nameof(WorkspaceLabel));
        StatusMessage = _demo ? "Demo data · changes stay in your separate demo journal." : "Your journal is up to date.";
    }

    private void NotifyStats()
    {
        foreach (var property in new[] { nameof(SessionsLabel), nameof(VolumeLabel), nameof(BestWeightLabel), nameof(BestLiftCaption), nameof(ChartPoints), nameof(ThisWeekVolume), nameof(GoalLabel), nameof(GoalFraction), nameof(GoalMessage), nameof(WeeklyCount), nameof(HasNoWorkouts), nameof(HasWorkouts) }) OnPropertyChanged(property);
    }
    private void FilterHistory()
    {
        Replace(FilteredWorkouts, _workouts.Where(w => string.IsNullOrWhiteSpace(Search) || w.Name.Contains(Search, StringComparison.OrdinalIgnoreCase)
            || w.Sets.Any(s => s.ExerciseName.Contains(Search, StringComparison.OrdinalIgnoreCase))));
        var selectedId = SelectedHistory?.Id;
        SelectedHistory = FilteredWorkouts.FirstOrDefault(w => w.Id == selectedId) ?? FilteredWorkouts.FirstOrDefault();
    }
    private void StartRoutine(string? id)
    {
        if (_active == null)
        {
            SelectedRoutine = Routines.FirstOrDefault(r => r.Id == id) ?? SelectedRoutine;
            WorkoutName = SelectedRoutine.Name;
            WorkoutDate = DateTime.Today;
            SelectedExercise = TrainingCatalog.FindExercise(SelectedRoutine.ExerciseIds[0]);
        }
        Navigate("Workout");
    }
    private async Task AddSetAsync()
    {
        if (!CanLog || _load == null) return;
        var request = new LogSetRequest { ExerciseId = SelectedExercise.Id, Unit = Unit, BarWeight = _load.BarWeight,
            LeftPlates = _load.LeftPlates.ToArray(), RightPlates = _load.RightPlates.ToArray(), Reps = int.Parse(Reps, CultureInfo.InvariantCulture),
            Rpe = string.IsNullOrWhiteSpace(Rpe) ? null : decimal.Parse(Rpe, CultureInfo.InvariantCulture), Kind = Kind, Notes = SetNotes };
        if (_active == null) SetActive(await _api.CreateAsync(new CreateWorkoutRequest { Name = WorkoutName, Date = DateOnly.FromDateTime(WorkoutDate) }));
        SetActive(await _api.AddSetAsync(_active!.Id, request));
        await RefreshAsync();
        SetNotes = "";
        _remaining = 90; _deadline = DateTimeOffset.UtcNow.AddSeconds(90); NotifyTimer();
        StatusMessage = $"Set saved · {request.Reps} × {TotalLoadLabel} {UnitSymbol}. Rest, then go again.";
    }
    private async Task FinishAsync()
    {
        if (_active == null) return;
        await _api.CompleteAsync(_active.Id);
        SetActive(null); _deadline = null; _remaining = 90; NotifyTimer();
        await RefreshAsync();
        Navigate("Overview");
        StatusMessage = "Session complete. Every set is in your journal.";
    }
    private async Task RemoveSetAsync()
    {
        if (_active == null || SelectedSessionSet == null) return;
        SetActive(await _api.DeleteSetAsync(_active.Id, SelectedSessionSet.Id));
        await RefreshAsync();
        StatusMessage = "Set removed from this session.";
    }
    private void SetActive(WorkoutResponse? workout)
    {
        _active = workout;
        Replace(SessionSets, workout?.Sets ?? []);
        if (workout != null) { WorkoutName = workout.Name; WorkoutDate = workout.Date.ToDateTime(TimeOnly.MinValue); }
        OnPropertyChanged(nameof(ActiveWorkout)); OnPropertyChanged(nameof(HasNoActiveWorkout)); OnPropertyChanged(nameof(SessionCaption)); RaiseCommands();
    }
    private void Resume()
    {
        if (SelectedHistory is not { IsCompleted: false } workout) return;
        SetActive(workout); Navigate("Workout");
    }
    private void CopyLoad()
    {
        if (SelectedHistorySet is not { } set) return;
        _loadingSnapshot = true; Unit = set.Unit; _loadingSnapshot = false;
        BarWeight = PlateLoad.Format(set.BarWeight); LeftStack = PlateLoad.FormatStack(set.LeftPlates);
        if (set.LeftPlates.Length == 0) LeftStack = "";
        RightStack = set.RightPlates.Length == 0 ? "" : PlateLoad.FormatStack(set.RightPlates);
        Mirror = set.LeftPlates.SequenceEqual(set.RightPlates);
        Reps = set.Reps.ToString(CultureInfo.InvariantCulture); Rpe = set.Rpe?.ToString(CultureInfo.InvariantCulture) ?? "";
        SelectedExercise = TrainingCatalog.FindExercise(set.ExerciseId); Kind = set.Kind;
        Navigate("Workout"); StatusMessage = "Previous plate setup loaded. Log a set when you are ready.";
    }
    private void RecalculateLoad()
    {
        try
        {
            if (!decimal.TryParse(BarWeight, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var bar)) throw new ArgumentException("Enter a valid bar weight.");
            _load = PlateLoad.Parse(bar, LeftStack, RightStack, Mirror, Unit);
            LoadError = "";
        }
        catch (ArgumentException ex) { _load = null; LoadError = ex.Message; }
        foreach (var property in new[] { nameof(LoadPreview), nameof(TotalLoadLabel), nameof(UnitSymbol), nameof(LoadFormula), nameof(BalanceLabel), nameof(HasImbalance) }) OnPropertyChanged(property);
        ValidateEntry();
    }
    private void ValidateEntry()
    {
        if (_load != null) LoadError = CanLog ? "" : "Use 1–100 reps, an optional RPE of 1–10, and notes up to 500 characters.";
        OnPropertyChanged(nameof(CanLog)); AddSetCommand?.RaiseCanExecuteChanged();
    }
    private async Task RunAsync(Func<Task> operation)
    {
        if (IsBusy) return;
        IsBusy = true; HasError = false;
        try { await operation(); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or ArgumentException)
        {
            HasError = true;
            StatusMessage = ex is HttpRequestException or TaskCanceledException ? "Could not reach your journal. Start the local service, then select Refresh." : ex.Message;
            if (ex is HttpRequestException or TaskCanceledException) ConnectionLabel = "Journal offline";
        }
        finally { IsBusy = false; }
    }
    private void Export()
    {
        var dialog = new SaveFileDialog { FileName = $"habittracker-{DateTime.Today:yyyy-MM-dd}.csv", Filter = "CSV files (*.csv)|*.csv" };
        if (dialog.ShowDialog() != true) return;
        try { File.WriteAllText(dialog.FileName, WorkoutCsvExporter.Export(_workouts), new System.Text.UTF8Encoding(true)); HasError = false; StatusMessage = $"Journal exported to {dialog.FileName}"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { HasError = true; StatusMessage = "Could not save the CSV. Choose a writable folder and try again."; }
    }
    public string TimerLabel => TimeSpan.FromSeconds(_remaining).ToString(@"mm\:ss");
    public string TimerButtonLabel => _deadline.HasValue ? "Pause" : "Start";
    public string TimerCaption => _remaining == 0 ? "Ready for your next set." : "Take a breath. Keep your form.";
    private void ToggleTimer()
    {
        if (_deadline.HasValue) { _remaining = Math.Max(0, (int)Math.Ceiling((_deadline.Value - DateTimeOffset.UtcNow).TotalSeconds)); _deadline = null; }
        else { if (_remaining == 0) _remaining = 90; _deadline = DateTimeOffset.UtcNow.AddSeconds(_remaining); }
        NotifyTimer();
    }
    private void OnTick(object? sender, EventArgs args)
    {
        if (!_deadline.HasValue) return;
        _remaining = Math.Max(0, (int)Math.Ceiling((_deadline.Value - DateTimeOffset.UtcNow).TotalSeconds));
        if (_remaining == 0) _deadline = null;
        NotifyTimer();
    }
    private void NotifyTimer() { OnPropertyChanged(nameof(TimerLabel)); OnPropertyChanged(nameof(TimerButtonLabel)); OnPropertyChanged(nameof(TimerCaption)); }
    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items) { collection.Clear(); foreach (var item in items) collection.Add(item); }
    private void RaiseCommands()
    {
        RefreshCommand?.RaiseCanExecuteChanged(); AddSetCommand?.RaiseCanExecuteChanged(); FinishCommand?.RaiseCanExecuteChanged(); RemoveSetCommand?.RaiseCanExecuteChanged();
        ExportCommand?.RaiseCanExecuteChanged(); CopyLoadCommand?.RaiseCanExecuteChanged(); ResumeCommand?.RaiseCanExecuteChanged();
    }
    public void Dispose() { _timer.Stop(); _timer.Tick -= OnTick; }
}
