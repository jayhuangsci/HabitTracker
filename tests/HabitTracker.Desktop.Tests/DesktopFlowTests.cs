using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HabitTracker.Core;
using HabitTracker.Services;
using HabitTracker.ViewModels;
using HabitTracker.Views;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HabitTracker.Desktop.Tests;

public class DesktopFlowTests
{
    [Fact]
    public async Task RealViewsBindAndWorkoutCommandsPersistThroughTheApi()
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                var database = Path.Combine(Path.GetTempPath(), $"habittracker-desktop-{Guid.NewGuid():N}.db");
                try
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/HabitTracker;component/Themes/FlatTheme.xaml") });
                    using var factory = new WebApplicationFactory<global::Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing")
                        .UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={database};Pooling=False").UseSetting("DemoMode", "false"));
                    using var http = factory.CreateClient();
                    using var vm = new FitnessViewModel(new FitnessApiClient(http), new HabitListViewModel(new ApiService("http://localhost:5253/")));
                    await vm.InitializeAsync();
                    Assert.False(vm.HasError);
                    UserControl[] views = [new OverviewView(), new WorkoutView(), new HistoryView(), new ProgressView(), new HabitsView()];
                    foreach (var view in views)
                    {
                        view.DataContext = view is HabitsView ? vm.Habits : vm;
                        view.Measure(new Size(1160, 1100)); view.Arrange(new Rect(0, 0, 1160, 1100)); view.UpdateLayout();
                    }
                    Assert.Equal("205", vm.TotalLoadLabel);
                    vm.LeftStack = "45++25";
                    Assert.False(vm.AddSetCommand.CanExecute(null));
                    vm.LeftStack = "45lbs+25lbs+10";
                    vm.Mirror = false; vm.RightStack = "45 + 10";
                    Assert.True(vm.HasImbalance);
                    Assert.Equal("180", vm.TotalLoadLabel);
                    vm.Mirror = true;
                    vm.Unit = WeightUnit.Kilograms;
                    Assert.Equal("72.5", vm.TotalLoadLabel);
                    vm.Unit = WeightUnit.Pounds;
                    vm.Navigate("Workout");
                    vm.AddSetCommand.Execute(null);
                    await WaitForIdle(vm);
                    Assert.False(vm.HasError, vm.StatusMessage);
                    Assert.Equal(205, Assert.Single(vm.SessionSets).TotalWeight);
                    Assert.NotNull(vm.ActiveWorkout);
                    using (var reopened = new FitnessViewModel(new FitnessApiClient(http), new HabitListViewModel(new ApiService("http://localhost:5253/"))))
                    {
                        await reopened.InitializeAsync();
                        Assert.Equal(vm.ActiveWorkout.Id, reopened.ActiveWorkout?.Id);
                        Assert.Equal(205, Assert.Single(reopened.SessionSets).TotalWeight);
                    }
                    vm.FinishCommand.Execute(null);
                    await WaitForIdle(vm);
                    Assert.False(vm.HasError, vm.StatusMessage);
                    Assert.Null(vm.ActiveWorkout);
                    Assert.Equal("1", vm.SessionsLabel);
                    vm.Search = "bench";
                    Assert.Single(vm.FilteredWorkouts);
                    vm.SelectedHistorySet = vm.HistorySets[0];
                    vm.CopyLoadCommand.Execute(null);
                    Assert.True(vm.IsWorkout);
                    Assert.Equal("205", vm.TotalLoadLabel);
                    Assert.Equal("45 + 25 + 10", vm.LeftStack);
                    result.SetResult();
                }
                catch (Exception ex) { result.TrySetException(ex); }
                finally
                {
                    foreach (var path in new[] { database, database + "-wal", database + "-shm" }) File.Delete(path);
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(await Task.Run(() => thread.Join(TimeSpan.FromSeconds(45))), "The desktop flow did not finish within 45 seconds.");
        await result.Task;
    }

    private static async Task WaitForIdle(FitnessViewModel vm)
    {
        var timeout = Stopwatch.StartNew();
        while (vm.IsBusy && timeout.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
        Assert.False(vm.IsBusy);
    }
}
