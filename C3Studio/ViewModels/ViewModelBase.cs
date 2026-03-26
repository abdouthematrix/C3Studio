using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace C3Studio.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected static ICommand Cmd(Action execute, Func<bool>? canExecute = null)
        => new RelayCommand(execute, canExecute);

    protected static ICommand Cmd<T>(Action<T?> execute, Func<T?, bool>? canExecute = null)
        => new RelayCommand<T>(execute, canExecute);
}

// ── RelayCommand ────────────────────────────────────────────────────────────
public class RelayCommand : ICommand
{
    private readonly Action       _execute;
    private readonly Func<bool>?  _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    { _execute = execute; _canExecute = canExecute; }

    public event EventHandler? CanExecuteChanged
    {
        add    => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? _) => _canExecute?.Invoke() ?? true;
    public void Execute(object? _)    => _execute();
}

public class RelayCommand<T> : ICommand
{
    private readonly Action<T?>      _execute;
    private readonly Func<T?, bool>? _canExecute;

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    { _execute = execute; _canExecute = canExecute; }

    public event EventHandler? CanExecuteChanged
    {
        add    => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? p) => _canExecute?.Invoke(p is T t ? t : default) ?? true;
    public void Execute(object? p)    => _execute(p is T t ? t : default);
}
