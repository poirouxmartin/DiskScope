using DiskScope.Core;

namespace DiskScope.Tests.Monitoring;

public sealed class MetricTrendTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);

    private static MetricPoint At(double minutes, double value, int samples = 1) =>
        new(T0.AddMinutes(minutes), value, value, value, samples);

    [Fact]
    public void NoPoint_IsNull()
    {
        Assert.Null(MetricTrend.Compute([], TenMinutes));
    }

    [Fact]
    public void NonPositiveWindow_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MetricTrend.Compute([At(0, 1)], TimeSpan.Zero));
    }

    [Fact]
    public void LinearSeries_GivesExactChangeOverWindow()
    {
        // +3 per minute over 6 minutes of a 10-minute window: +30 over the window.
        MetricTrend trend = MetricTrend.Compute([At(0, 10), At(2, 16), At(4, 22), At(6, 28)], TenMinutes)!;

        Assert.Equal(30, trend.Change!.Value, 9);
        Assert.Equal(19, trend.Average, 9);
        Assert.Equal(10, trend.Min);
        Assert.Equal(28, trend.Max);
        Assert.Equal(TimeSpan.FromMinutes(6), trend.Covered);
    }

    [Fact]
    public void FlatSeries_HasZeroChange()
    {
        MetricTrend trend = MetricTrend.Compute([At(0, 5), At(3, 5), At(6, 5)], TenMinutes)!;

        Assert.Equal(0, trend.Change!.Value, 9);
    }

    [Fact]
    public void Gap_DoesNotBendTheLine()
    {
        // Missing minutes 2-6 are a gap, not zeros: the slope stays -1 per minute.
        MetricTrend trend = MetricTrend.Compute([At(0, 50), At(1, 49), At(7, 43), At(8, 42)], TenMinutes)!;

        Assert.Equal(-10, trend.Change!.Value, 9);
        Assert.Equal(42, trend.Min);
    }

    [Fact]
    public void ShortCoverage_HasStatsButNoChange()
    {
        // 4 minutes of a 10-minute window: under half, so no slope is extrapolated.
        MetricTrend trend = MetricTrend.Compute([At(0, 1), At(2, 2), At(4, 100)], TenMinutes)!;

        Assert.Null(trend.Change);
        Assert.Equal(100, trend.Max);
    }

    [Fact]
    public void TwoPoints_HaveNoChange_EvenOverTheWholeWindow()
    {
        Assert.Null(MetricTrend.Compute([At(0, 1), At(10, 2)], TenMinutes)!.Change);
    }

    [Fact]
    public void AggregatedPoints_WeighByTheirSampleCount()
    {
        // A 9-sample aggregate at 10 and one raw point at 100: the average is (90 + 100) / 10.
        MetricTrend trend = MetricTrend.Compute([At(0, 10, samples: 9), At(1, 100)], TimeSpan.FromMinutes(1))!;

        Assert.Equal(19, trend.Average, 9);
    }

    [Fact]
    public void MinAndMax_ComeFromAggregateExtremes_NotAverages()
    {
        var aggregate = new MetricPoint(T0, Average: 50, Min: 2, Max: 99, Samples: 60);

        MetricTrend trend = MetricTrend.Compute([aggregate], TenMinutes)!;

        Assert.Equal(2, trend.Min);
        Assert.Equal(99, trend.Max);
        Assert.Null(trend.Change);
    }

    [Theory]
    [InlineData(4.04, MetricUnit.Percent, "+4.0 pts")]
    [InlineData(-0.5, MetricUnit.Percent, "-0.5 pts")]
    [InlineData(0, MetricUnit.Percent, "+0.0 pts")]
    [InlineData(-1536.0 * 1024 * 1024, MetricUnit.Bytes, "-1.5 GB")]
    [InlineData(12, MetricUnit.Count, "+12")]
    public void FormatChange_IsSignedAndUsesPointsForPercent(double change, MetricUnit unit, string expected)
    {
        Assert.Equal(expected, MetricTrend.FormatChange(change, unit));
    }
}
