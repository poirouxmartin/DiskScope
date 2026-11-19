using System.Collections.Frozen;
using System.Globalization;
using DiskScope.Core;
using DiskScope.Monitoring;

namespace DiskScope.Monitoring.Gpu;

/// <summary>
/// Per-adapter GPU engines and memory from the Windows GPU performance counters (WDDM 2.0+, the source of the Task
/// Manager GPU tab), joined to DXGI for the adapter name and capacities by LUID.
/// <list type="bullet">
/// <item>Engines are reported by type (3D, Copy, Video decode/encode/processing, Compute, Other) and never summed:
/// the overall figure is the busiest engine, as in Task Manager.</item>
/// <item>An engine type never seen on an adapter is Unavailable. Once seen, a sample without any instance of it
/// means no process holds a context on it, reported as 0 %.</item>
/// <item>Dedicated and shared usage are separate: shared GPU memory is system RAM, not VRAM.</item>
/// <item>Temperature, clocks, power and fans are not published: these counters do not expose them and no
/// vendor-neutral documented user-mode source was validated yet (docs/GPU-COMPATIBILITY.md).</item>
/// </list>
/// The first collection has no rate data, so engine values are Unavailable until the second.
/// </summary>
public sealed class GpuCollector : IMetricCollector, IDisposable
{
    public static readonly IReadOnlyList<string> EngineGroups = ["3d", "copy", "videodecode", "videoencode", "videoprocessing", "compute", "other"];

    private const string EngineSource = @"PDH \GPU Engine(*)\Utilization Percentage";
    private const string MemorySource = @"PDH \GPU Adapter Memory(*)";
    private const string DxgiSource = "DXGI IDXGIAdapter1::GetDesc1";

    private readonly PdhQuery? _query;
    private readonly nint _engineCounter;
    private readonly nint _dedicatedCounter;
    private readonly nint _sharedCounter;
    private readonly string? _unavailableReason;
    private readonly Dictionary<string, int> _indexByLuid;
    private readonly HashSet<(int Adapter, string Group)> _seenGroups = new();
    private FrozenDictionary<int, double> _processUsage = FrozenDictionary<int, double>.Empty;

    public GpuCollector()
    {
        Adapters = SafeEnumerate().Where(a => !a.IsSoftware).ToArray();
        _indexByLuid = Adapters.Select((a, i) => (a.Luid, i)).ToDictionary(x => x.Luid, x => x.i, StringComparer.Ordinal);
        Metrics = Adapters.SelectMany((a, i) => DescribeAdapter(a, i)).ToArray();

        if (Adapters.Count == 0)
        {
            _unavailableReason = "No hardware adapter reported by DXGI";
            return;
        }

        try
        {
            _query = new PdhQuery();
            _engineCounter = _query.TryAdd(@"\GPU Engine(*)\Utilization Percentage", out int status);
            if (_engineCounter == 0)
            {
                _unavailableReason = $"GPU Engine performance counters not available (PDH 0x{status:X8}); they need WDDM 2.0 or later";
                return;
            }

            _dedicatedCounter = _query.TryAdd(@"\GPU Adapter Memory(*)\Dedicated Usage", out _);
            _sharedCounter = _query.TryAdd(@"\GPU Adapter Memory(*)\Shared Usage", out _);
        }
        catch (InvalidOperationException ex)
        {
            _unavailableReason = ex.Message;
        }
    }

    public string Name => "GPU";

    public IReadOnlyList<GpuAdapterInfo> Adapters { get; }

    public IReadOnlyList<MetricDescriptor> Metrics { get; }

    public TimeSpan DefaultInterval => TimeSpan.FromSeconds(1);

    /// <summary>Busiest-engine utilization per PID at the last collection (all adapters); the counters attribute it per process.</summary>
    public IReadOnlyDictionary<int, double> ProcessUsage => Volatile.Read(ref _processUsage);

    public static string UtilizationId(int adapter) => Id(adapter, "util");

    public static string EngineId(int adapter, string group) => Id(adapter, "engine." + group);

    public static string DedicatedUsedId(int adapter) => Id(adapter, "dedicated.used");

    public static string DedicatedTotalId(int adapter) => Id(adapter, "dedicated.total");

    public static string SharedUsedId(int adapter) => Id(adapter, "shared.used");

    public static string SharedLimitId(int adapter) => Id(adapter, "shared.limit");

    /// <summary>Maps a counter engine type ("3D", "Compute_1", "VideoDecode"...) to the published group.</summary>
    public static string GroupOf(string engineType)
    {
        string t = engineType.ToLowerInvariant();
        if (t.StartsWith("compute", StringComparison.Ordinal))
        {
            return "compute";
        }

        return EngineGroups.Contains(t) ? t : "other";
    }

    public CollectorAvailability CheckAvailability() =>
        _unavailableReason is null
            ? CollectorAvailability.Available(string.Join(", ", Adapters.Select(a => a.Name)))
            : CollectorAvailability.NotAvailable(_unavailableReason);

