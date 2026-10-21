using System;
using System.IO;
using System.Threading;
using DiskScope.Storage;

namespace DiskScope.Tests;

public sealed class NodeSearchTests
{
    [Fact]
    public void NameContains_IsCaseInsensitiveSubstring()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\search");
        int report = AddNode(builder, root, "Annual-REPORT.pdf", NodeKind.File, 10, 10);
        AddNode(builder, root, "notes.txt", NodeKind.File, 20, 20);
        NodeIndex index = builder.Build();

        SearchResult result = NodeSearch.Find(index, new NodeFilter { NameContains = "report" }, 10);

        Assert.Equal(new[] { report }, result.Ids);
        Assert.Equal(1, result.TotalMatches);
    }

    [Fact]
    public void Extensions_AreNormalizedAndDirectoriesExcluded()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\search");
        int jpg = AddNode(builder, root, "a.jpg", NodeKind.File, 10, 10);
        int png = AddNode(builder, root, "b.PNG", NodeKind.File, 20, 20);
        AddNode(builder, root, "c.gif", NodeKind.File, 30, 30);
        AddNode(builder, root, "photos.jpg", NodeKind.Directory);
        NodeIndex index = builder.Build();

        SearchResult result = NodeSearch.Find(index, new NodeFilter { Extensions = new[] { ".JPG", "png" } }, 10);

        Assert.Equal(new[] { png, jpg }, result.Ids);
        Assert.Equal(2, result.TotalMatches);
    }

    [Fact]
    public void EmptyExtension_MatchesOnlyFilesWithoutExtension()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\search");
        int readme = AddNode(builder, root, "README", NodeKind.File, 5, 5);
        int gitignore = AddNode(builder, root, ".gitignore", NodeKind.File, 3, 3);
        AddNode(builder, root, "a.txt", NodeKind.File, 7, 7);
        NodeIndex index = builder.Build();

        SearchResult result = NodeSearch.Find(index, new NodeFilter { Extensions = new[] { "" } }, 10);

        Assert.Equal(new[] { readme, gitignore }, result.Ids);
        Assert.Equal(2, result.TotalMatches);
    }

    [Fact]
    public void SizeBounds_AreInclusive_ForLogicalAndAllocated()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\search");
        AddNode(builder, root, "s99", NodeKind.File, 99, 4096);
        int s100 = AddNode(builder, root, "s100", NodeKind.File, 100, 4096);
        int s200 = AddNode(builder, root, "s200", NodeKind.File, 200, 4096);
        AddNode(builder, root, "s201", NodeKind.File, 201, 4096);
        NodeIndex index = builder.Build();

        SearchResult logical = NodeSearch.Find(index, new NodeFilter { MinSize = 100, MaxSize = 200 }, 10);
        Assert.Equal(new[] { s200, s100 }, logical.Ids);

        // Every file is 4096 allocated: the bounds apply to allocated size, so all four match at exactly 4096.
        SearchResult allocated = NodeSearch.Find(index, new NodeFilter { MinSize = 4096, MaxSize = 4096, SizeKind = SizeKind.Allocated }, 10);
        Assert.Equal(4, allocated.TotalMatches);

        SearchResult allocatedOutOfRange = NodeSearch.Find(index, new NodeFilter { MinSize = 100, MaxSize = 200, SizeKind = SizeKind.Allocated }, 10);
        Assert.Empty(allocatedOutOfRange.Ids);
    }

    [Fact]
    public void ModifiedRange_IsInclusive_AndExcludesUnknownDates()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\search");
        AddNode(builder, root, "before", NodeKind.File, 1, 1, UtcDate(2019, 12, 31).Ticks);
        int from = AddNode(builder, root, "from", NodeKind.File, 2, 2, UtcDate(2020, 1, 1).Ticks);
        int mid = AddNode(builder, root, "mid", NodeKind.File, 3, 3, UtcDate(2020, 6, 1).Ticks);
        int to = AddNode(builder, root, "to", NodeKind.File, 4, 4, UtcDate(2020, 12, 31).Ticks);
        AddNode(builder, root, "after", NodeKind.File, 5, 5, UtcDate(2021, 1, 1).Ticks);
        AddNode(builder, root, "unknown", NodeKind.File, 6, 6, 0);
        NodeIndex index = builder.Build();

        SearchResult result = NodeSearch.Find(
            index,
            new NodeFilter { ModifiedFromUtc = UtcDate(2020, 1, 1), ModifiedToUtc = UtcDate(2020, 12, 31) },
            10);

        Assert.Equal(new[] { to, mid, from }, result.Ids);
        Assert.Equal(3, result.TotalMatches);
        Assert.Equal(1, result.UnknownDateExcluded);

        SearchResult unfiltered = NodeSearch.Find(index, new NodeFilter(), 10);
        Assert.Equal(6, unfiltered.TotalMatches);
        Assert.Equal(0, unfiltered.UnknownDateExcluded);
    }

    [Fact]
    public void KindFilter_SelectsFilesOrDirectories_AndNeverLinks()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\search");
        int dir = AddNode(builder, root, "dir", NodeKind.Directory);
        int file = AddNode(builder, root, "file", NodeKind.File, 10, 10);
        AddNode(builder, root, "link", NodeKind.Link, 50, 50);
        NodeIndex index = builder.Build();

        Assert.Equal(new[] { file, dir }, NodeSearch.Find(index, new NodeFilter(), 10).Ids);
        Assert.Equal(new[] { file }, NodeSearch.Find(index, new NodeFilter { Kind = KindFilter.Files }, 10).Ids);
        Assert.Equal(new[] { dir }, NodeSearch.Find(index, new NodeFilter { Kind = KindFilter.Directories }, 10).Ids);
    }

    [Fact]
    public void CombinedCriteria_MustAllHold()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\search");
        int match = AddNode(builder, root, "annual-report.pdf", NodeKind.File, 500, 4096, UtcDate(2020, 6, 1).Ticks);
        AddNode(builder, root, "report.txt", NodeKind.File, 500, 4096, UtcDate(2020, 6, 1).Ticks);
        AddNode(builder, root, "annual-notes.pdf", NodeKind.File, 500, 4096, UtcDate(2020, 6, 1).Ticks);
        AddNode(builder, root, "annual-report-old.pdf", NodeKind.File, 400, 4096, UtcDate(2018, 6, 1).Ticks);
        AddNode(builder, root, "small-report.pdf", NodeKind.File, 50, 4096, UtcDate(2020, 6, 1).Ticks);
        NodeIndex index = builder.Build();

        var filter = new NodeFilter
        {
            NameContains = "report",
            Extensions = new[] { "PDF" },
            MinSize = 100,
            ModifiedFromUtc = UtcDate(2020, 1, 1),
            Kind = KindFilter.Files,
        };
        SearchResult result = NodeSearch.Find(index, filter, 10);

        Assert.Equal(new[] { match }, result.Ids);
        Assert.Equal(1, result.TotalMatches);
        Assert.Equal(0, result.UnknownDateExcluded);
    }

    [Fact]
    public void MaxResults_TruncatesBySizeWithTieOnAscendingId_AndReportsTotal()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\search");
        AddNode(builder, root, "a", NodeKind.File, 10, 10);
        int b = AddNode(builder, root, "b", NodeKind.File, 50, 50);
        AddNode(builder, root, "c", NodeKind.File, 30, 30);
        int d = AddNode(builder, root, "d", NodeKind.File, 50, 50);
        AddNode(builder, root, "e", NodeKind.File, 20, 20);
        NodeIndex index = builder.Build();

        SearchResult result = NodeSearch.Find(index, new NodeFilter(), 2);

        Assert.Equal(new[] { b, d }, result.Ids);
        Assert.Equal(5, result.TotalMatches);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void MaxResults_Zero_ReturnsNoIdsButCountsMatches()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\search");
        AddNode(builder, root, "a", NodeKind.File, 10, 10);
        AddNode(builder, root, "b", NodeKind.File, 20, 20);
        NodeIndex index = builder.Build();

        SearchResult result = NodeSearch.Find(index, new NodeFilter(), 0);

        Assert.Empty(result.Ids);
        Assert.Equal(2, result.TotalMatches);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void MaxResults_Negative_Throws()
    {
        var builder = new NodeIndexBuilder();
        builder.AddRoot("C:\\search");
        NodeIndex index = builder.Build();

        Assert.Throws<ArgumentOutOfRangeException>(() => { NodeSearch.Find(index, new NodeFilter(), -1); });
    }

    [Fact]
    public void Find_PreCancelledToken_ThrowsOnLargeIndex()
    {
        // Cancellation is checked every 65536 nodes, so the index needs more than that.
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\many");
        for (int i = 0; i < 70_000; i++)
        {
            string name = $"f{i}.bin";
            AddNode(builder, root, name, NodeKind.File, 1, 1);
        }

        NodeIndex index = builder.Build();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => { NodeSearch.Find(index, new NodeFilter(), 10, cts.Token); });
    }

    [Fact]
    public void GetExtension_ReturnsTextAfterLastInnerDot()
    {
        Assert.Equal("gz", NodeSearch.GetExtension("a.tar.gz").ToString());
        Assert.Equal("", NodeSearch.GetExtension(".gitignore").ToString());
        Assert.Equal("", NodeSearch.GetExtension("a.").ToString());
        Assert.Equal("", NodeSearch.GetExtension("noext").ToString());
    }

    private static int AddNode(
        NodeIndexBuilder builder,
        int parent,
        string name,
        NodeKind kind,
        long logical = 0,
        long allocated = 0,
        long ticks = 0)
    {
        FileAttributes attributes = kind switch
        {
            NodeKind.File => FileAttributes.Normal,
            NodeKind.Link => FileAttributes.ReparsePoint | FileAttributes.Directory,
            _ => FileAttributes.Directory,
        };
        return builder.Add(parent, name, kind, attributes, logical, allocated, ticks, 0);
    }

    private static DateTime UtcDate(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);
}
