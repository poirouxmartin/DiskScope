using System.ComponentModel;
using System.Windows;
using DiskScope.App.ViewModels;

namespace DiskScope.App;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell = ShellViewModel.CreateDefault();
    private bool _disposed;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _shell;
        Loaded += (_, _) => _shell.Start();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_disposed)
        {
            return;
        }

        // Stop collectors and the process loop before the window goes away, so no callback hits a closed dispatcher.
        e.Cancel = true;
        _disposed = true;
        await _shell.DisposeAsync();
        Close();
    }
}
