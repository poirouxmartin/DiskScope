using System;
using DiskScope.App.Controls;
using DiskScope.App.ViewModels;
using DiskScope.Core;

namespace DiskScope.Tests.App;

public sealed class GaugeTests
{
    private static MetricTileViewModel Tile(MetricUnit unit) =>
        new(new MetricDescriptor("test.metric", "Test", unit, "unit test"), TimeSpan.FromMinutes(1));

    [Theory]
    [InlineData(null, 0)]
    [InlineData(0.0, 0)]
    [InlineData(0.1, 1)]
    [InlineData(50.0, 10)]
    [InlineData(82.0, 16)]
    [InlineData(100.0, 20)]
    [InlineData(140.0, 20)]
    [InlineData(-5.0, 0)]
    public void LitSegments_RoundsAndClamps(double? percent, int expected) =>
        Assert.Equal(expected, SegmentGaugeControl.LitSegments(percent, 20));

    [Fact]
    public void LitSegments_NaN_IsUnlit() => Assert.Equal(0, SegmentGaugeControl.LitSegments(double.NaN, 20));

    [Theory]
    [InlineData(79.9, 0)]
    [InlineData(80.0, 1)]
    [InlineData(89.9, 1)]
    [InlineData(90.0, 2)]
    public void Tile_PercentGauge_FollowsThresholds(double value, int level)
    {
        var tile = Tile(MetricUnit.Percent);
        tile.Update(MetricValue.Measured("test.metric", value), Array.Empty<MetricPoint>(), DateTime.UtcNow, 100);

        Assert.Equal(value, tile.GaugePercent);
        Assert.Equal(level, tile.GaugeLevel);
    }

    [Fact]
    public void Tile_UnknownValue_HasNoGauge()
    {
        var tile = Tile(MetricUnit.Percent);
        tile.Update(MetricValue.Unavailable("test.metric"), Array.Empty<MetricPoint>(), DateTime.UtcNow, 100);

        Assert.Null(tile.GaugePercent);
        Assert.Equal(0, tile.GaugeLevel);
        Assert.Equal(MetricFormatter.Unknown, tile.ValueText);
    }

    [Fact]
    public void Tile_NonPercentUnit_HasNoGauge()
    {
        var tile = Tile(MetricUnit.Bytes);
        tile.Update(MetricValue.Measured("test.metric", 1024), Array.Empty<MetricPoint>(), DateTime.UtcNow, double.NaN);

        Assert.Null(tile.GaugePercent);
    }
}
