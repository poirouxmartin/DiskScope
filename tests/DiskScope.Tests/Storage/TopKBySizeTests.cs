using DiskScope.Storage;

namespace DiskScope.Tests;

public sealed class TopKBySizeTests
{
    [Fact]
    public void ToSortedArray_OrdersBySizeDescendingThenAscendingId()
    {
        var top = new TopKBySize(10);
        top.Add(4, 10);
        top.Add(2, 50);
        top.Add(3, 10);
        top.Add(1, 50);

        Assert.Equal(new[] { 1, 2, 3, 4 }, top.ToSortedArray());
    }

    [Fact]
    public void Add_WhenFull_EvictsHighestIdAmongEqualSizes()
    {
        var top = new TopKBySize(2);
        top.Add(1, 10);
        top.Add(2, 10);
        top.Add(3, 10);
        top.Add(4, 5);
        top.Add(0, 10);

        Assert.Equal(new[] { 0, 1 }, top.ToSortedArray());
    }
}
