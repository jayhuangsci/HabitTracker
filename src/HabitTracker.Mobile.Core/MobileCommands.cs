using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace HabitTracker.Mobile.Core;

public abstract class ObservableModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Notify(name); return true;
    }
}

public sealed class MobileCommand(Action<object?> action, Func<bool>? canRun = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canRun?.Invoke() ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) action(parameter); }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class MobileAsyncCommand(Func<object?, Task> action, Func<bool>? canRun = null) : ICommand
{
    private bool _running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_running && (canRun?.Invoke() ?? true);
    public async void Execute(object? parameter) => await ExecuteAsync(parameter);
    public async Task ExecuteAsync(object? parameter = null)
    {
        if (!CanExecute(parameter)) return;
        _running = true; Refresh();
        try { await action(parameter); }
        finally { _running = false; Refresh(); }
    }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
