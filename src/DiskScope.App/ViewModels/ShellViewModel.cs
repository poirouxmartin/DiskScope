namespace DiskScope.App.ViewModels;

/// <summary>Navigation between modules. Tab indexes are constants so dashboard cards can open their module.</summary>
public sealed class ShellViewModel : ObservableObject, IAsyncDisposable
{
    public const int DashboardTab = 0;
    public const int StorageTab = 1;
    public const int CpuMemoryTab = 2;
    public const int ProcessesTab = 3;
    public const int GpuTab = 4;
    public const int HistoryTab = 5;
    public const int StartupTab = 6;

    private int _selectedTab;

    public ShellViewModel(StorageViewModel storage, MonitoringViewModel monitoring, ProcessesViewModel processes, StartupViewModel startup)
    {
        Storage = storage;
        Monitoring = monitoring;
        Processes = processes;
        Startup = startup;
        Monitoring.ProcessNameResolver = processes.TryGetName;
        NavigateCommand = new ParameterCommand(p =>
        {
            if (p is int tab)
            {
                SelectedTab = tab;
            }
            else if (p is string s && int.TryParse(s, out int parsed))
            {
                SelectedTab = parsed;
            }
        });
    }

    public StorageViewModel Storage { get; }

    public MonitoringViewModel Monitoring { get; }

    public ProcessesViewModel Processes { get; }

    public StartupViewModel Startup { get; }

    public ParameterCommand NavigateCommand { get; }

    public int SelectedTab { get => _selectedTab; set => SetProperty(ref _selectedTab, value); }

    public static ShellViewModel CreateDefault() =>
        new(new StorageViewModel(), MonitoringViewModel.CreateDefault(), new ProcessesViewModel(), new StartupViewModel());

    public void Start()
    {
        Monitoring.Start();
        Processes.Start();
        _ = Startup.RefreshAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Processes.DisposeAsync().ConfigureAwait(true);
        await Monitoring.DisposeAsync().ConfigureAwait(true);
    }
}
