using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using static System.FormattableString;
using System.Windows;
using System.IO;
using System.Text;
using DiskScope.App.Services;
using DiskScope.Core;
using DiskScope.Reports;
using DiskScope.Diagnostics;
using DiskScope.Processes;

namespace DiskScope.App.ViewModels;

/// <summary>One row of the process view. Handle-dependent values read "n/a" when the process refused the open.</summary>
public sealed class ProcessRowViewModel : ObservableObject
{
    private int _depth;
    private double? _cpu;
    private ProcessSnapshot _snapshot;
    private string _growth = string.Empty;
    private double? _memoryShare;

    public ProcessRowViewModel(ProcessSnapshot snapshot) => _snapshot = snapshot;

    public ProcessKey Key => _snapshot.Key;

    public string Name => _snapshot.Name;

    public int Pid => _snapshot.ProcessId;

    public int ParentPid => _snapshot.ParentProcessId;

    internal ProcessSnapshot Snapshot => _snapshot;

    public int Depth { get => _depth; private set { if (SetProperty(ref _depth, value)) { RaisePropertyChanged(nameof(Indent)); } } }

    public Thickness Indent => new(Depth * 14, 0, 0, 0);

    /// <summary>Percent of the whole machine; null = unknown (first refresh, no access, PID reuse).</summary>
    public double? Cpu { get => _cpu; private set { if (SetProperty(ref _cpu, value)) { RaisePropertyChanged(nameof(CpuText)); } } }

    public string CpuText => MetricFormatter.Format(Cpu, MetricUnit.Percent);

    public long? PrivateBytes => _snapshot.PrivateBytes;

    public string PrivateBytesText => Bytes(PrivateBytes);

    public long? WorkingSet => _snapshot.WorkingSetBytes;

    public string WorkingSetText => Bytes(WorkingSet);

    public long? PrivateWorkingSet => _snapshot.PrivateWorkingSetBytes;

    public string PrivateWorkingSetText => Bytes(PrivateWorkingSet);

    public int Threads => _snapshot.ThreadCount;

    public int? Handles => _snapshot.HandleCount;

    public string HandlesText => Handles?.ToString("N0", CultureInfo.InvariantCulture) ?? MetricFormatter.Unknown;

    public long? IoRead => _snapshot.IoReadBytes;

    public string IoReadText => Bytes(IoRead);

    public long? IoWrite => _snapshot.IoWriteBytes;

    public string IoWriteText => Bytes(IoWrite);

    public string Started => _snapshot.StartTimeUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? MetricFormatter.Unknown;

    public string Path => _snapshot.ImagePath ?? (_snapshot.IsAccessible ? MetricFormatter.Unknown : "access denied");

    public string Growth { get => _growth; private set => SetProperty(ref _growth, value); }

    /// <summary>Private bytes as a percent of the largest process in the list (0-100); null when unknown.</summary>
    public double? MemoryShare { get => _memoryShare; private set => SetProperty(ref _memoryShare, value); }

    public void Update(ProcessSnapshot snapshot, int depth, double? cpu, string growth, long maxPrivateBytes = 0)
    {
        bool changed = !ReferenceEquals(_snapshot, snapshot);
        _snapshot = snapshot;
        Depth = depth;
        Cpu = cpu;
        Growth = growth;
        MemoryShare = snapshot.PrivateBytes is long bytes && maxPrivateBytes > 0
            ? Math.Clamp(100.0 * bytes / maxPrivateBytes, 0, 100)
            : null;
        if (changed)
        {
            foreach (string name in new[] { nameof(PrivateBytes), nameof(PrivateBytesText), nameof(WorkingSet), nameof(WorkingSetText), nameof(PrivateWorkingSet), nameof(PrivateWorkingSetText), nameof(Threads), nameof(Handles), nameof(HandlesText), nameof(IoRead), nameof(IoReadText), nameof(IoWrite), nameof(IoWriteText), nameof(Path) })
            {
                RaisePropertyChanged(name);
            }
        }
    }

    public override string ToString() => $"{Name} ({Pid})";

    private static string Bytes(long? v) => MetricFormatter.Format(v, MetricUnit.Bytes);
}

