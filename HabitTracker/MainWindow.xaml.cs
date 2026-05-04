using System.Windows;
using HabitTracker.Services;
using HabitTracker.ViewModels;

namespace HabitTracker;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var apiService = new ApiService("http://localhost:5253/");
        var viewModel = new HabitListViewModel(apiService);

        DataContext = viewModel;

        Loaded += async (_, _) => await viewModel.LoadHabitsAsync();
    }
}
