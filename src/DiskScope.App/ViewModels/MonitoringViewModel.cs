using System.Collections.ObjectModel;
using System.Globalization;
using static System.FormattableString;
using System.Windows.Threading;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using DiskScope.App.Services;
using DiskScope.Core;
using DiskScope.Reports;
using DiskScope.Diagnostics;
using DiskScope.History;
using DiskScope.Monitoring;
using DiskScope.Monitoring.CpuRam;
using DiskScope.Monitoring.Gpu;
using DiskScope.Processes;

namespace DiskScope.App.ViewModels;

/// <summary>
/// Owns the collectors, the scheduler, the history and the alert engine, and exposes them to the Dashboard,
/// CPU &amp; memory and History &amp; alerts views. Collectors run on scheduler threads; this view model PULLS the latest
/// values once per <see cref="RefreshInterval"/> on the UI thread, so the UI cost does not grow with the number of
/// batches and no collector ever touches WPF.
/// </summary>
public sealed class MonitoringViewModel : ObservableObject, IAsyncDisposable
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan TrendWindow = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan DetailWindow = TimeSpan.FromMinutes(10);

    private readonly MonitoringScheduler _scheduler;
    private readonly IMetricStore _store;
    private readonly IAlertEngine _alerts;
    private readonly MonitoringClock _clock;
    private readonly Dictionary<string, MetricDescriptor> _descriptors;
    private readonly GpuCollector? _gpu;
    private readonly IReadOnlyList<IMetricCollector> _collectors;
    private readonly DiagnosticEngine _diagnostics;
    private readonly IShellActions _shell;
    private string _exportStatus = string.Empty;
    private DateTime _lastDiagnostics;
    private string _findingSignature = string.Empty;
    private DispatcherTimer? _timer;
    private string _alertSignature = string.Empty;
    private string _statusText = "Starting";
    private string _selfCostText = MetricFormatter.Unknown;
    private MetricDescriptor? _historyMetric;
    private TimeSpan _historyRange = TimeSpan.FromMinutes(10);
    private IReadOnlyList<MetricPoint> _historyPoints = Array.Empty<MetricPoint>();
    private string _historySummary = string.Empty;
    private DateTime _now;

    public MonitoringViewModel(IReadOnlyList<IMetricCollector> collectors, IMetricStore store, IAlertEngine alerts, MonitoringClock clock, DiagnosticEngine? diagnostics = null, IShellActions? shell = null)
    {
        _shell = shell ?? new ShellActions();
        ExportHistoryCsvCommand = new RelayCommand(() => Export("DiskScope-history.csv", w => MetricHistoryCsv.Write(w, BuildReport(_historyRange, allMetrics: true).Series)));
        ExportJsonCommand = new RelayCommand(() => Export("DiskScope-report.json", w => JsonReportWriter.Write(w, BuildReport(_historyRange))));
        ExportHtmlCommand = new RelayCommand(() => Export("DiskScope-report.html", w => HtmlReportWriter.Write(w, BuildReport(_historyRange))));
        _collectors = collectors;
        _diagnostics = diagnostics ?? new DiagnosticEngine([]);
        _store = store;
        _alerts = alerts;
        _clock = clock;
        _scheduler = new MonitoringScheduler(collectors, store, clock);
        _scheduler.BatchPublished += batch => _alerts.Evaluate(batch);
        _descriptors = _scheduler.Metrics.ToDictionary(m => m.Id, StringComparer.Ordinal);

        CpuTile = Tile(CpuCollector.Total, TrendWindow, ShellViewModel.CpuMemoryTab);
        MemoryTile = Tile(MemoryCollector.PhysicalUsed, TrendWindow, ShellViewModel.CpuMemoryTab);
        CommitTile = Tile(MemoryCollector.CommitUsed, TrendWindow, ShellViewModel.CpuMemoryTab);
        SelfCpuTile = Tile(SelfCollector.Cpu, TrendWindow, ShellViewModel.CpuMemoryTab);
        CpuDetailTile = Tile(CpuCollector.Total, DetailWindow, null);
        MemoryDetailTile = Tile(MemoryCollector.PhysicalUsed, DetailWindow, null);
        CommitDetailTile = Tile(MemoryCollector.CommitUsed, DetailWindow, null);
        DiskTile = Tile(DiskActivityCollector.Active, TrendWindow, ShellViewModel.CpuMemoryTab);
        DiskDetailTile = Tile(DiskActivityCollector.Active, DetailWindow, null);

        VolumeTiles = new ObservableCollection<MetricTileViewModel>(
            _scheduler.Metrics.Where(m => m.Id.StartsWith("disk.", StringComparison.Ordinal) && m.Id.EndsWith(".used", StringComparison.Ordinal))
                .Select(m => new MetricTileViewModel(m, TrendWindow, ShellViewModel.StorageTab)));

        Cores = new ObservableCollection<CoreViewModel>(
            _scheduler.Metrics.Count(m => m.Id.StartsWith("cpu.core.", StringComparison.Ordinal)) is int n and > 0
                ? Enumerable.Range(0, n).Select(i => new CoreViewModel(i))
                : []);

        MemoryRows = new ObservableCollection<MetricRowViewModel>(
            _scheduler.Metrics.Where(m => m.Id.StartsWith("mem.", StringComparison.Ordinal) || m.Id.StartsWith("sys.", StringComparison.Ordinal)
                || (m.Id.StartsWith("cpu.", StringComparison.Ordinal) && !m.Id.StartsWith("cpu.core.", StringComparison.Ordinal))
                || (m.Id.StartsWith("disk.", StringComparison.Ordinal) && m.Id.Count(c => c == '.') == 1))
                .Select(m => new MetricRowViewModel(m)));

        _gpu = collectors.OfType<GpuCollector>().FirstOrDefault();
        GpuAdapters = new ObservableCollection<GpuAdapterViewModel>(
            _gpu?.Adapters.Select((a, i) => new GpuAdapterViewModel(i, a, id => Tile(id, DetailWindow, null), _descriptors)) ?? []);
        GpuTiles = new ObservableCollection<MetricTileViewModel>(
            GpuAdapters.Select(a => Tile(GpuCollector.UtilizationId(a.Index), TrendWindow, ShellViewModel.GpuTab)).OfType<MetricTileViewModel>());
        GpuStatus = _gpu is null
            ? "GPU collector not configured."
            : _gpu.CheckAvailability() is { IsAvailable: false } off ? "GPU monitoring unavailable: " + off.Reason : "GPU counters available.";

        Rules = new ObservableCollection<RuleViewModel>(_alerts.Rules.Select(r => new RuleViewModel(r, _alerts)));
        HistoryMetrics = _scheduler.Metrics.OrderBy(m => m.Id, StringComparer.Ordinal).ToArray();
        _historyMetric = HistoryMetrics.FirstOrDefault(m => m.Id == CpuCollector.Total) ?? HistoryMetrics.FirstOrDefault();
    }

    public static MonitoringViewModel CreateDefault()
    {
        var clock = new MonitoringClock();
        IReadOnlyList<IMetricCollector> collectors =
        [
            new CpuCollector(),
            new MemoryCollector(),
            new VolumeSpaceCollector(),
            new DiskActivityCollector(),
            new GpuCollector(),
            new SelfCollector(),
        ];
        return new MonitoringViewModel(collectors, new InMemoryMetricStore(), new AlertEngine(DefaultRules.Create()), clock, DiagnosticEngine.CreateDefault());
    }

    public MonitoringScheduler Scheduler => _scheduler;

    public MetricTileViewModel? CpuTile { get; }

    public MetricTileViewModel? MemoryTile { get; }

    public MetricTileViewModel? CommitTile { get; }

    public MetricTileViewModel? SelfCpuTile { get; }

    public MetricTileViewModel? DiskTile { get; }

    public MetricTileViewModel? DiskDetailTile { get; }

    /// <summary>Hypotheses from the diagnostic rules, newest first; re-evaluated every 5 s.</summary>
    public ObservableCollection<FindingRowViewModel> Findings { get; } = new();

    public MetricTileViewModel? CpuDetailTile { get; }

    public MetricTileViewModel? MemoryDetailTile { get; }

    public MetricTileViewModel? CommitDetailTile { get; }

    public ObservableCollection<MetricTileViewModel> VolumeTiles { get; }

    public ObservableCollection<CoreViewModel> Cores { get; }

    public ObservableCollection<GpuAdapterViewModel> GpuAdapters { get; }

    /// <summary>Dashboard cards, one per adapter.</summary>
    public ObservableCollection<MetricTileViewModel> GpuTiles { get; }

    public ObservableCollection<GpuProcessRow> GpuProcesses { get; } = new();

    public string GpuStatus { get; }

    /// <summary>PID to image name, provided by the process view; any thread.</summary>
    public Func<int, string?>? ProcessNameResolver { get; set; }

    public ObservableCollection<MetricRowViewModel> MemoryRows { get; }

    public ObservableCollection<CollectorStats> CollectorStats { get; } = new();

    public ObservableCollection<AlertRowViewModel> Alerts { get; } = new();

    public ObservableCollection<RuleViewModel> Rules { get; }

    public IReadOnlyList<MetricDescriptor> HistoryMetrics { get; }

    public IReadOnlyList<TimeSpan> HistoryRanges { get; } = [TimeSpan.FromMinutes(10), TimeSpan.FromHours(1), TimeSpan.FromHours(24)];

    public MetricDescriptor? HistoryMetric
    {
        get => _historyMetric;
        set
        {
            if (SetProperty(ref _historyMetric, value))
            {
                RefreshHistory();
            }
        }
    }

    public TimeSpan HistoryRange
    {
        get => _historyRange;
        set
        {
            if (SetProperty(ref _historyRange, value))
            {
                RefreshHistory();
            }
        }
    }

    public IReadOnlyList<MetricPoint> HistoryPoints { get => _historyPoints; private set => SetProperty(ref _historyPoints, value); }

    public string HistorySummary { get => _historySummary; private set => SetProperty(ref _historySummary, value); }

    public DateTime Now { get => _now; private set => SetProperty(ref _now, value); }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    public string SelfCostText { get => _selfCostText; private set => SetProperty(ref _selfCostText, value); }

    public int ActiveAlertCount => Alerts.Count(a => a.Alert.EndedUtc is null && !a.Alert.Acknowledged);

    public void Start()
    {
        _scheduler.Start();
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = RefreshInterval };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
    }

    public async ValueTask DisposeAsync()
    {
        _timer?.Stop();
        await _scheduler.StopAsync().ConfigureAwait(true);
        foreach (IDisposable d in _collectors.OfType<IDisposable>())
        {
            d.Dispose();
        }
    }

    /// <summary>Pulls the latest values and the recent history into the bound properties. UI thread.</summary>
    public void Refresh()
    {
        DateTime now = _clock.UtcNow;
        Now = now;
        UpdateTile(CpuTile, now, 100);
        UpdateTile(SelfCpuTile, now, double.NaN);
        UpdateTile(CpuDetailTile, now, 100);
        double totalRam = Latest(MemoryCollector.PhysicalTotal) ?? double.NaN;
        double commitLimit = Latest(MemoryCollector.CommitLimit) ?? double.NaN;
        UpdateTile(MemoryTile, now, totalRam, totalRam);
        UpdateTile(MemoryDetailTile, now, totalRam, totalRam);
        UpdateTile(CommitTile, now, commitLimit, commitLimit, "limit");
        UpdateTile(CommitDetailTile, now, commitLimit, commitLimit, "limit");

        foreach (MetricTileViewModel tile in VolumeTiles)
        {
            string letter = tile.Id.Split('.')[1];
            tile.Update(_scheduler.GetLatest(tile.Id), _store.Query(tile.Id, now - tile.Window, now), now, 100);
            tile.Detail = $"{MetricFormatter.Format(Latest(VolumeSpaceCollector.FreeId(letter)), MetricUnit.Bytes)} free of {MetricFormatter.Format(Latest(VolumeSpaceCollector.TotalId(letter)), MetricUnit.Bytes)}";
        }

        foreach (CoreViewModel core in Cores)
        {
            core.Update(_scheduler.GetLatest(CpuCollector.CoreId(core.Index)));
        }

        RefreshGpu(now);

        foreach (MetricRowViewModel row in MemoryRows)
        {
            row.Update(_scheduler.GetLatest(row.Descriptor.Id));
        }

        UpdateTile(DiskTile, now, 100);
        UpdateTile(DiskDetailTile, now, 100);
        RefreshStats();
        RefreshAlerts();
        if (now - _lastDiagnostics >= TimeSpan.FromSeconds(5))
        {
            _lastDiagnostics = now;
            RefreshFindings(now);
        }
        RefreshHistory();
    }

    private void RefreshGpu(DateTime now)
    {
        foreach (MetricTileViewModel tile in GpuTiles)
        {
            tile.Update(_scheduler.GetLatest(tile.Id), _store.Query(tile.Id, now - tile.Window, now), now, 100);
        }

        foreach (GpuAdapterViewModel adapter in GpuAdapters)
        {
            UpdateTile(adapter.UsageTile, now, 100);
            UpdateTile(adapter.DedicatedTile, now, adapter.Info.DedicatedVideoMemory > 0 ? adapter.Info.DedicatedVideoMemory : double.NaN, adapter.Info.DedicatedVideoMemory);
            UpdateTile(adapter.SharedTile, now, adapter.Info.SharedSystemMemory > 0 ? adapter.Info.SharedSystemMemory : double.NaN, adapter.Info.SharedSystemMemory, "limit");
            foreach (MetricRowViewModel row in adapter.EngineRows)
            {
                row.Update(_scheduler.GetLatest(row.Descriptor.Id));
            }
        }

        if (_gpu is null)
        {
            return;
        }

        GpuProcesses.Clear();
        foreach (var (pid, percent) in _gpu.ProcessUsage.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Take(15))
        {
            GpuProcesses.Add(new GpuProcessRow(pid, ProcessNameResolver?.Invoke(pid) ?? "(unknown)", percent));
        }
    }

    private void RefreshStats()
    {
        IReadOnlyList<CollectorStats> stats = _scheduler.GetStats();
        CollectorStats.Clear();
        foreach (CollectorStats s in stats)
        {
            CollectorStats.Add(s);
        }

        long failures = stats.Sum(s => s.Failures);
        StatusText = Invariant($"{stats.Count(s => s.Availability.IsAvailable)}/{stats.Count} collectors running, {failures} failed collections, {_scheduler.StoreFailures} history write failures");
        SelfCostText = Invariant($"DiskScope: CPU {MetricFormatter.Format(Latest(SelfCollector.Cpu), MetricUnit.Percent)}, working set {MetricFormatter.Format(Latest(SelfCollector.WorkingSet), MetricUnit.Bytes)}, ") +
            Invariant($"collection time {stats.Sum(s => s.LastDuration.TotalMilliseconds):F1} ms per round");
    }

    private void RefreshAlerts()
    {
        IReadOnlyList<AlertEvent> events = _alerts.Events;
        string signature = string.Join('|', events.Select(e => $"{e.Id}:{e.EndedUtc?.Ticks}:{e.Acknowledged}:{e.Explanation}"));
        if (signature == _alertSignature)
        {
            return;
        }

        _alertSignature = signature;
        Alerts.Clear();
        foreach (AlertEvent e in events.Reverse())
        {
            Alerts.Add(new AlertRowViewModel(e, id =>
            {
                _alerts.Acknowledge(id);
                RefreshAlerts();
            }));
        }

        OnPropertyChanged(nameof(ActiveAlertCount));
    }

    internal void RefreshFindings(DateTime now)
    {
        (IReadOnlyList<Finding> findings, IReadOnlyList<string> errors) = _diagnostics.Evaluate(new DiagnosticContext(_alerts.Events, _store, now));
        string signature = string.Join('|', findings.Select(f => f.RuleId + f.FromUtc.Ticks + f.Explanation)) + string.Join('|', errors);
        if (signature == _findingSignature)
        {
            return;
        }

        _findingSignature = signature;
        Findings.Clear();
        foreach (Finding f in findings.OrderByDescending(f => f.FromUtc))
        {
            Findings.Add(new FindingRowViewModel(f));
        }

        foreach (string e in errors)
        {
            Findings.Add(FindingRowViewModel.ForError(e, now));
        }

        RaisePropertyChanged(nameof(FindingCount));
    }

    public int FindingCount => Findings.Count;

    public RelayCommand ExportHistoryCsvCommand { get; }

    public RelayCommand ExportJsonCommand { get; }

    public RelayCommand ExportHtmlCommand { get; }

    public string ExportStatus { get => _exportStatus; private set => SetProperty(ref _exportStatus, value); }

    /// <summary>
    /// Snapshot of the session for the report writers, over the last <paramref name="range"/>. Key metrics only unless
    /// <paramref name="allMetrics"/>. No machine name, user name or command line is included.
    /// </summary>
    public DiagnosticReport BuildReport(TimeSpan range, bool allMetrics = false)
    {
        DateTime now = _clock.UtcNow;
        DateTime from = now - range;
        string[] key = [CpuCollector.Total, MemoryCollector.PhysicalUsed, MemoryCollector.CommitPercent, DiskActivityCollector.Active, DiskActivityCollector.Latency];
        IEnumerable<MetricDescriptor> metrics = allMetrics
            ? _scheduler.Metrics
            : _scheduler.Metrics.Where(m => key.Contains(m.Id) || (m.Id.StartsWith("gpu.", StringComparison.Ordinal) && m.Id.EndsWith(".util", StringComparison.Ordinal)) || VolumeTiles.Any(v => v.Id == m.Id));
        var series = metrics.Select(m => new ReportSeries(m, _store.Query(m.Id, from, now))).ToArray();
        var collectors = _scheduler.GetStats().Select(s => new ReportCollector(
            s.Collector, s.Availability.IsAvailable, s.Availability.Reason, s.Collections, s.Failures, s.SkippedTicks,
            s.Collections > 0 ? s.TotalDuration.TotalMilliseconds / s.Collections : double.NaN, s.MaxDuration.TotalMilliseconds)).ToArray();
        var unavailable = _scheduler.Metrics.Where(m => _scheduler.GetLatest(m.Id) is not { IsAvailable: true }).ToArray();
        string version = typeof(MonitoringViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        (IReadOnlyList<Finding> findings, _) = _diagnostics.Evaluate(new DiagnosticContext(_alerts.Events, _store, now));
        return new DiagnosticReport(
            version,
            now,
            from,
            now,
            RuntimeInformation.OSDescription,
            Environment.ProcessorCount,
            collectors,
            unavailable,
            series,
            _alerts.Events.Where(a => (a.EndedUtc ?? now) >= from).ToArray(),
            findings.Where(x => x.ToUtc >= from).ToArray(),
            _alerts.Rules,
            [
                "History is kept in memory since DiskScope started; older periods are not covered.",
                "Values that could not be read are absent (gaps), never zero; first samples of rate metrics are always unavailable.",
                "Without administrator rights some processes cannot be opened; their details are unavailable.",
                "Hypotheses are correlations in time, not proven causes.",
                "GPU temperature, clocks, power and fans, CPU frequency and hardware sensors are not collected.",
            ]);
    }

    private void Export(string suggestedName, Action<TextWriter> write)
    {
        if (_shell.AskSavePath(suggestedName) is not { } path)
        {
            return;
        }

        try
        {
            using var writer = new StreamWriter(path, false, new UTF8Encoding(path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)));
            write(writer);
            ExportStatus = "Exported to " + path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ExportStatus = "Export failed: " + ex.Message;
        }
    }

    private void RefreshHistory()
    {
        if (_historyMetric is null)
        {
            return;
        }

        DateTime now = _clock.UtcNow;
        IReadOnlyList<MetricPoint> points = _store.Query(_historyMetric.Id, now - _historyRange, now);
        HistoryPoints = points;
        HistorySummary = points.Count == 0
            ? "No data in this range (the history is kept in memory since DiskScope started)."
            : Invariant($"{points.Count} points, min {MetricFormatter.Format(points.Min(p => p.Min), _historyMetric.Unit)}, ") +
                Invariant($"avg {MetricFormatter.Format(points.Sum(p => p.Average * p.Samples) / points.Sum(p => p.Samples), _historyMetric.Unit)}, ") +
                Invariant($"max {MetricFormatter.Format(points.Max(p => p.Max), _historyMetric.Unit)}. Source: {_historyMetric.Source}");
    }

    private double? Latest(string id) => _scheduler.GetLatest(id)?.Value;

    private void UpdateTile(MetricTileViewModel? tile, DateTime now, double maximum, double ofValue = double.NaN, string ofLabel = "of")
    {
        if (tile is null)
        {
            return;
        }

        tile.Update(_scheduler.GetLatest(tile.Id), _store.Query(tile.Id, now - tile.Window, now), now, maximum);
        if (!double.IsNaN(ofValue))
        {
            tile.Detail = $"{ofLabel} {MetricFormatter.Format(ofValue, tile.Descriptor.Unit)}";
        }
    }

    private MetricTileViewModel? Tile(string id, TimeSpan window, int? navigateTo) =>
        _descriptors.TryGetValue(id, out MetricDescriptor? d) ? new MetricTileViewModel(d, window, navigateTo) : null;

    private void OnPropertyChanged(string name) => RaisePropertyChanged(name);
}
