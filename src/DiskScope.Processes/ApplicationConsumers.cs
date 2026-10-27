using System.Globalization;
using DiskScope.Core;
using static System.FormattableString;

namespace DiskScope.Processes;

/// <summary>What DiskScope can say about closing an application. Conservative: anything it cannot inspect gets no advice.</summary>
public enum ConsumerKind
{
    /// <summary>Every process was opened and runs from outside the Windows directory: closing it is the user's call.</summary>
    Application,

    /// <summary>At least one process runs from the Windows directory: part of the system, never suggested.</summary>
    WindowsComponent,

    /// <summary>At least one process refused the open (protected, elevated or system): its path is unknown, so no advice.</summary>
    NotInspectable,

    /// <summary>DiskScope itself.</summary>
    Self,
}

/// <summary>
/// Processes sharing one image name at one refresh. Sums cover the processes that could be read; when
/// <see cref="InaccessibleCount"/> is not 0 they are lower bounds. A sum is null when no member reported the value.
/// </summary>
/// <param name="CpuTrend">CPU % of the machine over the tracker window (sum of the members at each refresh).</param>
/// <param name="PrivateBytesTrend">Private bytes over the tracker window.</param>
public sealed record ApplicationConsumer(
    string Name,
    ConsumerKind Kind,
    int ProcessCount,
    int InaccessibleCount,
    long? PrivateBytes,
    long? PrivateWorkingSetBytes,
    long? WorkingSetBytes,
    double? CpuPercent,
    MetricTrend? CpuTrend,
    MetricTrend? PrivateBytesTrend);

/// <param name="FreeableRamBytes">Private working set of all <see cref="ConsumerKind.Application"/> groups: physical
/// memory their exit would return. Null when no process reported it (Windows builds before 22H2).</param>
/// <param name="FreeableCommitBytes">Private bytes of the same groups: commit charge their exit would return.</param>
/// <param name="OtherCommitBytes">Private bytes of every other group (Windows, not inspectable, DiskScope).</param>
public sealed record ConsumerReport(
    IReadOnlyList<ApplicationConsumer> Consumers,
    int ApplicationCount,
    long? FreeableRamBytes,
    long FreeableCommitBytes,
    long OtherCommitBytes);

/// <summary>
/// Groups processes by image name, keeps a short history per group, and classifies each group for the "what could be
/// freed" view. Read-only: it never stops anything. Private bytes and private working set are what a process returns
/// on exit; shared pages (DLLs, mapped files) and the file cache stay, so the estimate does not count them.
/// Not thread-safe: one caller (the process refresh loop).
/// </summary>
public sealed class ApplicationConsumerTracker
{
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(5);

