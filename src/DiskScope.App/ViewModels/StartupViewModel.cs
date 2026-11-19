using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using DiskScope.App.Services;
using DiskScope.Maintenance;
using DiskScope.Reports;
using static System.FormattableString;

namespace DiskScope.App.ViewModels;

/// <summary>
/// Start-up entries and services, read-only (spec section 10: the first version may be read-only, with export).
/// No impact score is shown: DiskScope has not measured start-up impact, and an invented score is forbidden.
/// </summary>
public sealed class StartupViewModel : ObservableObject
{
    private readonly IShellActions _shell;
    private readonly Func<InventoryResult<StartupEntry>> _readStartup;
    private readonly Func<InventoryResult<ServiceEntry>> _readServices;
    private IReadOnlyList<StartupEntry> _allStartup = Array.Empty<StartupEntry>();
    private IReadOnlyList<ServiceEntry> _allServices = Array.Empty<ServiceEntry>();
    private string _status = "Not loaded yet";
    private string _filter = string.Empty;
    private bool _isLoading;

    public StartupViewModel(IShellActions? shell = null, Func<InventoryResult<StartupEntry>>? readStartup = null, Func<InventoryResult<ServiceEntry>>? readServices = null)
    {
        _shell = shell ?? new ShellActions();
        _readStartup = readStartup ?? (() => StartupInventory.Read());
        _readServices = readServices ?? ServiceInventory.Read;
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => !_isLoading);
        ExportStartupCommand = new RelayCommand(() => Export("DiskScope-startup.csv", w => WriteStartup(w, _allStartup)), () => _allStartup.Count > 0);
        ExportServicesCommand = new RelayCommand(() => Export("DiskScope-services.csv", w => WriteServices(w, _allServices)), () => _allServices.Count > 0);
    }

    public ObservableCollection<StartupEntry> Startup { get; } = new();

    public ObservableCollection<ServiceEntry> Services { get; } = new();

    public ObservableCollection<string> Errors { get; } = new();

    public RelayCommand RefreshCommand { get; }

    public RelayCommand ExportStartupCommand { get; }

    public RelayCommand ExportServicesCommand { get; }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
            {
                ApplyFilter();
            }
        }
    }

    public async Task RefreshAsync()
    {
        if (_isLoading)
        {
            return;
        }

        _isLoading = true;
        RefreshCommand.RaiseCanExecuteChanged();
        Status = "Reading start-up entries and services...";
        try
        {
            (InventoryResult<StartupEntry> startup, InventoryResult<ServiceEntry> services) =
                await Task.Run(() => (_readStartup(), _readServices())).ConfigureAwait(true);
            _allStartup = startup.Items;
            _allServices = services.Items;
            Errors.Clear();
            foreach (string e in startup.Errors.Concat(services.Errors))
            {
                Errors.Add(e);
            }

            ApplyFilter();
            int errors = Errors.Count;
            Status = Invariant($"{_allStartup.Count} start-up entries, {_allServices.Count} services") +
                (errors > 0 ? Invariant($"; {errors} sources could not be read (list below): the inventory is partial") : "; every source was read") +
                ". Read-only: nothing can be disabled from here.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = "Reading start-up entries failed: " + ex.Message;
        }
        finally
        {
            _isLoading = false;
            RefreshCommand.RaiseCanExecuteChanged();
            ExportStartupCommand.RaiseCanExecuteChanged();
            ExportServicesCommand.RaiseCanExecuteChanged();
        }
    }

    internal static void WriteStartup(TextWriter w, IEnumerable<StartupEntry> entries) =>
        CsvTable.Write(w,
            ["source", "scope", "name", "command", "location", "executable", "executable_exists", "publisher_version_resource", "enabled", "trigger"],
            entries.Select(e => (IReadOnlyList<string?>)[e.Source.ToString(), e.Scope, e.Name, e.Command, e.Location, e.ExecutablePath, Bool(e.ExecutableExists), e.Publisher, Bool(e.Enabled), e.Trigger]));

    internal static void WriteServices(TextWriter w, IEnumerable<ServiceEntry> services) =>
        CsvTable.Write(w,
            ["name", "display_name", "state", "pid", "start_type", "delayed_start", "account", "image_path", "depends_on", "shares_process"],
            services.Select(s => (IReadOnlyList<string?>)[s.Name, s.DisplayName, s.State, s.ProcessId?.ToString(System.Globalization.CultureInfo.InvariantCulture), s.StartType.ToString(), Bool(s.DelayedStart), s.Account, s.ImagePath, string.Join(';', s.DependsOn), Bool(s.SharesProcess)]));

    private static string? Bool(bool? b) => b switch { true => "true", false => "false", null => null };

    private void ApplyFilter()
    {
        string f = _filter.Trim();
        Startup.Clear();
        foreach (StartupEntry e in _allStartup.Where(e => f.Length == 0 || e.Name.Contains(f, StringComparison.OrdinalIgnoreCase) || e.Command.Contains(f, StringComparison.OrdinalIgnoreCase)))
        {
            Startup.Add(e);
        }

        Services.Clear();
        foreach (ServiceEntry s in _allServices.Where(s => f.Length == 0 || s.Name.Contains(f, StringComparison.OrdinalIgnoreCase) || s.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase)))
        {
            Services.Add(s);
        }
    }

    private void Export(string suggested, Action<TextWriter> write)
    {
        if (_shell.AskSavePath(suggested) is not { } path)
        {
            return;
        }

        try
        {
            using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
            write(writer);
            Status = $"Exported to {path}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = "Export failed: " + ex.Message;
        }
    }
}
