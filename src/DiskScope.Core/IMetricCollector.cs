namespace DiskScope.Core;

/// <summary>
/// Collects the metrics of one category (CPU, memory, GPU...).
/// Called from a scheduler thread, never from the UI thread; it never touches WPF.
/// <see cref="Collect"/> is called sequentially for a given instance, so delta-based collectors can keep
/// their previous sample in fields without locking.
/// </summary>
public interface IMetricCollector
{
    string Name { get; }

    /// <summary>Every metric this collector can publish. A metric it cannot read is published Unavailable, not omitted.</summary>
    IReadOnlyList<MetricDescriptor> Metrics { get; }

    /// <summary>Suggested period; the scheduler configuration may override it.</summary>
    TimeSpan DefaultInterval { get; }

    /// <summary>Cheap and side-effect free.</summary>
    CollectorAvailability CheckAvailability();

    /// <summary>
    /// One collection. Expected failures (access denied, missing counter, vanished process) are reported as
    /// Unavailable values plus <see cref="CollectorError"/> entries; exceptions are reserved for bugs, and the
    /// scheduler isolates them anyway.
    /// </summary>
    CollectorResult Collect(CancellationToken cancellationToken);
}

/// <summary>Values and errors of one collection; the scheduler adds the timestamp and the measured duration.</summary>
public sealed record CollectorResult(IReadOnlyList<MetricValue> Values, IReadOnlyList<CollectorError> Errors)
{
    public static CollectorResult Of(IReadOnlyList<MetricValue> values) => new(values, Array.Empty<CollectorError>());
}
