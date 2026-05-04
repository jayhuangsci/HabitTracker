using System.Collections.ObjectModel;
using System.Linq;
using HabitTracker.Models;
using HabitTracker.Services;

namespace HabitTracker.ViewModels;

public class HabitListViewModel : ViewModelBase
{
    private readonly ApiService _apiService;
    private bool _suppressSelectedHabitChanged;

    private string _newHabitName = string.Empty;
    private string _newHabitUnit = string.Empty;
    private string? _newHabitDescription;

    private string _editHabitName = string.Empty;
    private string _editHabitUnit = string.Empty;
    private string? _editHabitDescription;

    private DateTime _newLogDate = DateTime.Today;
    private decimal _newLogQuantity;
    private string? _newLogComment;

    private string _statusMessage = "Ready.";
    private bool _isBusy;
    private bool _isEditingSelectedHabit;
    private HabitDto? _selectedHabit;

    public HabitListViewModel(ApiService apiService)
    {
        _apiService = apiService;

        Habits = new ObservableCollection<HabitDto>();
        HabitLogs = new ObservableCollection<HabitLogDto>();

        LoadHabitsCommand = new AsyncRelayCommand(LoadHabitsAsync, () => !IsBusy);
        AddHabitCommand = new AsyncRelayCommand(AddHabitAsync, CanAddHabit);
        AddHabitLogCommand = new AsyncRelayCommand(AddHabitLogAsync, CanAddHabitLog);

        BeginEditSelectedHabitCommand = new AsyncRelayCommand(
            BeginEditSelectedHabitAsync,
            () => HasSelectedHabit && !IsBusy && !IsEditingSelectedHabit);

        SaveSelectedHabitCommand = new AsyncRelayCommand(
            SaveSelectedHabitAsync,
            CanSaveSelectedHabit);

        CancelEditSelectedHabitCommand = new AsyncRelayCommand(
            CancelEditSelectedHabitAsync,
            () => IsEditingSelectedHabit && !IsBusy);

        DeleteSelectedHabitCommand = new AsyncRelayCommand(
            DeleteSelectedHabitAsync,
            () => HasSelectedHabit && !IsBusy);
    }

    public ObservableCollection<HabitDto> Habits { get; }

    public ObservableCollection<HabitLogDto> HabitLogs { get; }

    public AsyncRelayCommand LoadHabitsCommand { get; }

    public AsyncRelayCommand AddHabitCommand { get; }

    public AsyncRelayCommand AddHabitLogCommand { get; }

    public AsyncRelayCommand BeginEditSelectedHabitCommand { get; }

    public AsyncRelayCommand SaveSelectedHabitCommand { get; }

    public AsyncRelayCommand CancelEditSelectedHabitCommand { get; }

    public AsyncRelayCommand DeleteSelectedHabitCommand { get; }

