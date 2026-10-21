namespace DiskScope.Scanner;

/// <summary>
/// Decides when an engine publishes a provisional index (see <see cref="ScanOptions.PartialResultInterval"/>).
/// Pure arithmetic on elapsed times so engines share it and tests do not need a clock.
/// </summary>
public sealed class PartialResultSchedule
{
    private const int MinCostMultiplier = 10;

    private readonly TimeSpan _max;
    private TimeSpan _interval;
    private TimeSpan _next;

    public PartialResultSchedule(ScanOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Enabled = options.PartialResultInterval is { } first && first > TimeSpan.Zero;
        _interval = options.PartialResultInterval ?? TimeSpan.Zero;
        _max = options.MaxPartialResultInterval;
        _next = _interval;
    }

    public bool Enabled { get; }

    public bool IsDue(TimeSpan elapsed) => Enabled && elapsed >= _next;

    /// <summary>Records a snapshot that ended at <paramref name="elapsed"/> and took <paramref name="cost"/>.</summary>
    public void Published(TimeSpan elapsed, TimeSpan cost)
    {
        _interval = TimeSpan.FromTicks(Math.Min(_interval.Ticks * 2, _max.Ticks));
        TimeSpan wait = TimeSpan.FromTicks(Math.Max(_interval.Ticks, cost.Ticks * MinCostMultiplier));
        _next = elapsed + wait;
    }
}
