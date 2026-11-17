using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HabitTracker.Core;

namespace HabitTracker.Mobile.Core;

/// <summary>UI-independent mobile state; OpenSilver only binds to this model and supplies device adapters.</summary>
public sealed partial class MobileWorkspace : ObservableModel, IDisposable
{
    private readonly IJournalFactory _factory;
    private readonly JsonStateStore _state;
    private readonly TimeProvider _clock;
    private readonly Func<string, string, Task> _export;
    private readonly RestTimer _timer;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private JournalHandle? _handle;
    private DevicePreferences _preferences = new();
    private WorkoutDraft _draft = new();
    private List<WorkoutResponse> _workouts = [];
    private WorkoutResponse? _active;
    private LogSetRequest? _pendingSet;
    private MobilePage _page;
    private string _status = "Opening your journal…", _search = "", _apiAddress = "http://localhost:5253/";
    private bool _busy, _error, _initialized, _disposed, _details;
    private int _weeklyGoal = 4;
    private JournalMode _selectedMode;
    private CancellationTokenSource? _draftSave;
    private Task _draftSaveTask = Task.CompletedTask;

    public MobileWorkspace(IJournalFactory factory, IJournalStore store, TimeProvider clock, Func<string, string, Task> export)
    {
        _factory = factory; _state = new(store); _clock = clock; _export = export; _timer = new(clock);
        NavigateCommand = new(p => Navigate(Enum.TryParse<MobilePage>(p?.ToString(), out var page) ? page : MobilePage.Home));
        StartWorkoutCommand = new(p => StartRoutine(p?.ToString()));
        ToggleDetailsCommand = new(_ => ShowDetails = !ShowDetails);
        RefreshCommand = new(_ => RunAsync(RefreshAsync), () => !IsBusy && _handle != null);
        ApplySettingsCommand = new(_ => RunAsync(ApplySettingsAsync), () => !IsBusy);
        UseDemoCommand = new(_ => RunAsync(async () => { SelectedMode = JournalMode.Demo; await ApplySettingsAsync(); Navigate(MobilePage.Home); }), () => !IsBusy);
        LogSetCommand = new(_ => RunAsync(LogSetAsync), () => !IsBusy && _handle != null && (CanLog || HasPendingSave));
        FinishCommand = new(_ => RunAsync(FinishAsync), () => !IsBusy && _active?.Sets.Count > 0 && !HasPendingSave);
        ExportCommand = new(_ => RunAsync(ExportAsync), () => !IsBusy && _workouts.Any(w => w.Sets.Count > 0));
        AddLeftPlateCommand = new(p => AddPlate(p, true), () => CanEditDraft);
        AddRightPlateCommand = new(p => AddPlate(p, false), () => CanEditDraft);
        ClearPlatesCommand = new(_ => { Left = ""; Right = ""; }, () => CanEditDraft);
        SelectUnitCommand = new(p => Unit = p?.ToString() == "kg" ? WeightUnit.Kilograms : WeightUnit.Pounds, () => CanEditDraft);
        TimerToggleCommand = new(_ => RunAsync(async () => { if (_timer.IsRunning) _timer.Pause(); else _timer.Resume(); await SaveWorkspaceAsync(); NotifyTimer(); }), () => !IsBusy && _handle != null);
        TimerAddCommand = new(_ => RunAsync(async () => { _timer.AddSeconds(); await SaveWorkspaceAsync(); NotifyTimer(); }), () => !IsBusy && _handle != null);
        TimerResetCommand = new(_ => RunAsync(async () => { _timer.Reset(); await SaveWorkspaceAsync(); NotifyTimer(); }), () => !IsBusy && _handle != null);
        Routines = TrainingCatalog.Routines.Select(r => new RoutineTile(r, new MobileCommand(_ => StartRoutine(r.Id)))).ToArray();
        InitializeHabitCommands();
        RebuildPreview();
    }

