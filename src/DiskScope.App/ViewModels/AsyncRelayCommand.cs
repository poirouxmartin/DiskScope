using System.Windows.Input;

namespace DiskScope.App.ViewModels;

/// <summary>
/// Command for asynchronous work. Unlike an async lambda in <see cref="RelayCommand"/> (an async void), every
/// exception except cancellation is caught and handed to <c>onError</c>, so a failed action shows a status
/// instead of terminating the application.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Action<Exception> _onError;
    private readonly Func<bool>? _canExecute;

    public AsyncRelayCommand(Func<Task> execute, Action<Exception> onError, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _onError = onError;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    // ExecuteAsync never throws (except from onError itself), so this async void cannot crash the application.
    public async void Execute(object? parameter) => await ExecuteAsync();

    public async Task ExecuteAsync()
    {
        try
        {
            await _execute();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _onError(ex);
        }
    }

    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}
