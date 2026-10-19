using System.Windows.Input;

namespace DiskScope.App.ViewModels;

public sealed class ParameterCommand(Action<object?> execute) : ICommand
{
    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute(parameter);

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }
}