/// <summary>
/// Process explorer: refreshes every <see cref="RefreshInterval"/> on a background task (enumeration, CPU deltas,
/// tree, growth detection), then applies the result on the UI thread in place, so selection and scroll survive.
/// Read-only: no kill, no priority change (spec section 6).
/// </summary>
public sealed class ProcessesViewModel : ObservableObject, IAsyncDisposable
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    private readonly ProcessCpuTracker _cpu = new(Environment.ProcessorCount);
    private readonly MemoryGrowthDetector<ProcessKey> _growth = new();
    private readonly Dictionary<ProcessKey, ProcessRowViewModel> _rows = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private string _filterText = string.Empty;
    private bool _showTree;
    private string _summary = "Not refreshed yet";
    private ProcessRowViewModel? _selected;
    private Snapshot? _last;
    private Dictionary<int, string> _names = new();
    private readonly IShellActions _shell;

    public ProcessesViewModel(IShellActions? shell = null)
    {
        _shell = shell ?? new ShellActions();
        ExportCommand = new RelayCommand(Export, () => Rows.Count > 0);
    }

    public RelayCommand ExportCommand { get; }

    public ObservableCollection<ProcessRowViewModel> Rows { get; } = new();

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value) && _last is not null)
            {
                Apply(_last);
            }
        }
    }

    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    /// <summary>False (default): flat list by private bytes, largest first. True: parent/child tree when no filter is set.</summary>
    public bool ShowTree
    {
        get => _showTree;
        set
        {
            if (SetProperty(ref _showTree, value) && _last is not null)
            {
                Apply(_last);
            }
        }
    }

    public ProcessRowViewModel? Selected { get => _selected; set => SetProperty(ref _selected, value); }

    public int SuspicionCount { get; private set; }

    /// <summary>Image name of a PID at the last refresh; any thread.</summary>
    public string? TryGetName(int pid) => Volatile.Read(ref _names).TryGetValue(pid, out string? n) ? n : null;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        CancellationToken ct = _cts.Token;
        _loop = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(RefreshInterval);
            do
            {
                Snapshot? s = TryRead(Read, out string? error);
                if (s is null)
                {
                    _ = Application.Current?.Dispatcher.InvokeAsync(() => Summary = error!);
                    continue;
                }

                _ = Application.Current?.Dispatcher.InvokeAsync(() => Apply(s));
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(true);
        try
        {
            await (_loop ?? Task.CompletedTask).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
    }

    /// <summary>
    /// CSV of the rows currently shown (filter applied). No command line or user: the spec keeps potentially sensitive
    /// data out of exports by default. Unknown values are empty cells.
    /// </summary>
    internal static int WriteCsv(TextWriter w, IEnumerable<ProcessRowViewModel> rows)
    {
        static string? N(long? v) => v?.ToString(CultureInfo.InvariantCulture);
        return CsvTable.Write(
            w,
            ["name", "pid", "parent_pid", "started_utc", "cpu_percent_machine", "private_bytes", "working_set", "private_working_set", "threads", "handles", "io_read_bytes", "io_write_bytes", "image_path", "memory_growth"],
            rows.Select(r => (IReadOnlyList<string?>)
            [
                r.Name, r.Pid.ToString(CultureInfo.InvariantCulture), r.ParentPid.ToString(CultureInfo.InvariantCulture),
                r.Snapshot.StartTimeUtc?.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture),
                r.Cpu?.ToString("F2", CultureInfo.InvariantCulture), N(r.PrivateBytes), N(r.WorkingSet), N(r.PrivateWorkingSet),
                r.Threads.ToString(CultureInfo.InvariantCulture), N(r.Handles), N(r.IoRead), N(r.IoWrite), r.Snapshot.ImagePath, r.Growth.Length > 0 ? r.Growth : null,
            ]));
    }

    /// <summary>Runs one read; any failure except cancellation becomes a message, so the refresh loop never dies silently.</summary>
    internal static Snapshot? TryRead(Func<Snapshot> read, out string? error)
    {
        try
        {
            error = null;
            return read();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            error = $"Process enumeration failed ({ex.GetType().Name}): {ex.Message}";
            return null;
        }
    }

    private void Export()
    {
        if (_shell.AskSavePath("DiskScope-processes.csv") is not { } path)
        {
            return;
        }

        try
        {
            using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
            int n = WriteCsv(writer, Rows);
            Summary = Invariant($"Exported {n} processes to ") + path;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Summary = "Export failed: " + ex.Message;
        }
    }

    /// <summary>Enumeration and every computation that does not need the UI thread.</summary>
    internal Snapshot Read()
    {
        ProcessReadResult read = ProcessReader.ReadAll();
        long ts = Stopwatch.GetTimestamp();
        var names = new Dictionary<int, string>(read.Processes.Count);
        foreach (ProcessSnapshot p in read.Processes)
        {
            names.TryAdd(p.ProcessId, p.Name);
        }

        Volatile.Write(ref _names, names);
        DateTime now = DateTime.UtcNow;
        IReadOnlyDictionary<ProcessKey, double?> cpu = _cpu.Update(read.Processes, ts);
        foreach (ProcessSnapshot p in read.Processes)
        {
            if (p.PrivateBytes is { } bytes && p.Key.IsCreationTimeKnown)
            {
                _growth.Observe(p.Key, now, bytes);
            }
        }

        _growth.Forget(now - _growth.Window);
        var growth = _growth.Evaluate().ToDictionary(
            g => g.Key,
            g => Invariant($"+{MetricFormatter.FormatBytes(g.GrowthBytes)} in {(g.ToUtc - g.FromUtc).TotalMinutes:0} min: sustained growth, to verify (not a proven leak)"));
        return new Snapshot(read, cpu, ProcessTree.Build(read.Processes), growth);
    }

    /// <summary>UI thread. Rows are matched by key and moved, inserted or removed, never all recreated.</summary>
    internal void Apply(Snapshot s)
    {
        _last = s;
        string filter = _filterText.Trim();
        var ordered = new List<(ProcessSnapshot P, int Depth)>(s.Read.Processes.Count);
        if (filter.Length == 0 && _showTree)
        {
            var stack = new Stack<(ProcessSnapshot, int)>();
            foreach (ProcessSnapshot root in s.Tree.Roots.OrderByDescending(r => r.Name, StringComparer.OrdinalIgnoreCase))
            {
                stack.Push((root, 0));
            }

            while (stack.Count > 0)
            {
                (ProcessSnapshot p, int depth) = stack.Pop();
                ordered.Add((p, depth));
                foreach (ProcessSnapshot child in s.Tree.GetChildren(p.Key).OrderByDescending(c => c.Name, StringComparer.OrdinalIgnoreCase))
                {
                    stack.Push((child, depth + 1));
                }
            }
        }
        else
        {
            // Flat list, largest private bytes first; unknown memory goes last, never counted as 0.
            ordered.AddRange(s.Read.Processes
                .Where(p => filter.Length == 0
                    || p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || p.ProcessId.ToString(CultureInfo.InvariantCulture) == filter
                    || (p.ImagePath?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
                .OrderBy(p => p.PrivateBytes is null)
                .ThenByDescending(p => p.PrivateBytes ?? 0)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(p => (p, 0)));
        }

        long maxPrivate = ordered.Count == 0 ? 0 : ordered.Max(o => o.P.PrivateBytes ?? 0);

        var targetKeys = new HashSet<ProcessKey>(ordered.Select(o => o.P.Key));
        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            if (!targetKeys.Contains(Rows[i].Key))
            {
                _rows.Remove(Rows[i].Key);
                Rows.RemoveAt(i);
            }
        }

        for (int i = 0; i < ordered.Count; i++)
        {
            (ProcessSnapshot p, int depth) = ordered[i];
            if (!_rows.TryGetValue(p.Key, out ProcessRowViewModel? row))
            {
                _rows[p.Key] = row = new ProcessRowViewModel(p);
                Rows.Insert(i, row);
            }
            else
            {
                int at = Rows.IndexOf(row, i);
                if (at != i)
                {
                    Rows.Move(at, i);
                }
            }

            row.Update(p, depth, s.Cpu.TryGetValue(p.Key, out double? c) ? c : null, s.Growth.TryGetValue(p.Key, out string? g) ? g : string.Empty, maxPrivate);
        }

        SuspicionCount = s.Growth.Count;
        RaisePropertyChanged(nameof(SuspicionCount));
        Summary = Invariant($"{s.Read.Processes.Count} processes, {s.Read.Inaccessible} not openable (their memory, CPU and I/O read n/a), ") +
            Invariant($"read in {s.Read.Duration.TotalMilliseconds:F0} ms, {s.Tree.UnverifiedLinks.Count} parent links unverified, {s.Growth.Count} growth suspicions. ") +
            "CPU % is of the whole machine; private bytes = commit charge.";
    }

    internal sealed record Snapshot(
        ProcessReadResult Read,
        IReadOnlyDictionary<ProcessKey, double?> Cpu,
        ProcessTree Tree,
        IReadOnlyDictionary<ProcessKey, string> Growth);
}

internal static class CollectionExtensions
{
    public static int IndexOf<T>(this ObservableCollection<T> items, T item, int start)
    {
        for (int i = start; i < items.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(items[i], item))
            {
                return i;
            }
        }

        return items.IndexOf(item);
    }
}
