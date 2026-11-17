using System.Globalization;
using System.Runtime.CompilerServices;
using HabitTracker.Core;

namespace HabitTracker.Mobile.Core;

public sealed partial class MobileWorkspace
{
    private PlateLoad? _load;
    private string _validation = "";
    public PlateLoad? Load => _load;
    public string TotalLoad => _load == null ? "—" : PlateLoad.Format(_load.TotalWeight);
    public string UnitLabel => Unit == WeightUnit.Pounds ? "lb" : "kg";
    public string LoadFormula => _load?.Formula ?? "Enter a valid bar and plate setup.";
    public bool IsBalanced => _load is { IsBalanced: true };
    public bool IsUneven => _load is { IsBalanced: false };
    public string BalanceLabel => _load == null ? "Check your plate entries" : _load.IsBalanced ? "Balanced · both sides match" : "Uneven sides · check your setup";
    public string EntryValidation => _validation;
    public bool HasValidation => _validation.Length > 0;
    public bool IsPounds => Unit == WeightUnit.Pounds;
    public bool IsKilograms => Unit == WeightUnit.Kilograms;
    public bool CanEditRight => CanEditDraft && !Mirror;
    public IReadOnlyList<PlateOption> QuickPlates => (Unit == WeightUnit.Pounds ? new decimal[] { 45, 25, 10, 5, 2.5m } : [20, 10, 5, 2.5m, 1.25m]).Select(p => new PlateOption(p)).ToArray();
    public string WorkoutName { get => _draft.Name; set => ChangeDraft(_draft with { Name = value ?? "" }); }
    public DateTime? WorkoutDate { get => _draft.Date.ToDateTime(TimeOnly.MinValue); set { if (value.HasValue) ChangeDraft(_draft with { Date = DateOnly.FromDateTime(value.Value) }); } }
    public string Bar { get => _draft.Bar; set => ChangeDraft(_draft with { Bar = value ?? "" }); }
    public string Left
    {
        get => _draft.Left;
        set
        {
            var expression = value ?? "";
            ChangeDraft(_draft with { Left = expression, Right = Mirror ? expression : _draft.Right });
            if (Mirror) Notify(nameof(Right));
        }
    }
    public string Right { get => _draft.Right; set => ChangeDraft(_draft with { Right = value ?? "" }); }
    public bool Mirror
    {
        get => _draft.Mirror;
        set
        {
            ChangeDraft(_draft with { Mirror = value, Right = value ? Left : _draft.Right });
            Notify(nameof(Right)); Notify(nameof(CanEditRight));
        }
    }
    public string Reps { get => _draft.Reps; set => ChangeDraft(_draft with { Reps = value ?? "" }); }
    public string Rpe { get => _draft.Rpe; set => ChangeDraft(_draft with { Rpe = value ?? "" }); }
    public string SetNotes { get => _draft.Notes; set => ChangeDraft(_draft with { Notes = value ?? "" }); }
    public SetKind Kind { get => _draft.Kind; set => ChangeDraft(_draft with { Kind = value }); }
    public Routine SelectedRoutine
    {
        get => TrainingCatalog.Routines.FirstOrDefault(r => r.Id == _draft.RoutineId) ?? TrainingCatalog.Routines[0];
        set
        {
            if (value == null || _active != null) return;
            _draft = _draft with { RoutineId = value.Id, Name = value.Name, ExerciseId = value.ExerciseIds[0] };
            NotifyDraft(); ScheduleDraftSave();
        }
    }
    public Exercise SelectedExercise
    {
        get => TrainingCatalog.Exercises.FirstOrDefault(e => e.Id == _draft.ExerciseId) ?? TrainingCatalog.Exercises[0];
        set { if (value != null) ChangeDraft(_draft with { ExerciseId = value.Id }); }
    }
    public string RoutineHint => string.Join(" · ", SelectedRoutine.ExerciseIds.Select(id => TrainingCatalog.FindExercise(id).Name));
    public WeightUnit Unit
    {
        get => _draft.Unit;
        set
        {
            if (value == _draft.Unit || !Enum.IsDefined(value)) return;
            _draft = _draft with { Unit = value, Bar = value == WeightUnit.Pounds ? "45" : "20",
                Left = value == WeightUnit.Pounds ? "45 + 25 + 10" : "20 + 5 + 1.25",
                Right = value == WeightUnit.Pounds ? "45 + 25 + 10" : "20 + 5 + 1.25" };
            NotifyDraft(); ScheduleDraftSave();
        }
    }

