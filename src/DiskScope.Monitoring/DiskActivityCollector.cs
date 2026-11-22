using DiskScope.Core;

namespace DiskScope.Monitoring;

/// <summary>
/// Physical disk activity from the PhysicalDisk performance counters (English names via PdhAddEnglishCounterW), for
/// all disks together (_Total instance). Rate and average counters need two collections: the first one publishes
/// them Unavailable (the instantaneous queue length is valid at once). "Active time" is 100 - % Idle Time, which, unlike % Disk Time, cannot exceed 100 % on queued I/O.
/// </summary>
public sealed class DiskActivityCollector : IMetricCollector, IDisposable
{
    public const string Active = MetricIds.DiskActive;
    public const string ReadRate = MetricIds.DiskRead;
    public const string WriteRate = MetricIds.DiskWrite;
    public const string Latency = MetricIds.DiskLatency;
    public const string Queue = MetricIds.DiskQueue;

    private const string Source = @"PDH \PhysicalDisk(_Total)";

    private readonly PdhQuery? _query;
    private readonly (string Id, nint Counter, Func<double, double> Map)[] _counters = [];
    private readonly string? _unavailable;

    public DiskActivityCollector()
    {
        try
        {
            _query = new PdhQuery();
            var list = new List<(string, nint, Func<double, double>)>();
            Add(list, Active, @"\PhysicalDisk(_Total)\% Idle Time", v => Math.Clamp(100 - v, 0, 100));
            Add(list, ReadRate, @"\PhysicalDisk(_Total)\Disk Read Bytes/sec", v => v);
            Add(list, WriteRate, @"\PhysicalDisk(_Total)\Disk Write Bytes/sec", v => v);
            Add(list, Latency, @"\PhysicalDisk(_Total)\Avg. Disk sec/Transfer", v => v * 1000);
            Add(list, Queue, @"\PhysicalDisk(_Total)\Current Disk Queue Length", v => v);
            _counters = list.ToArray();
            _unavailable = _counters.Length == 0 ? "PhysicalDisk performance counters not available (disabled or missing)" : null;
        }
        catch (InvalidOperationException ex)
        {
            _unavailable = ex.Message;
        }
    }

    public string Name => "Disk";

    public IReadOnlyList<MetricDescriptor> Metrics { get; } =
    [
        new(Active, "Disk active time", MetricUnit.Percent, Source + @"\% Idle Time", "100 - % idle time, all physical disks"),
        new(ReadRate, "Disk read", MetricUnit.BytesPerSecond, Source + @"\Disk Read Bytes/sec"),
        new(WriteRate, "Disk write", MetricUnit.BytesPerSecond, Source + @"\Disk Write Bytes/sec"),
        new(Latency, "Disk latency", MetricUnit.Milliseconds, Source + @"\Avg. Disk sec/Transfer", "Average time per transfer over the interval"),
        new(Queue, "Disk queue", MetricUnit.Count, Source + @"\Current Disk Queue Length", "Instantaneous, not averaged"),
    ];

    public TimeSpan DefaultInterval => TimeSpan.FromSeconds(1);

    public CollectorAvailability CheckAvailability() =>
        _unavailable is null ? CollectorAvailability.Available(Source) : CollectorAvailability.NotAvailable(_unavailable);

    public CollectorResult Collect(CancellationToken cancellationToken)
    {
        if (_query is null || _counters.Length == 0)
        {
            return CollectorResult.AllUnavailable(Metrics, Array.Empty<CollectorError>());
        }

        int status = _query.Collect();
        var errors = new List<CollectorError>();
        if (status != 0)
        {
            errors.Add(new CollectorError(Name, $"PdhCollectQueryData failed, 0x{status:X8}"));
        }

        var values = new List<MetricValue>(Metrics.Count);
        foreach (MetricDescriptor m in Metrics)
        {
            (string Id, nint Counter, Func<double, double> Map) c = Array.Find(_counters, x => x.Id == m.Id);
            List<(string Instance, double Value)> read = status == 0 && c.Counter != 0 ? _query.Read(c.Counter, noCap: true) : [];
            values.Add(read.Count > 0 ? MetricValue.Derived(m.Id, c.Map(read[0].Value)) : MetricValue.Unavailable(m.Id));
        }

        // Without any transfer in the interval the counter reports 0 s per transfer: that is "no data", not "instant".
        if (values.Find(v => v.Id == ReadRate).Value == 0 && values.Find(v => v.Id == WriteRate).Value == 0)
        {
            values[values.FindIndex(v => v.Id == Latency)] = MetricValue.Unavailable(Latency);
        }

        return new CollectorResult(values, errors);
    }

    public void Dispose() => _query?.Dispose();

    private void Add(List<(string, nint, Func<double, double>)> list, string id, string path, Func<double, double> map)
    {
        nint counter = _query!.TryAdd(path, out _);
        if (counter != 0)
        {
            list.Add((id, counter, map));
        }
    }
}
