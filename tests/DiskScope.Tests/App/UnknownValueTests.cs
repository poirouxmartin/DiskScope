using System.IO;
using DiskScope.App.Controls;
using DiskScope.App.Formatting;
using DiskScope.App.ViewModels;
using DiskScope.Core;
using DiskScope.Storage;

namespace DiskScope.Tests.App;

/// <summary>An unknown value is shown as unknown ("n/a"), never as 0 or NaN.</summary>
public sealed class UnknownValueTests
{
    private static readonly DateTime T0 = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DefaultMetricValue_IsNotAvailable()
    {
        Assert.False(default(MetricValue).IsAvailable);
    }

    [Fact]
    public void MetricFormatter_NonFinite_IsUnknown()
    {
        Assert.Equal(MetricFormatter.Unknown, MetricFormatter.Format(double.NaN, MetricUnit.Percent));
        Assert.Equal(MetricFormatter.Unknown, MetricFormatter.Format(double.PositiveInfinity, MetricUnit.Bytes));
        Assert.Equal(MetricFormatter.Unknown, MetricFormatter.FormatBytes(double.NaN));
    }

    [Fact]
    public void CoreBar_Unknown_IsHidden()
    {
        var core = new CoreViewModel(0);

        core.Update(null);

        Assert.False(core.HasValue);
        Assert.Equal(MetricFormatter.Unknown, core.Text);

        core.Update(MetricValue.Derived("cpu.core.0", 42));

        Assert.True(core.HasValue);
        Assert.Equal(42, core.Percent);
    }

    [Fact]
    public void Sparkline_TryGetStart_DefaultEnd_ReturnsFalse()
    {
        Assert.False(SparklineControl.TryGetStart(default, TimeSpan.FromMinutes(1), out _));
        Assert.True(SparklineControl.TryGetStart(T0, TimeSpan.FromMinutes(1), out DateTime start));
        Assert.Equal(T0.AddMinutes(-1), start);
    }

    [Fact]
    public void Sparkline_FinitePoints_DropsNaN()
    {
        var points = new List<MetricPoint>
        {
            new(T0, 10, 10, 10, 1),
            new(T0.AddSeconds(1), double.NaN, double.NaN, double.NaN, 0),
            new(T0.AddSeconds(2), 30, 30, 30, 1),
        };

        IReadOnlyList<MetricPoint> finite = SparklineControl.FinitePoints(points);

        Assert.Equal(2, finite.Count);
        Assert.All(finite, p => Assert.True(double.IsFinite(p.Average)));
    }

    [Fact]
    public void ByteFormatter_FormatNodeSize_MarksLowerBoundsAndUnknown()
    {
        Assert.Equal("1.0 KB", ByteFormatter.FormatNodeSize(1024, NodeStatus.None));
        Assert.Equal("\u2265 1.0 KB", ByteFormatter.FormatNodeSize(1024, NodeStatus.SubtreeIncomplete));
        Assert.Equal(MetricFormatter.Unknown, ByteFormatter.FormatNodeSize(0, NodeStatus.AccessDenied));
    }

    [Fact]
    public void NodeViewModel_IncompleteDirectory_ShowsLowerBound()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\data");
        int locked = builder.Add(root, "locked", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        builder.Add(locked, "a.bin", NodeKind.File, FileAttributes.Normal, 1024, 4096, 0, 0);
        builder.AddError(new ScanError(locked, ScanErrorKind.AccessDenied, 5, "Access denied"));
        NodeIndex index = builder.Build();

        var vm = new NodeViewModel(index, locked, SizeKind.Logical, parentTotal: 0);

        Assert.StartsWith("\u2265 ", vm.TotalSizeText);
        Assert.Equal(MetricFormatter.Unknown, vm.PercentText);
    }
}
