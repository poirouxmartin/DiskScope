using System.Diagnostics;

namespace DiskScope.Monitoring;

/// <summary>
/// UTC timestamps that never go backwards: the wall clock is read once, then time advances with Stopwatch.
/// A wall-clock change (NTP, DST is irrelevant in UTC, manual change) therefore shifts nothing inside a session;
/// the drift against the wall clock is bounded by the Stopwatch accuracy over the session length.
/// </summary>
public sealed class MonitoringClock
{
    private readonly DateTime _anchorUtc;
    private readonly long _anchorTimestamp;

    public MonitoringClock()
        : this(DateTime.UtcNow, Stopwatch.GetTimestamp())
    {
    }

    internal MonitoringClock(DateTime anchorUtc, long anchorTimestamp)
    {
        _anchorUtc = DateTime.SpecifyKind(anchorUtc, DateTimeKind.Utc);
        _anchorTimestamp = anchorTimestamp;
    }

    public DateTime UtcNow => At(Stopwatch.GetTimestamp());

    internal DateTime At(long stopwatchTimestamp) =>
        _anchorUtc + Stopwatch.GetElapsedTime(_anchorTimestamp, stopwatchTimestamp);
}