    public CollectorResult Collect(CancellationToken cancellationToken)
    {
        var values = new List<MetricValue>(Metrics.Count);
        var errors = new List<CollectorError>();
        if (_query is null || _engineCounter == 0)
        {
            return CollectorResult.AllUnavailable(Metrics, errors);
        }

        int status = _query.Collect();
        if (status != 0)
        {
            errors.Add(new CollectorError(Name, $"PdhCollectQueryData failed, 0x{status:X8}"));
            return CollectorResult.AllUnavailable(Metrics, errors);
        }

        var samples = new List<(GpuEngineInstance, double)>();
        foreach ((string instance, double value) in _query.Read(_engineCounter))
        {
            if (GpuEngineInstance.TryParse(instance, out GpuEngineInstance parsed))
            {
                samples.Add((parsed, value));
            }
        }

        Volatile.Write(ref _processUsage, GpuCounterMath.AggregateProcesses(samples).ToFrozenDictionary());
        bool haveRates = samples.Count > 0;
        var byGroup = new Dictionary<(int, string), double>();
        foreach (GpuEngineUsage u in GpuCounterMath.AggregateEngines(samples))
        {
            if (_indexByLuid.TryGetValue(u.Luid, out int adapter))
            {
                var key = (adapter, GroupOf(u.EngineType));
                byGroup[key] = Math.Max(byGroup.GetValueOrDefault(key), u.Percent);
                _seenGroups.Add(key);
            }
        }

        Dictionary<int, double> dedicated = SumByAdapter(_dedicatedCounter);
        Dictionary<int, double> shared = SumByAdapter(_sharedCounter);

        for (int a = 0; a < Adapters.Count; a++)
        {
            double? overall = null;
            foreach (string group in EngineGroups)
            {
                MetricValue v = !haveRates || !_seenGroups.Contains((a, group))
                    ? MetricValue.Unavailable(EngineId(a, group))
                    : MetricValue.Derived(EngineId(a, group), byGroup.GetValueOrDefault((a, group)));
                values.Add(v);
                if (v.Value is { } x)
                {
                    overall = Math.Max(overall ?? 0, x);
                }
            }

            values.Add(overall is { } o ? MetricValue.Derived(UtilizationId(a), o) : MetricValue.Unavailable(UtilizationId(a)));
            values.Add(dedicated.TryGetValue(a, out double d) ? MetricValue.Measured(DedicatedUsedId(a), d) : MetricValue.Unavailable(DedicatedUsedId(a)));
            values.Add(MetricValue.Measured(DedicatedTotalId(a), Adapters[a].DedicatedVideoMemory));
            values.Add(shared.TryGetValue(a, out double s) ? MetricValue.Measured(SharedUsedId(a), s) : MetricValue.Unavailable(SharedUsedId(a)));
            values.Add(MetricValue.Measured(SharedLimitId(a), Adapters[a].SharedSystemMemory));
        }

        return new CollectorResult(values, errors);
    }

    public void Dispose() => _query?.Dispose();

    private Dictionary<int, double> SumByAdapter(nint counter)
    {
        var result = new Dictionary<int, double>();
        if (counter == 0 || _query is null)
        {
            return result;
        }

        foreach ((string instance, double value) in _query.Read(counter, noCap: true))
        {
            if (GpuCounterMath.LuidOfAdapterInstance(instance) is { } luid && _indexByLuid.TryGetValue(luid, out int a))
            {
                result[a] = result.GetValueOrDefault(a) + value;
            }
        }

        return result;
    }

    private static IEnumerable<MetricDescriptor> DescribeAdapter(GpuAdapterInfo a, int i)
    {
        string p = AdapterLabel(i, a.Name);
        yield return new MetricDescriptor(UtilizationId(i), $"{p} usage", MetricUnit.Percent, EngineSource, "Busiest engine (engines run in parallel and are not summed)");
        foreach (string g in EngineGroups)
        {
            yield return new MetricDescriptor(EngineId(i, g), $"{p} {g}", MetricUnit.Percent, EngineSource, "Busiest engine of this type; sum over processes per engine");
        }

        yield return new MetricDescriptor(DedicatedUsedId(i), $"{p} dedicated memory used", MetricUnit.Bytes, MemorySource + @"\Dedicated Usage");
        yield return new MetricDescriptor(DedicatedTotalId(i), $"{p} dedicated memory", MetricUnit.Bytes, DxgiSource, "DedicatedVideoMemory; on integrated GPUs only a small carve-out");
        yield return new MetricDescriptor(SharedUsedId(i), $"{p} shared memory used", MetricUnit.Bytes, MemorySource + @"\Shared Usage", "System RAM used by the GPU, not VRAM");
        yield return new MetricDescriptor(SharedLimitId(i), $"{p} shared memory limit", MetricUnit.Bytes, DxgiSource, "SharedSystemMemory");
    }

    private static string AdapterLabel(int i, string name) => string.Create(CultureInfo.InvariantCulture, $"GPU {i} ({name})");

    private static string Id(int adapter, string suffix) => string.Create(CultureInfo.InvariantCulture, $"gpu.{adapter}.{suffix}");

    private static IReadOnlyList<GpuAdapterInfo> SafeEnumerate()
    {
        try
        {
            return Dxgi.EnumerateAdapters();
        }
        catch (DllNotFoundException)
        {
            return Array.Empty<GpuAdapterInfo>();
        }
    }
}
