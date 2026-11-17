using System.Collections.ObjectModel;
using System.Globalization;

namespace HabitTracker.Mobile.Core;

public sealed record HabitLogTile(HabitLogRecord Log, string Unit, MobileAsyncCommand RemoveCommand)
{
    public string DateLabel => Log.Date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    public string QuantityLabel => Log.Quantity.ToString("0.##", CultureInfo.InvariantCulture) + " " + Unit;
}

public sealed partial class MobileWorkspace
{
    private HabitRecord? _selectedHabit, _deleteHabitTarget;
    private string _habitName = "", _habitUnit = "times", _habitDescription = "", _habitQuantity = "", _habitComment = "";
    private DateTime? _habitDate;
    private bool _selectingHabit;
    public ObservableCollection<HabitRecord> Habits { get; } = [];
    public ObservableCollection<HabitLogTile> HabitLogs { get; } = [];
    public MobileCommand NewHabitCommand { get; private set; } = null!;
    public MobileAsyncCommand SaveHabitCommand { get; private set; } = null!;
    public MobileCommand DeleteHabitCommand { get; private set; } = null!;
    public MobileAsyncCommand ConfirmDeleteHabitCommand { get; private set; } = null!;
    public MobileCommand CancelDeleteHabitCommand { get; private set; } = null!;
    public MobileAsyncCommand LogHabitCommand { get; private set; } = null!;
    public bool HasHabits => Habits.Count > 0;
    public bool HasSelectedHabit => _selectedHabit != null;
    public bool HasDeleteHabitConfirmation => _deleteHabitTarget != null;
    public string DeleteHabitPrompt => "Delete “" + _deleteHabitTarget?.Name + "” and all its daily records? This cannot be undone.";
    public HabitRecord? SelectedHabit
    {
        get => _selectedHabit;
        set
        {
            if (!Set(ref _selectedHabit, value)) return;
            CopyHabitFields();
            if (!_selectingHabit && _handle != null) _ = RunAsync(LoadHabitLogsAsync);
        }
    }
    public string HabitName { get => _habitName; set { Set(ref _habitName, value); RefreshHabitCommands(); } }
    public string HabitUnit { get => _habitUnit; set { Set(ref _habitUnit, value); RefreshHabitCommands(); } }
    public string HabitDescription { get => _habitDescription; set => Set(ref _habitDescription, value); }
    public string HabitQuantity { get => _habitQuantity; set { Set(ref _habitQuantity, value); RefreshHabitCommands(); } }
    public string HabitComment { get => _habitComment; set => Set(ref _habitComment, value); }
    public DateTime? HabitDate { get => _habitDate ?? Today.ToDateTime(TimeOnly.MinValue); set => Set(ref _habitDate, value); }
    public string SaveHabitLabel => SelectedHabit == null ? "Create habit" : "Save habit";
    public string HabitLogHint => "One entry per date. Saving the same date updates its quantity.";

    private void InitializeHabitCommands()
    {
        NewHabitCommand = new(_ =>
        {
            _selectingHabit = true; SelectedHabit = null; _selectingHabit = false;
            HabitName = ""; HabitUnit = "times"; HabitDescription = ""; HabitLogs.Clear();
            Notify(nameof(SaveHabitLabel));
        }, () => !IsBusy);
        SaveHabitCommand = new(_ => RunAsync(async () =>
        {
            if (_handle == null) return;
            var saved = await _handle.Journal.SaveHabitAsync(SelectedHabit?.Id, new(HabitName, HabitUnit, HabitDescription));
            await ReloadHabitsAsync(saved.Id);
            Status = "Habit saved in this journal.";
        }), () => !IsBusy && _handle != null && !string.IsNullOrWhiteSpace(HabitName) && !string.IsNullOrWhiteSpace(HabitUnit));
        DeleteHabitCommand = new(_ =>
        {
            _deleteHabitTarget = SelectedHabit;
            Notify(nameof(HasDeleteHabitConfirmation)); Notify(nameof(DeleteHabitPrompt));
            ConfirmDeleteHabitCommand.Refresh();
        }, () => !IsBusy && SelectedHabit != null);
        CancelDeleteHabitCommand = new(_ => { _deleteHabitTarget = null; Notify(nameof(HasDeleteHabitConfirmation)); }, () => !IsBusy);
        ConfirmDeleteHabitCommand = new(_ => RunAsync(async () =>
        {
            if (_handle == null || _deleteHabitTarget == null) return;
            await _handle.Journal.DeleteHabitAsync(_deleteHabitTarget.Id);
            _deleteHabitTarget = null; Notify(nameof(HasDeleteHabitConfirmation));
            await ReloadHabitsAsync();
            Status = "Habit and its daily records removed.";
        }), () => !IsBusy && _deleteHabitTarget != null);
        LogHabitCommand = new(_ => RunAsync(async () =>
        {
            if (_handle == null || SelectedHabit == null || !HabitDate.HasValue) return;
            if (!decimal.TryParse(HabitQuantity, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity) || quantity < 0)
                throw new ArgumentException("Enter a nonnegative quantity using a decimal point.");
            await _handle.Journal.SaveHabitLogAsync(new(SelectedHabit.Id, DateOnly.FromDateTime(HabitDate.Value), quantity, HabitComment));
            await LoadHabitLogsAsync();
            Status = "Daily habit record saved.";
        }), () => !IsBusy && _handle != null && SelectedHabit != null && !string.IsNullOrWhiteSpace(HabitQuantity));
    }

    private Task LoadHabitsAsync() => ReloadHabitsAsync(SelectedHabit?.Id);
    private async Task ReloadHabitsAsync(int? selectedId = null)
    {
        if (_handle == null) return;
        var rows = await _handle.Journal.ListHabitsAsync();
        _selectingHabit = true;
        Replace(Habits, rows);
        _selectedHabit = rows.FirstOrDefault(h => h.Id == selectedId) ?? rows.FirstOrDefault();
        Notify(nameof(SelectedHabit));
        CopyHabitFields();
        _selectingHabit = false;
        Notify(nameof(HasHabits));
        await LoadHabitLogsAsync();
    }
    private async Task LoadHabitLogsAsync()
    {
        HabitLogs.Clear();
        if (_handle == null || SelectedHabit == null) return;
        var selected = SelectedHabit;
        var logs = await _handle.Journal.ListHabitLogsAsync(selected.Id);
        if (SelectedHabit?.Id != selected.Id) return;
        Replace(HabitLogs, logs.Select(l => new HabitLogTile(l, selected.Unit, new MobileAsyncCommand(_ => RunAsync(async () =>
        {
            await _handle.Journal.DeleteHabitLogAsync(l.Id);
            await LoadHabitLogsAsync(); Status = "Daily record removed.";
        }), () => !IsBusy))));
    }
    private void CopyHabitFields()
    {
        HabitName = _selectedHabit?.Name ?? "";
        HabitUnit = _selectedHabit?.Unit ?? "times";
        HabitDescription = _selectedHabit?.Description ?? "";
        HabitQuantity = ""; HabitComment = "";
        Notify(nameof(HasSelectedHabit)); Notify(nameof(SaveHabitLabel));
        RefreshHabitCommands();
    }
    private void RefreshHabitCommands()
    {
        NewHabitCommand?.Refresh(); SaveHabitCommand?.Refresh(); DeleteHabitCommand?.Refresh(); ConfirmDeleteHabitCommand?.Refresh(); CancelDeleteHabitCommand?.Refresh(); LogHabitCommand?.Refresh();
    }
}