    public bool CanLog => _load != null && (_active != null || !string.IsNullOrWhiteSpace(WorkoutName) && WorkoutName.Trim().Length <= 100)
        && int.TryParse(Reps, NumberStyles.Integer, CultureInfo.InvariantCulture, out var reps) && reps is >= 1 and <= 100
        && (string.IsNullOrWhiteSpace(Rpe) || decimal.TryParse(Rpe, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rpe) && rpe is >= 1 and <= 10)
        && SetNotes.Length <= 500;

    private void ChangeDraft(WorkoutDraft value, [CallerMemberName] string? property = null)
    {
        if (_draft == value) return;
        _draft = value; Notify(property); RebuildPreview(); ScheduleDraftSave();
    }

    private void NotifyDraft()
    {
        foreach (var property in new[] { nameof(WorkoutName), nameof(WorkoutDate), nameof(SelectedRoutine), nameof(SelectedExercise), nameof(Bar), nameof(Left), nameof(Right), nameof(Mirror), nameof(Reps), nameof(Rpe), nameof(Kind), nameof(SetNotes), nameof(Unit), nameof(RoutineHint) }) Notify(property);
        RebuildPreview();
    }

    private void RebuildPreview()
    {
        try
        {
            if (!decimal.TryParse(Bar, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var bar)) throw new ArgumentException("Enter a bar weight using a decimal point.");
            _load = PlateLoad.Parse(bar, Left, Right, Mirror, Unit);
            _validation = CanLog ? "" : "Use a session name, 1–100 reps, optional RPE 1–10, and notes up to 500 characters.";
        }
        catch (ArgumentException ex) { _load = null; _validation = ex.Message; }
        Replace(LeftChips, (_load?.LeftPlates ?? []).Select((p, i) => new PlateChip(p, i, new MobileCommand(_ => RemovePlate(i, true), () => CanEditDraft))));
        Replace(RightChips, (_load?.RightPlates ?? []).Select((p, i) => new PlateChip(p, i, new MobileCommand(_ => RemovePlate(i, false), () => CanEditRight))));
        foreach (var property in new[] { nameof(Load), nameof(TotalLoad), nameof(UnitLabel), nameof(LoadFormula), nameof(IsBalanced), nameof(IsUneven), nameof(BalanceLabel), nameof(EntryValidation), nameof(HasValidation), nameof(CanLog), nameof(IsPounds), nameof(IsKilograms), nameof(QuickPlates), nameof(CanEditRight), nameof(RoutineHint) }) Notify(property);
        RefreshCommands();
    }

