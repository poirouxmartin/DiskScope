using System.Globalization;

namespace DiskScope.Core;

/// <summary>
/// Summary of one metric over a window of stored points: sample-weighted average, extremes, and the change over
/// the window estimated by a least-squares line through the point averages (weighted by their sample count).
/// Gaps are not filled: the line uses the real timestamps of the points that exist.
/// </summary>
/// <param name="Change">Slope of the line times the window length, in the metric unit; null when the points cover
/// too little of the window for a slope to mean anything (see <see cref="Compute"/>).</param>
/// <param name="Covered">Time between the first and the last point.</param>
public sealed record MetricTrend(double Average, double Min, double Max, double? Change, TimeSpan Covered)
{
    /// <summary>A slope needs at least this many points.</summary>
    public const int MinPointsForChange = 3;

    /// <summary>
    /// Null when there is no point. <see cref="Change"/> is null unless there are at least
    /// <see cref="MinPointsForChange"/> points spanning at least half the window: a line through a few seconds of a
    /// ten-minute window would extrapolate noise.
    /// </summary>
    public static MetricTrend? Compute(IReadOnlyList<MetricPoint> points, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "Window must be positive.");
        }

        if (points.Count == 0)
        {
            return null;
        }

        DateTime origin = points[0].TimestampUtc;
        double weight = 0, sumX = 0, sumY = 0, min = double.PositiveInfinity, max = double.NegativeInfinity;
        foreach (MetricPoint p in points)
        {
            double w = Math.Max(1, p.Samples);
            weight += w;
            sumX += w * (p.TimestampUtc - origin).TotalMinutes;
            sumY += w * p.Average;
            min = Math.Min(min, p.Min);
            max = Math.Max(max, p.Max);
        }

        double meanX = sumX / weight;
        double meanY = sumY / weight;
        TimeSpan covered = points[^1].TimestampUtc - origin;

        double? change = null;
        if (points.Count >= MinPointsForChange && covered.Ticks * 2 >= window.Ticks)
        {
            double sxx = 0, sxy = 0;
            foreach (MetricPoint p in points)
            {
                double w = Math.Max(1, p.Samples);
                double dx = (p.TimestampUtc - origin).TotalMinutes - meanX;
                sxx += w * dx * dx;
                sxy += w * dx * (p.Average - meanY);
            }

            change = sxx > 0 ? sxy / sxx * window.TotalMinutes : null;
        }

        return new MetricTrend(meanY, min, max, change, covered);
    }

    /// <summary>
    /// Signed change in the metric unit, for example "+1.3 GB" or "-0.4 pts". Percent metrics change by percentage
    /// points, so they read "pts" rather than "%" (from 40 % to 44 % is +4 pts, not +10 %).
    /// </summary>
    public static string FormatChange(double change, MetricUnit unit)
    {
        string sign = change < 0 ? "-" : "+";
        double magnitude = Math.Abs(change);
        string text = unit == MetricUnit.Percent
            ? magnitude.ToString("F1", CultureInfo.InvariantCulture) + " pts"
            : MetricFormatter.Format(magnitude, unit);
        return sign + text;
    }
}
