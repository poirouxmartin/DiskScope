namespace DiskScope.Core;

/// <summary>
/// One stored point. Raw points have Min = Average = Max and Samples = 1; aggregated points summarize
/// <see cref="Samples"/> available raw values. Unavailable values are not stored: a missing period is a gap
/// (no point), never a zero.
/// </summary>
public readonly record struct MetricPoint(DateTime TimestampUtc, double Average, double Min, double Max, int Samples);

/// <summary>Persistence and query of timestamped metric values.</summary>
public interface IMetricStore
{
    void Append(MetricBatch batch);

    /// <summary>Points of one metric between two instants (inclusive), oldest first, at the finest resolution still retained.</summary>
    IReadOnlyList<MetricPoint> Query(string metricId, DateTime fromUtc, DateTime toUtc);
}
