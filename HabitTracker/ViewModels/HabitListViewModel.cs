using System.Collections.ObjectModel;
using HabitTracker.Models;
using HabitTracker.Services;

namespace HabitTracker.ViewModels;

public class HabitListViewModel : ViewModelBase
{
    private readonly ApiService _apiService;

    private string _newHabitName = string.Empty;
    private string _newHabitUnit = string.Empty;
    private string? _newHabitDescription;
    private string _statusMessage = "Ready.";
    private bool _isBusy;
    private HabitDto? _selectedHabit;

    public HabitListViewModel(ApiService apiService)
    {
        _apiService = apiService;

        LoadHabitsCommand = new AsyncRelayCommand(LoadHabitsAsync, () => !IsBusy);
        AddHabitCommand = new AsyncRelayCommand(AddHabitAsync, CanAddHabit);
    }

    public ObservableCollection<HabitDto> Habits { get; } = [];

    public AsyncRelayCommand LoadHabitsCommand { get; }

    public AsyncRelayCommand AddHabitCommand { get; }

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
                LoadHabitsCommand.RaiseCanExecuteChanged();
                AddHabitCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public HabitDto? SelectedHabit
    {
        get => _selectedHabit;
        set => SetProperty(ref _selectedHabit, value);
    }

    public async Task LoadHabitsAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Loading habits...";

            await ReloadHabitsAsync();

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

            var request = new CreateHabitRequest
            {
                Name = NewHabitName.Trim(),
                Unit = NewHabitUnit.Trim(),
                Description = string.IsNullOrWhiteSpace(NewHabitDescription)
                    ? null
                    : NewHabitDescription.Trim()
            };

            await _apiService.CreateHabitAsync(request);

            NewHabitName = string.Empty;
            NewHabitUnit = string.Empty;
            NewHabitDescription = null;

            await ReloadHabitsAsync();

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

    private bool CanAddHabit()
    {
        return !IsBusy
            && !string.IsNullOrWhiteSpace(NewHabitName)
            && !string.IsNullOrWhiteSpace(NewHabitUnit);
    }

    private async Task ReloadHabitsAsync()
    {
        var habits = await _apiService.GetHabitsAsync();

        Habits.Clear();

        foreach (var habit in habits)
        {
            Habits.Add(habit);
        }
    }
}
