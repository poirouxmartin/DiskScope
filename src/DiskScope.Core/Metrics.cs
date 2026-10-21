namespace DiskScope.Core;

public enum MetricUnit
{
    Percent,
    Bytes,
    BytesPerSecond,
    Count,
    Megahertz,
    Celsius,
    Watts,
    Rpm,
    Milliseconds,
}

public enum MetricQuality
{
    /// <summary>Read from the source as is (for example a byte count).</summary>
    Measured,

    /// <summary>Computed from measured values (for example a percentage from two counter samples).</summary>
    Derived,

    /// <summary>No value: source missing, access denied, first sample of a delta, or collector failure. Never shown as zero.</summary>
    Unavailable,
}

/// <summary>Static description of a metric: what it is, in which unit, and where it comes from.</summary>
/// <param name="Id">Stable dotted key, for example "cpu.total" or "mem.commit.used".</param>
/// <param name="Source">The API or counter it is computed from, shown to the user.</param>
public sealed record MetricDescriptor(string Id, string DisplayName, MetricUnit Unit, string Source, string? Definition = null);

/// <summary>
/// One value of one metric. Invariant: <see cref="Value"/> is null exactly when <see cref="Quality"/> is Unavailable,
/// so an unknown value can never be read as zero by accident.
/// </summary>
public readonly record struct MetricValue
{
    private MetricValue(string id, double? value, MetricQuality quality)
    {
        Id = id;
        Value = value;
        Quality = quality;
    }

    public string Id { get; }

    public double? Value { get; }

    public MetricQuality Quality { get; }

    public bool IsAvailable => Quality != MetricQuality.Unavailable;

    public static MetricValue Measured(string id, double value) => Create(id, value, MetricQuality.Measured);

    public static MetricValue Derived(string id, double value) => Create(id, value, MetricQuality.Derived);

    public static MetricValue Unavailable(string id) => new(id, null, MetricQuality.Unavailable);

    private static MetricValue Create(string id, double value, MetricQuality quality)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        // NaN or infinity is a computation bug or a broken counter; it is reported as missing, not as a number.
        return double.IsFinite(value) ? new MetricValue(id, value, quality) : Unavailable(id);
    }
}

/// <summary>Error met by a collector during one collection. Never fatal for the other collectors.</summary>
public sealed record CollectorError(string Collector, string Message, int? Win32Error = null);

/// <summary>Whether a collector can run on this machine, and why not.</summary>
public sealed record CollectorAvailability(bool IsAvailable, string Reason)
{
    public static CollectorAvailability Available(string reason) => new(true, reason);

    public static CollectorAvailability NotAvailable(string reason) => new(false, reason);
}

/// <summary>
/// The values of one collection, published together.
/// <see cref="TimestampUtc"/> comes from a monotonic clock anchored once at start-up (see MonitoringClock),
/// so successive batches never go back in time when the wall clock is adjusted.
/// </summary>
public sealed record MetricBatch(
    string Collector,
    DateTime TimestampUtc,
    IReadOnlyList<MetricValue> Values,
    IReadOnlyList<CollectorError> Errors,
    TimeSpan CollectionDuration);
