using System.Linq;
using DiskScope.Core;

namespace DiskScope.Tests;

public sealed class CollectorResultTests
{
    [Fact]
    public void AllUnavailable_MarksEveryMetricUnavailableAndKeepsErrors()
    {
        MetricDescriptor[] metrics =
        [
            new("a.one", "One", MetricUnit.Percent, "src"),
            new("a.two", "Two", MetricUnit.Bytes, "src"),
        ];
        var error = new CollectorError("a", "boom");

        CollectorResult result = CollectorResult.AllUnavailable(metrics, [error]);

        Assert.Equal(new[] { "a.one", "a.two" }, result.Values.Select(v => v.Id));
        Assert.All(result.Values, v =>
        {
            Assert.False(v.IsAvailable);
            Assert.Null(v.Value);
        });
        Assert.Equal(error, Assert.Single(result.Errors));
    }
}
