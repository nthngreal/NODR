using System.Windows.Input;

namespace NODR.ViewModels;

public sealed class RelayCommand : ICommand
{
    private readonly Action? _execute;
    private readonly Action<object?>? _executeWithParameter;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public RelayCommand(Action<object?> execute, Func<bool>? canExecute = null)
    {
        _executeWithParameter = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter)
    {
        if (_executeWithParameter is not null) _executeWithParameter(parameter);
        else _execute?.Invoke();
    }
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
