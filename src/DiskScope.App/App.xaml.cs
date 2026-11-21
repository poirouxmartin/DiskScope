using System.Windows;
using System.Windows.Threading;
using DiskScope.App.Services;

namespace DiskScope.App;

/// <summary>
/// Last-resort safety net: an exception nobody caught is logged and reported to the user instead of closing the application silently.
/// </summary>
public partial class App : System.Windows.Application
{
    private bool _showingError;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        string? log = ErrorLog.Default.Write("UI thread", e.Exception);
        e.Handled = true;
        if (_showingError)
        {
            return;
        }

        _showingError = true;
        try
        {
            string msg = e.Exception.Message;
            MessageBox.Show(
                $"An unexpected error occurred: {msg}\n\n"
                + (log is not null ? $"Details were written to {log}." : "The error log could not be written.")
                + "\n\nDiskScope keeps running, but the last action may not have completed.",
                "DiskScope",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _showingError = false;
        }
    }

    /// <summary>MenuButton style: opens the button's ContextMenu below it. A menu opened by code does not inherit the DataContext, so it is copied.</summary>
    private void OnMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } button)
        {
            menu.DataContext = button.DataContext;
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private static void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        ErrorLog.Default.Write("Unobserved task", e.Exception);
        e.SetObserved();
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            ErrorLog.Default.Write("Background thread (fatal)", ex);
        }
    }
}