    private readonly string _windowsDirectory;
    private readonly int _selfProcessId;
    private readonly Dictionary<string, Queue<Sample>> _history = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="windowsDirectory">For example <c>C:\Windows</c>; an image under it is a Windows component.</param>
    public ApplicationConsumerTracker(string windowsDirectory, int selfProcessId, TimeSpan? window = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowsDirectory);
        _windowsDirectory = windowsDirectory.TrimEnd('\\', '/') + "\\";
        _selfProcessId = selfProcessId;
        Window = window ?? DefaultWindow;
        if (Window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "Window must be positive.");
        }
    }

    public TimeSpan Window { get; }

    /// <summary>Groups one refresh, records it, and returns the groups by private bytes, largest first (unknown last).</summary>
    /// <param name="cpu">CPU % of the machine per process, as returned by <see cref="ProcessCpuTracker.Update"/>.</param>
    public ConsumerReport Update(IReadOnlyList<ProcessSnapshot> processes, IReadOnlyDictionary<ProcessKey, double?> cpu, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(cpu);

        var consumers = new List<ApplicationConsumer>();
        foreach (IGrouping<string, ProcessSnapshot> group in processes.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var members = group.ToList();
            long? privateBytes = Sum(members, p => p.PrivateBytes);
            double? cpuSum = null;
            foreach (ProcessSnapshot p in members)
            {
                if (cpu.TryGetValue(p.Key, out double? c) && c is { } value)
                {
                    cpuSum = (cpuSum ?? 0) + value;
                }
            }

            if (!_history.TryGetValue(group.Key, out Queue<Sample>? samples))
            {
                _history[group.Key] = samples = new Queue<Sample>();
            }

            samples.Enqueue(new Sample(nowUtc, cpuSum, privateBytes));
            consumers.Add(new ApplicationConsumer(
                group.Key,
                Classify(members, _windowsDirectory, _selfProcessId),
                members.Count,
                members.Count(p => !p.IsAccessible),
                privateBytes,
                Sum(members, p => p.PrivateWorkingSetBytes),
                Sum(members, p => p.WorkingSetBytes),
                cpuSum,
                Trend(samples, s => s.Cpu),
                Trend(samples, s => s.PrivateBytes)));
        }

        Forget(nowUtc - Window);
        consumers.Sort((a, b) =>
        {
            int bySize = (b.PrivateBytes ?? -1).CompareTo(a.PrivateBytes ?? -1);
            return bySize != 0 ? bySize : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
        });

        long? freeableRam = null;
        long freeableCommit = 0, otherCommit = 0;
        int applications = 0;
        foreach (ApplicationConsumer c in consumers)
        {
            if (c.Kind == ConsumerKind.Application)
            {
                applications++;
                freeableCommit += c.PrivateBytes ?? 0;
                if (c.PrivateWorkingSetBytes is { } ws)
                {
                    freeableRam = (freeableRam ?? 0) + ws;
                }
            }
            else
            {
                otherCommit += c.PrivateBytes ?? 0;
            }
        }

        return new ConsumerReport(consumers, applications, freeableRam, freeableCommit, otherCommit);
    }

    /// <summary>
    /// Most cautious kind wins: DiskScope, then Windows, then not inspectable. A group with one process that refused
    /// the open is not inspectable as a whole, because the same image name may run elevated for another purpose.
    /// </summary>
    internal static ConsumerKind Classify(IReadOnlyList<ProcessSnapshot> members, string windowsDirectoryWithSeparator, int selfProcessId)
    {
        if (members.Any(p => p.ProcessId == selfProcessId))
        {
            return ConsumerKind.Self;
        }

        if (members.Any(p => p.ImagePath?.StartsWith(windowsDirectoryWithSeparator, StringComparison.OrdinalIgnoreCase) == true))
        {
            return ConsumerKind.WindowsComponent;
        }

        return members.Any(p => !p.IsAccessible || p.ImagePath is null) ? ConsumerKind.NotInspectable : ConsumerKind.Application;
    }

    /// <summary>One line for the user. Never says a group is safe to end; for applications it states what exiting returns.</summary>
    public static string Advise(ApplicationConsumer c, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(c);
        switch (c.Kind)
        {
            case ConsumerKind.Self:
                return "DiskScope itself.";
            case ConsumerKind.WindowsComponent:
                return "Part of Windows: do not end it.";
            case ConsumerKind.NotInspectable:
                return "Could not be inspected without administrator rights: no advice.";
        }

        if (c.PrivateBytes is null)
        {
            return "Memory could not be read: no estimate.";
        }

        string text = Invariant($"Closing it returns about {MetricFormatter.Format(c.PrivateWorkingSetBytes, MetricUnit.Bytes)} of RAM and {MetricFormatter.FormatBytes(c.PrivateBytes.Value)} of commit. Save open work first.");
        if (c.CpuTrend is { } cpu && cpu.Average >= 5)
        {
            text += Invariant($" Used {cpu.Average:F1} % of the machine's CPU on average over {Minutes(cpu.Covered)}.");
        }

        if (c.PrivateBytesTrend is { Change: { } change } && change >= GrowthWorthMentioningBytes)
        {
            text += " Private memory rising: " + MetricTrend.FormatChange(change, MetricUnit.Bytes) + " per " + Minutes(window) + ".";
        }

        return text;
    }

    /// <summary>Below this a trend over a few minutes is mostly allocator noise.</summary>
    internal const double GrowthWorthMentioningBytes = 100 * 1024 * 1024;

    private static string Minutes(TimeSpan t) =>
        t.TotalMinutes >= 1 ? t.TotalMinutes.ToString("0", CultureInfo.InvariantCulture) + " min" : t.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s";

    private static long? Sum(List<ProcessSnapshot> members, Func<ProcessSnapshot, long?> value)
    {
        long? total = null;
        foreach (ProcessSnapshot p in members)
        {
            if (value(p) is { } v)
            {
                total = (total ?? 0) + v;
            }
        }

        return total;
    }

    private MetricTrend? Trend(Queue<Sample> samples, Func<Sample, double?> value)
    {
        var points = new List<MetricPoint>(samples.Count);
        foreach (Sample s in samples)
        {
            if (value(s) is { } v)
            {
                points.Add(new MetricPoint(s.TimestampUtc, v, v, v, 1));
            }
        }

        return MetricTrend.Compute(points, Window);
    }

    /// <summary>Drops samples older than the window, and groups with none left (applications that exited).</summary>
    private void Forget(DateTime cutoffUtc)
    {
        List<string>? empty = null;
        foreach ((string name, Queue<Sample> samples) in _history)
        {
            while (samples.Count > 0 && samples.Peek().TimestampUtc < cutoffUtc)
            {
                samples.Dequeue();
            }

            if (samples.Count == 0)
            {
                (empty ??= []).Add(name);
            }
        }

        empty?.ForEach(n => _history.Remove(n));
    }

    internal int TrackedGroupCount => _history.Count;

    private readonly record struct Sample(DateTime TimestampUtc, double? Cpu, long? PrivateBytes);
}
