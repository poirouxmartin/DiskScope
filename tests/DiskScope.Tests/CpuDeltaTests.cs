using DiskScope.Core;

namespace DiskScope.Tests;

public sealed class CpuDeltaTests
{
    [Theory]
    [InlineData(0L)]
    [InlineData(-5L)]
    public void Percent_NonPositiveWallDelta_IsNull(long wall)
    {
        Assert.Null(CpuDelta.Percent(100, wall, 4));
    }

    [Fact]
    public void Percent_NegativeCpuDelta_IsNull()
    {
        Assert.Null(CpuDelta.Percent(-1, 1000, 4));
    }

    [Fact]
    public void Percent_ZeroCpuDelta_IsZeroNotNull()
    {
        Assert.Equal(0.0, CpuDelta.Percent(0, 1000, 4));
    }

    [Fact]
    public void Percent_DividesByWallTimeAndProcessorCount()
    {
        Assert.Equal(25.0, CpuDelta.Percent(1000, 1000, 4));
    }

    [Fact]
    public void Percent_ClampsAt100()
    {
        Assert.Equal(100.0, CpuDelta.Percent(5000, 1000, 4));
    }
}