    private void AddPlate(object? parameter, bool left)
    {
        if (!CanEditDraft || !left && Mirror) return;
        var value = parameter is PlateOption option ? option.Weight : parameter is decimal number ? number : decimal.TryParse(parameter?.ToString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        if (value <= 0) return;
        try
        {
            var existing = PlateLoad.ParseStack(left ? Left : Right, Unit);
            if (existing.Length >= 16) throw new ArgumentException("Use at most 16 plates per side.");
            var text = PlateLoad.FormatStack(existing.Append(value));
            if (left) Left = text; else Right = text;
        }
        catch (ArgumentException ex) { _validation = ex.Message; Notify(nameof(EntryValidation)); Notify(nameof(HasValidation)); }
    }

    private void RemovePlate(int index, bool left)
    {
        if (!CanEditDraft || !left && Mirror) return;
        var plates = PlateLoad.ParseStack(left ? Left : Right, Unit);
        if (index < 0 || index >= plates.Length) return;
        var remaining = plates.Where((_, i) => i != index).ToArray();
        var expression = remaining.Length == 0 ? "" : PlateLoad.FormatStack(remaining);
        if (left) Left = expression; else Right = expression;
    }

    private void StartRoutine(string? id)
    {
        if (IsBusy) return;
        if (_active == null && !HasPendingSave)
        {
            var routine = TrainingCatalog.Routines.FirstOrDefault(r => r.Id == id) ?? SelectedRoutine;
            _draft = _draft with { Name = routine.Name, Date = Today, RoutineId = routine.Id, ExerciseId = routine.ExerciseIds[0] };
            NotifyDraft(); ScheduleDraftSave();
        }
        Navigate(MobilePage.Workout);
    }

    private async Task LogSetAsync()
    {
        if (_handle == null || !CanLog && _pendingSet == null) return;
        if (_active == null)
        {
            _active = await _handle.Journal.CreateAsync(new CreateWorkoutRequest { Name = WorkoutName, Date = _draft.Date });
            await SaveWorkspaceAsync();
        }
        _pendingSet ??= new LogSetRequest
        {
            ExerciseId = SelectedExercise.Id, Unit = Unit, BarWeight = _load!.BarWeight,
            LeftPlates = _load.LeftPlates.ToArray(), RightPlates = _load.RightPlates.ToArray(),
            Reps = int.Parse(Reps, CultureInfo.InvariantCulture),
            Rpe = string.IsNullOrWhiteSpace(Rpe) ? null : decimal.Parse(Rpe, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture),
            Kind = Kind, Notes = SetNotes
        };
        await SaveWorkspaceAsync();
        try { _active = await _handle.Journal.AddSetAsync(_active.Id, _pendingSet); }
        catch (JournalConflictException)
        {
            var rows = await _handle.Journal.ListAsync();
            var saved = rows.FirstOrDefault(w => w.Sets.Any(s => s.ClientId == _pendingSet.ClientId));
            if (saved == null) throw;
            _active = saved;
        }
        _pendingSet = null;
        _draft = _draft with { Notes = "" };
        _timer.Start();
        await SaveWorkspaceAsync();
        await RefreshAsync();
        Notify(nameof(SetNotes));
        Status = "Set saved. Rest, then go again.";
    }

    private async Task FinishAsync()
    {
        if (_handle == null || _active == null || HasPendingSave) return;
        await _handle.Journal.CompleteAsync(_active.Id);
        _active = null; _timer.Reset();
        await SaveWorkspaceAsync();
        await RefreshAsync();
        Navigate(MobilePage.Home);
        Status = "Workout complete. Every set is in your journal.";
    }

    public void Reuse(WorkoutSetResponse set)
    {
        if (IsBusy || HasPendingSave) return;
        var exercise = TrainingCatalog.FindExercise(set.ExerciseId);
        _draft = _draft with
        {
            Name = _active?.Name ?? _draft.Name, Date = _active?.Date ?? Today,
            ExerciseId = exercise.Id, Unit = set.Unit, Bar = PlateLoad.Format(set.BarWeight),
            Left = set.LeftPlates.Length == 0 ? "" : PlateLoad.FormatStack(set.LeftPlates),
            Right = set.RightPlates.Length == 0 ? "" : PlateLoad.FormatStack(set.RightPlates),
            Mirror = set.LeftPlates.SequenceEqual(set.RightPlates),
            Reps = set.Reps.ToString(CultureInfo.InvariantCulture), Rpe = set.Rpe?.ToString(CultureInfo.InvariantCulture) ?? "",
            Kind = set.Kind, Notes = set.Notes ?? ""
        };
        NotifyDraft(); Navigate(MobilePage.Workout); ScheduleDraftSave();
        Status = "Previous plate setup loaded. Ready when you are.";
    }
}
