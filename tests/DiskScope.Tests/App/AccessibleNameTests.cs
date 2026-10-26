using DiskScope.App.ViewModels;
using DiskScope.Core;

namespace DiskScope.Tests.App;

/// <summary>
/// WPF gives a DataGrid row or ItemsControl item the UI Automation name ToString() returns. Without an override,
/// screen readers and UI tests read the view model's type name.
/// </summary>
public sealed class AccessibleNameTests
{
    [Fact]
    public void CoreViewModel_IsNamedByItsLabel()
    {
        Assert.Equal("CPU 3", new CoreViewModel(3).ToString());
    }

    [Fact]
    public void MetricRowViewModel_IsNamedByItsMetric()
    {
        var row = new MetricRowViewModel(new MetricDescriptor("cpu.total", "CPU usage", MetricUnit.Percent, "test"));

        Assert.Equal("CPU usage", row.ToString());
    }
}