    public MobileCommand NavigateCommand { get; }
    public MobileCommand StartWorkoutCommand { get; }
    public MobileCommand ToggleDetailsCommand { get; }
    public MobileAsyncCommand RefreshCommand { get; }
    public MobileAsyncCommand ApplySettingsCommand { get; }
    public MobileAsyncCommand UseDemoCommand { get; }
    public MobileAsyncCommand LogSetCommand { get; }
    public MobileAsyncCommand FinishCommand { get; }
    public MobileAsyncCommand ExportCommand { get; }
    public MobileCommand AddLeftPlateCommand { get; }
    public MobileCommand AddRightPlateCommand { get; }
    public MobileCommand ClearPlatesCommand { get; }
    public MobileCommand SelectUnitCommand { get; }
    public MobileAsyncCommand TimerToggleCommand { get; }
    public MobileAsyncCommand TimerAddCommand { get; }
    public MobileAsyncCommand TimerResetCommand { get; }
    public IReadOnlyList<RoutineTile> Routines { get; }
    public IReadOnlyList<Routine> RoutineOptions => TrainingCatalog.Routines;
    public IReadOnlyList<Exercise> ExerciseOptions => TrainingCatalog.Exercises;
    public SetKind[] SetKinds { get; } = Enum.GetValues<SetKind>();
    public int[] GoalOptions { get; } = Enumerable.Range(1, 7).ToArray();
    public ModeOption[] ModeOptions { get; } = [new(JournalMode.Local, "Local · this device"), new(JournalMode.Demo, "Demo · sample data"), new(JournalMode.Connected, "Connected · existing API")];
    public ObservableCollection<PlateChip> LeftChips { get; } = [];
    public ObservableCollection<PlateChip> RightChips { get; } = [];
    public ObservableCollection<WorkoutTile> RecentWorkouts { get; } = [];
    public ObservableCollection<WorkoutTile> History { get; } = [];
    public ObservableCollection<SetTile> SessionSets { get; } = [];
    public ObservableCollection<PersonalRecord> Records { get; } = [];
    public ObservableCollection<MuscleSummary> Muscles { get; } = [];
    public ObservableCollection<DayChip> Days { get; } = [];
    public ObservableCollection<ChartBar> Chart { get; } = [];
    public DateOnly Today => DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
    public string DateCaption => _clock.GetLocalNow().ToString("dddd, MMMM d", CultureInfo.InvariantCulture);
    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (!Set(ref _busy, value)) return;
            foreach (var name in new[] { nameof(IsReady), nameof(CanEditDraft), nameof(CanEditRight), nameof(CanEditSession), nameof(HasPendingSave), nameof(LogButtonLabel), nameof(HasActiveWorkout), nameof(SessionSummary) }) Notify(name);
            RefreshCommands();
        }
    }
    public bool IsReady => _initialized && !IsBusy;
    public bool HasError { get => _error; private set => Set(ref _error, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public MobilePage Page => _page;
    public bool IsHome => _page == MobilePage.Home;
    public bool IsWorkout => _page == MobilePage.Workout;
    public bool IsHistory => _page == MobilePage.History;
    public bool IsProgress => _page == MobilePage.Progress;
    public bool IsSettings => _page == MobilePage.Settings;
    public bool IsHabits => _page == MobilePage.Habits;
    public bool IsMainTab => IsHome || IsWorkout || IsHistory || IsProgress;
    public string SourceLabel => (_handle?.Mode ?? _preferences.Mode) switch { JournalMode.Demo => "DEMO JOURNAL", JournalMode.Connected => "CONNECTED JOURNAL", _ => "LOCAL JOURNAL" };
    public string SourceDetail => (_handle?.Mode ?? _preferences.Mode) switch { JournalMode.Demo => "Sample data · kept separate from your personal records", JournalMode.Connected => "Saved by your configured API · separate from this device's local journal", _ => "Saved on this device · works without a desktop service" };
    public bool IsDemo => _handle?.Mode == JournalMode.Demo;
    public JournalMode SelectedMode { get => _selectedMode; set { if (Set(ref _selectedMode, value)) Notify(nameof(IsConnectedSelected)); } }
    public bool IsConnectedSelected => SelectedMode == JournalMode.Connected;
    public string ApiAddress { get => _apiAddress; set => Set(ref _apiAddress, value); }
    public int WeeklyGoal
    {
        get => _weeklyGoal;
        set
        {
            if (value is < 1 or > 7 || !Set(ref _weeklyGoal, value)) return;
            _preferences = _preferences with { WeeklyGoal = value };
            NotifyStats();
            if (_initialized) _ = SavePreferencesSafelyAsync();
        }
    }
    public string Search { get => _search; set { if (Set(ref _search, value)) RebuildHistory(); } }
    public bool ShowDetails { get => _details; private set => Set(ref _details, value); }
    public bool HasActiveWorkout => _active != null;
    public bool CanEditSession => !IsBusy && _active == null && !HasPendingSave;
    public bool CanEditDraft => !IsBusy && !HasPendingSave;
    public bool HasPendingSave => _pendingSet != null;
    public bool HasSessionSets => SessionSets.Count > 0;
    public bool HasWorkouts => _workouts.Count > 0;
    public bool HasNoWorkouts => !HasWorkouts;
    public bool HasHistory => History.Count > 0;
    public bool HasRecords => Records.Count > 0;
    public string SessionSummary => _active == null ? "A fresh session. Make the first set count." : $"{_active.SetCount} sets saved · {_active.VolumeLabel} working volume";
    public string LogButtonLabel => HasPendingSave ? "Retry pending save" : "＋  Log set";
    public string Sessions28 => Completed.Where(w => w.Date >= Today.AddDays(-27)).Count().ToString(CultureInfo.InvariantCulture);
    public string Volume28 => Compact(Completed.Where(w => w.Date >= Today.AddDays(-27)).Sum(w => w.VolumePounds));
    public string VolumeThisWeek => _volumePoints.LastOrDefault()?.Volume.ToString("N0", CultureInfo.InvariantCulture) ?? "0";
    public string Heaviest => Records.FirstOrDefault()?.WeightLabel ?? "—";
    public int WeeklySessions => Completed.Count(w => w.Date >= TrainingAnalytics.StartOfWeek(Today));
    public string GoalLabel => WeeklySessions + " / " + WeeklyGoal;
    public double GoalFraction => Math.Min(1, (double)WeeklySessions / WeeklyGoal);
    public string GoalMessage => WeeklySessions >= WeeklyGoal ? "Weekly goal reached. Make room for recovery." : Math.Max(0, WeeklyGoal - WeeklySessions) + " more sessions to your weekly goal.";
    private IEnumerable<WorkoutResponse> Completed => _workouts.Where(w => w.IsCompleted && w.Date <= Today);
    private IReadOnlyList<VolumePoint> _volumePoints = [];
    public string TimerLabel => TimeSpan.FromSeconds(_timer.RemainingSeconds).ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    public string TimerAction => _timer.IsRunning ? "Pause" : "Start";
    public string TimerCaption => _timer.RemainingSeconds == 0 ? "Ready for your next set." : "Take a breath. Keep your form.";

    public Task InitializeAsync(bool forceDemo = false) => RunAsync(async () =>
    {
        _preferences = await _state.ReadAsync("device/preferences/v1", JournalJsonContext.Default.DevicePreferences) ?? new();
        _weeklyGoal = Math.Clamp(_preferences.WeeklyGoal, 1, 7);
        Notify(nameof(WeeklyGoal));
        ApiAddress = _preferences.ApiBaseUrl;
        SelectedMode = forceDemo ? JournalMode.Demo : _preferences.Mode;
        await OpenJournalAsync(SelectedMode, ApiAddress, savePreference: false);
    });

    public void Navigate(MobilePage page)
    {
        _page = page;
        foreach (var name in new[] { nameof(Page), nameof(IsHome), nameof(IsWorkout), nameof(IsHistory), nameof(IsProgress), nameof(IsSettings), nameof(IsHabits), nameof(IsMainTab) }) Notify(name);
        if (page == MobilePage.Habits && _handle != null) _ = RunAsync(LoadHabitsAsync);
    }

    private async Task ApplySettingsAsync()
    {
        await FlushAsync();
        await OpenJournalAsync(SelectedMode, ApiAddress, savePreference: true);
    }

    private async Task OpenJournalAsync(JournalMode mode, string address, bool savePreference)
    {
        var candidate = await _factory.OpenAsync(mode, address);
        try
        {
            var rows = await candidate.Journal.ListAsync();
            var saved = await _state.ReadAsync(candidate.WorkspaceKey, JournalJsonContext.Default.WorkspaceSnapshot);
            _draftSave?.Cancel();
            var old = _handle;
            _handle = candidate;
            old?.Dispose();
            _workouts = rows.ToList();
            _draft = saved?.Draft ?? new WorkoutDraft { Date = Today };
            _pendingSet = saved?.PendingSet;
            _timer.Restore(saved?.Timer ?? new());
            _active = rows.FirstOrDefault(w => w.Id == saved?.ActiveWorkoutId && !w.IsCompleted) ?? rows.FirstOrDefault(w => !w.IsCompleted);
            RecoverPendingSave();
            _initialized = true;
            _preferences = _preferences with { Mode = mode, ApiBaseUrl = mode == JournalMode.Connected ? ApiWorkoutJournal.NormalizeAddress(address).AbsoluteUri : _preferences.ApiBaseUrl, WeeklyGoal = WeeklyGoal };
            if (savePreference) await _state.SaveAsync("device/preferences/v1", _preferences, JournalJsonContext.Default.DevicePreferences);
            NotifyDraft(); RebuildPresentation();
            Notify(nameof(SourceLabel)); Notify(nameof(SourceDetail)); Notify(nameof(IsDemo)); Notify(nameof(IsReady));
            Status = mode == JournalMode.Demo ? "Demo journal ready. Changes stay with your sample data." : mode == JournalMode.Local ? "Local journal ready. Your next set starts here." : "Connected to your journal service.";
            await SaveWorkspaceAsync();
        }
        catch { if (!ReferenceEquals(_handle, candidate)) candidate.Dispose(); throw; }
    }

    private async Task RefreshAsync()
    {
        if (_handle == null) return;
        _workouts = (await _handle.Journal.ListAsync()).ToList();
        _active = _workouts.FirstOrDefault(w => w.Id == _active?.Id && !w.IsCompleted) ?? _workouts.FirstOrDefault(w => !w.IsCompleted);
        RecoverPendingSave();
        RebuildPresentation();
        await SaveWorkspaceAsync();
        Status = IsDemo ? "Demo journal is up to date." : "Your journal is up to date.";
    }

    private void RecoverPendingSave()
    {
        if (_pendingSet != null && _workouts.SelectMany(w => w.Sets).Any(s => s.ClientId == _pendingSet.ClientId)) _pendingSet = null;
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy || _disposed) return;
        IsBusy = true; HasError = false;
        try { await action(); }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException or ArgumentException or InvalidOperationException or JsonException)
        {
            HasError = true;
            Status = ex is HttpRequestException or TaskCanceledException
                ? "Could not reach the journal. Refresh to check an uncertain save, or retry the same pending set."
                : ex.Message;
            if (!_initialized) Navigate(MobilePage.Settings);
        }
        finally { IsBusy = false; }
    }

    private WorkspaceSnapshot Snapshot() => new() { ActiveWorkoutId = _active?.Id, Draft = _draft, Timer = _timer.Snapshot, PendingSet = _pendingSet };
    private async Task SaveWorkspaceAsync()
    {
        if (_handle == null || _disposed) return;
        var key = _handle.WorkspaceKey;
        await _stateGate.WaitAsync();
        try { if (_handle?.WorkspaceKey == key) await _state.SaveAsync(key, Snapshot(), JournalJsonContext.Default.WorkspaceSnapshot); }
        finally { _stateGate.Release(); }
    }
    public async Task FlushAsync()
    {
        _draftSave?.Cancel();
        await _draftSaveTask;
        if (_handle != null) await SaveWorkspaceAsync();
    }
    private void ScheduleDraftSave()
    {
        if (!_initialized || _disposed) return;
        _draftSave?.Cancel(); _draftSave?.Dispose();
        _draftSave = new CancellationTokenSource();
        _draftSaveTask = SaveDraftLaterAsync(_draftSave.Token);
    }
    private async Task SaveDraftLaterAsync(CancellationToken ct)
    {
        try { await Task.Delay(300, ct); ct.ThrowIfCancellationRequested(); await SaveWorkspaceAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        { HasError = true; Status = "Draft could not be saved: " + ex.Message; }
    }
    private async Task SavePreferencesSafelyAsync()
    {
        try { await _state.SaveAsync("device/preferences/v1", _preferences, JournalJsonContext.Default.DevicePreferences); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        { HasError = true; Status = "Goal updated for this session; device settings could not be saved."; }
    }

    public async Task TickAsync()
    {
        if (_disposed) return;
        NotifyTimer();
        if (_timer.SettleExpired())
        {
            try { await SaveWorkspaceAsync(); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException) { HasError = true; Status = "Timer ended; its device state could not be saved."; }
        }
    }
    private void NotifyTimer() { Notify(nameof(TimerLabel)); Notify(nameof(TimerAction)); Notify(nameof(TimerCaption)); }
    private async Task ExportAsync()
    {
        await _export("form-workouts-" + Today.ToString("yyyy-MM-dd") + ".csv", WorkoutCsvExporter.Export(_workouts));
        Status = "CSV prepared with original units and both plate stacks.";
    }

    private void RebuildPresentation()
    {
        Replace(SessionSets, (_active?.Sets ?? []).Select(s => Tile(s, _active?.Id)));
        Replace(RecentWorkouts, _workouts.Take(3).Select(WorkoutCard));
        Replace(Records, TrainingAnalytics.PersonalRecords(Completed));
        _volumePoints = TrainingAnalytics.WeeklyVolume(_workouts, Today);
        var maximum = Math.Max(1, _volumePoints.Max(p => p.Volume));
        Replace(Chart, _volumePoints.Select((p, i) => new ChartBar(p.Label, Math.Max(2, (double)(p.Volume / maximum) * 104), i == _volumePoints.Count - 1, p.Volume.ToString("N0") + " lb")));
        var monday = TrainingAnalytics.StartOfWeek(Today);
        Replace(Days, Enumerable.Range(0, 7).Select(i => new DayChip(monday.AddDays(i).ToString("ddd", CultureInfo.InvariantCulture)[..1], Completed.Any(w => w.Date == monday.AddDays(i)))));
        var groups = Completed.Where(w => w.Date >= Today.AddDays(-27)).SelectMany(w => w.Sets).Where(s => s.Kind == SetKind.Working).GroupBy(s => s.Muscle).OrderByDescending(g => g.Count()).ToArray();
        var count = groups.Sum(g => g.Count());
        Replace(Muscles, groups.Select(g => new MuscleSummary(g.Key, g.Count(), count == 0 ? 0 : (double)g.Count() / count)));
        RebuildHistory();
        foreach (var name in new[] { nameof(HasActiveWorkout), nameof(CanEditSession), nameof(CanEditDraft), nameof(HasPendingSave), nameof(HasSessionSets), nameof(HasWorkouts), nameof(HasNoWorkouts), nameof(HasRecords), nameof(SessionSummary), nameof(LogButtonLabel) }) Notify(name);
        NotifyStats(); NotifyTimer(); RefreshCommands();
    }
    private void RebuildHistory()
    {
        Replace(History, _workouts.Where(w => string.IsNullOrWhiteSpace(Search) || w.Name.Contains(Search, StringComparison.OrdinalIgnoreCase)
            || w.Sets.Any(s => s.ExerciseName.Contains(Search, StringComparison.OrdinalIgnoreCase))).Select(WorkoutCard));
        Notify(nameof(HasHistory));
    }
    private WorkoutTile WorkoutCard(WorkoutResponse workout) => new(workout, workout.Sets.Select(s => Tile(s, null)).ToArray(), () => Resume(workout));
    private SetTile Tile(WorkoutSetResponse set, int? activeId) => new(set,
        new MobileCommand(_ => Reuse(set), () => !IsBusy && !HasPendingSave),
        new MobileAsyncCommand(_ => RunAsync(async () =>
        {
            if (_handle == null || _active == null) return;
            _active = await _handle.Journal.RemoveSetAsync(_active.Id, set.Id);
            await RefreshAsync(); Status = "Set removed from this session.";
        }), () => !IsBusy && activeId.HasValue && !HasPendingSave),
        activeId.HasValue);

    private void Resume(WorkoutResponse workout)
    {
        if (workout.IsCompleted || IsBusy || HasPendingSave) return;
        _active = workout; _draft = _draft with { Name = workout.Name, Date = workout.Date };
        NotifyDraft(); RebuildPresentation(); Navigate(MobilePage.Workout); ScheduleDraftSave();
    }
    private void NotifyStats()
    {
        foreach (var name in new[] { nameof(Sessions28), nameof(Volume28), nameof(VolumeThisWeek), nameof(Heaviest), nameof(WeeklySessions), nameof(GoalLabel), nameof(GoalFraction), nameof(GoalMessage) }) Notify(name);
    }
    private void RefreshCommands()
    {
        foreach (var command in new[] { RefreshCommand, ApplySettingsCommand, UseDemoCommand, LogSetCommand, FinishCommand, ExportCommand, TimerToggleCommand, TimerAddCommand, TimerResetCommand }) command?.Refresh();
        foreach (var command in new[] { AddLeftPlateCommand, AddRightPlateCommand, ClearPlatesCommand, SelectUnitCommand }) command?.Refresh();
        RefreshHabitCommands();
    }
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (var value in values) target.Add(value); }
    private static string Compact(decimal value) => value < 1000 ? value.ToString("N0", CultureInfo.InvariantCulture) : (value / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "k";
    public void Dispose() { _disposed = true; _draftSave?.Cancel(); _draftSave?.Dispose(); _handle?.Dispose(); }
}