    public string NewHabitName
    {
        get => _newHabitName;
        set
        {
            if (SetProperty(ref _newHabitName, value))
            {
                AddHabitCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewHabitUnit
    {
        get => _newHabitUnit;
        set
        {
            if (SetProperty(ref _newHabitUnit, value))
            {
                AddHabitCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? NewHabitDescription
    {
        get => _newHabitDescription;
        set => SetProperty(ref _newHabitDescription, value);
    }

    public string EditHabitName
    {
        get => _editHabitName;
        set
        {
            if (SetProperty(ref _editHabitName, value))
            {
                SaveSelectedHabitCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string EditHabitUnit
    {
        get => _editHabitUnit;
        set
        {
            if (SetProperty(ref _editHabitUnit, value))
            {
                SaveSelectedHabitCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? EditHabitDescription
    {
        get => _editHabitDescription;
        set => SetProperty(ref _editHabitDescription, value);
    }

    public DateTime NewLogDate
    {
        get => _newLogDate;
        set
        {
            if (SetProperty(ref _newLogDate, value))
            {
                AddHabitLogCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public decimal NewLogQuantity
    {
        get => _newLogQuantity;
        set
        {
            if (SetProperty(ref _newLogQuantity, value))
            {
                AddHabitLogCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? NewLogComment
    {
        get => _newLogComment;
        set => SetProperty(ref _newLogComment, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public HabitDto? SelectedHabit
    {
        get => _selectedHabit;
        set
        {
            if (SetProperty(ref _selectedHabit, value))
            {
                CopySelectedHabitToEditFields();
                IsEditingSelectedHabit = false;

                OnPropertyChanged(nameof(HasSelectedHabit));
                OnPropertyChanged(nameof(HasNoSelectedHabit));
                OnPropertyChanged(nameof(IsViewingSelectedHabit));

                RaiseCommandStates();

                if (!_suppressSelectedHabitChanged)
                {
                    _ = LoadSelectedHabitLogsForSelectedHabitAsync();
                }
            }
        }
    }

    public bool HasSelectedHabit => SelectedHabit is not null;

    public bool HasNoSelectedHabit => !HasSelectedHabit;

    public bool IsViewingSelectedHabit => HasSelectedHabit && !IsEditingSelectedHabit;

    public bool IsEditingSelectedHabit
    {
        get => _isEditingSelectedHabit;
        private set
        {
            if (SetProperty(ref _isEditingSelectedHabit, value))
            {
                OnPropertyChanged(nameof(IsViewingSelectedHabit));
                RaiseCommandStates();
            }
        }
    }

    public async Task LoadHabitsAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Loading habits...";

            await ReloadHabitsAsync(SelectedHabit?.Id);

            StatusMessage = $"Loaded {Habits.Count} habits.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task AddHabitAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Creating habit...";

            var createdHabit = await _apiService.CreateHabitAsync(new CreateHabitRequest
            {
                Name = NewHabitName.Trim(),
                Unit = NewHabitUnit.Trim(),
                Description = string.IsNullOrWhiteSpace(NewHabitDescription)
                    ? null
                    : NewHabitDescription.Trim()
            });

            NewHabitName = string.Empty;
            NewHabitUnit = string.Empty;
            NewHabitDescription = null;

            await ReloadHabitsAsync(createdHabit.Id);

            StatusMessage = "Habit created.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Create failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task BeginEditSelectedHabitAsync()
    {
        if (SelectedHabit is null)
        {
            return Task.CompletedTask;
        }

        CopySelectedHabitToEditFields();
        IsEditingSelectedHabit = true;
        StatusMessage = $"Editing '{SelectedHabit.Name}'.";

        return Task.CompletedTask;
    }

    public Task CancelEditSelectedHabitAsync()
    {
        CopySelectedHabitToEditFields();
        IsEditingSelectedHabit = false;
        StatusMessage = "Edit cancelled.";

        return Task.CompletedTask;
    }

    public async Task SaveSelectedHabitAsync()
    {
        if (SelectedHabit is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Saving habit...";

            var updatedHabit = await _apiService.UpdateHabitAsync(SelectedHabit.Id, new UpdateHabitRequest
            {
                Name = EditHabitName.Trim(),
                Unit = EditHabitUnit.Trim(),
                Description = string.IsNullOrWhiteSpace(EditHabitDescription)
                    ? null
                    : EditHabitDescription.Trim()
            });

            IsEditingSelectedHabit = false;

            await ReloadHabitsAsync(updatedHabit.Id);

            StatusMessage = "Habit updated.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Update failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeleteSelectedHabitAsync()
    {
        if (SelectedHabit is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Deleting habit...";

            await _apiService.DeleteHabitAsync(SelectedHabit.Id);

            IsEditingSelectedHabit = false;
            await ReloadHabitsAsync();

            StatusMessage = "Habit deleted.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Delete failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task AddHabitLogAsync()
    {
        if (SelectedHabit is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Creating habit log...";

            await _apiService.CreateHabitLogAsync(new CreateHabitLogRequest
            {
                HabitId = SelectedHabit.Id,
                Date = DateOnly.FromDateTime(NewLogDate),
                Quantity = NewLogQuantity,
                Comment = string.IsNullOrWhiteSpace(NewLogComment)
                    ? null
                    : NewLogComment.Trim()
            });

            NewLogDate = DateTime.Today;
            NewLogQuantity = 0;
            NewLogComment = null;

            await ReloadSelectedHabitLogsAsync();

            StatusMessage = "Habit log created.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Create log failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanAddHabit()
    {
        return !IsBusy
            && !string.IsNullOrWhiteSpace(NewHabitName)
            && !string.IsNullOrWhiteSpace(NewHabitUnit);
    }

    private bool CanSaveSelectedHabit()
    {
        return HasSelectedHabit
            && IsEditingSelectedHabit
            && !IsBusy
            && !string.IsNullOrWhiteSpace(EditHabitName)
            && !string.IsNullOrWhiteSpace(EditHabitUnit);
    }

    private bool CanAddHabitLog()
    {
        return HasSelectedHabit
            && !IsBusy
            && NewLogQuantity >= 0;
    }

    private void CopySelectedHabitToEditFields()
    {
        if (SelectedHabit is null)
        {
            EditHabitName = string.Empty;
            EditHabitUnit = string.Empty;
            EditHabitDescription = null;
            return;
        }

        EditHabitName = SelectedHabit.Name;
        EditHabitUnit = SelectedHabit.Unit;
        EditHabitDescription = SelectedHabit.Description;
    }

    private async Task ReloadHabitsAsync(int? selectedHabitId = null)
    {
        var habits = await _apiService.GetHabitsAsync();

        Habits.Clear();

        foreach (var habit in habits)
        {
            Habits.Add(habit);
        }

        var targetHabit = selectedHabitId is null
            ? null
            : Habits.FirstOrDefault(x => x.Id == selectedHabitId.Value);

        _suppressSelectedHabitChanged = true;
        SelectedHabit = targetHabit;
        _suppressSelectedHabitChanged = false;

        if (SelectedHabit is null)
        {
            HabitLogs.Clear();
        }
        else
        {
            await ReloadSelectedHabitLogsAsync();
        }
    }

    private async Task LoadSelectedHabitLogsForSelectedHabitAsync()
    {
        try
        {
            if (SelectedHabit is null)
            {
                HabitLogs.Clear();
                return;
            }

            IsBusy = true;
            StatusMessage = $"Loading logs for '{SelectedHabit.Name}'...";

            await ReloadSelectedHabitLogsAsync();

            StatusMessage = $"Loaded {HabitLogs.Count} logs for '{SelectedHabit.Name}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load logs failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadSelectedHabitLogsAsync()
    {
        if (SelectedHabit is null)
        {
            HabitLogs.Clear();
            return;
        }

        var logs = await _apiService.GetHabitLogsAsync(SelectedHabit.Id);

        HabitLogs.Clear();

        foreach (var log in logs)
        {
            HabitLogs.Add(log);
        }
    }

    private void RaiseCommandStates()
    {
        LoadHabitsCommand.RaiseCanExecuteChanged();
        AddHabitCommand.RaiseCanExecuteChanged();
        AddHabitLogCommand.RaiseCanExecuteChanged();
        BeginEditSelectedHabitCommand.RaiseCanExecuteChanged();
        SaveSelectedHabitCommand.RaiseCanExecuteChanged();
        CancelEditSelectedHabitCommand.RaiseCanExecuteChanged();
        DeleteSelectedHabitCommand.RaiseCanExecuteChanged();
    }
}
