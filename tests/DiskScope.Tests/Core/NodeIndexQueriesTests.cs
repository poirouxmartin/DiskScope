using System.IO;
using System.Linq;
using DiskScope.Core;

namespace DiskScope.Tests;

public sealed class NodeIndexQueriesTests
{
    [Fact]
    public void GetTotalSize_ReturnsRequestedKind()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\");
        builder.Add(root, "tiny.txt", NodeKind.File, FileAttributes.Normal, 1, 4096, 0, 0);
        NodeIndex index = builder.Build();

        Assert.Equal(1L, index.GetTotalSize(root, SizeKind.Logical));
        Assert.Equal(4096L, index.GetTotalSize(root, SizeKind.Allocated));
    }

    [Fact]
    public void GetChildrenSortedBySize_OrdersBySizeThenNameIgnoringCase()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\sort");
        int beta = builder.Add(root, "Beta.bin", NodeKind.File, FileAttributes.Normal, 100, 4096, 0, 0);
        int alpha = builder.Add(root, "alpha.bin", NodeKind.File, FileAttributes.Normal, 100, 4096, 0, 0);
        int gamma = builder.Add(root, "Gamma.bin", NodeKind.File, FileAttributes.Normal, 300, 512, 0, 0);
        int dir = builder.Add(root, "dir", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        builder.Add(dir, "x", NodeKind.File, FileAttributes.Normal, 200, 4096, 0, 0);
        NodeIndex index = builder.Build();

        // Beta has the lower id, but "alpha.bin" wins the tie on name.
        Assert.Equal(new[] { gamma, dir, alpha, beta }, index.GetChildrenSortedBySize(root, SizeKind.Logical));

        // Allocated sizes order the children differently: the 512-byte file drops to the end.
        Assert.Equal(new[] { alpha, beta, dir, gamma }, index.GetChildrenSortedBySize(root, SizeKind.Allocated));
    }

    [Fact]
    public void GetLargestFiles_ReturnsOnlyFilesAcrossDirectoriesInSizeOrder()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\files");
        int dirA = builder.Add(root, "a", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        int dirB = builder.Add(root, "b", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        int dirC = builder.Add(root, "c", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        builder.Add(root, "link", NodeKind.Link, FileAttributes.ReparsePoint | FileAttributes.Directory, 10000, 10000, 0, 0);
        int a1 = builder.Add(dirA, "a1", NodeKind.File, FileAttributes.Normal, 5, 5, 0, 0);
        int a2 = builder.Add(dirA, "a2", NodeKind.File, FileAttributes.Normal, 900, 900, 0, 0);
        int a3 = builder.Add(dirA, "a3", NodeKind.File, FileAttributes.Normal, 40, 100000, 0, 0);
        int b1 = builder.Add(dirB, "b1", NodeKind.File, FileAttributes.Normal, 700, 700, 0, 0);
        int b2 = builder.Add(dirB, "b2", NodeKind.File, FileAttributes.Normal, 3, 3, 0, 0);
        int b3 = builder.Add(dirB, "b3", NodeKind.File, FileAttributes.Normal, 800, 800, 0, 0);
        int c1 = builder.Add(dirC, "c1", NodeKind.File, FileAttributes.Normal, 20, 20, 0, 0);
        int c2 = builder.Add(dirC, "c2", NodeKind.File, FileAttributes.Normal, 600, 600, 0, 0);
        int c3 = builder.Add(dirC, "c3", NodeKind.File, FileAttributes.Normal, 1, 1, 0, 0);
        int c4 = builder.Add(dirC, "c4", NodeKind.File, FileAttributes.Normal, 50, 50, 0, 0);
        NodeIndex index = builder.Build();

        // Directory totals (a: 945, b: 1503) and the 10000-byte link must not appear in the result.
        Assert.Equal(new[] { a2, b3, b1 }, index.GetLargestFiles(3, SizeKind.Logical));
        Assert.Equal(new[] { a3, a2, b3 }, index.GetLargestFiles(3, SizeKind.Allocated));
        Assert.Equal(new[] { a2, b3, b1, c2, c4, a3, c1, a1, b2, c3 }, index.GetLargestFiles(50, SizeKind.Logical));
        Assert.Empty(index.GetLargestFiles(0, SizeKind.Logical));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            index.GetLargestFiles(-1, SizeKind.Logical);
        });
    }

    [Fact]
    public void GetLargestFiles_TiesKeepLowestIds()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\ties");
        int first = builder.Add(root, "first", NodeKind.File, FileAttributes.Normal, 10, 10, 0, 0);
        int second = builder.Add(root, "second", NodeKind.File, FileAttributes.Normal, 10, 10, 0, 0);
        builder.Add(root, "third", NodeKind.File, FileAttributes.Normal, 10, 10, 0, 0);
        NodeIndex index = builder.Build();

        Assert.Equal(new[] { first, second }, index.GetLargestFiles(2, SizeKind.Logical));
    }

    [Fact]
    public void GetLargestFiles_100kFiles_MatchesLinqReference()
    {
        const int fileCount = 100000;
        const int top = 50;
        var builder = new NodeIndexBuilder(initialCapacity: 16);
        int root = builder.AddRoot("C:\\big");
        var sizes = new long[fileCount];
        for (int i = 0; i < fileCount; i++)
        {
            sizes[i] = (long)i * 7919 % 100003;
            builder.Add(root, $"f{i}", NodeKind.File, FileAttributes.Normal, sizes[i], sizes[i], 0, 0);
        }

        NodeIndex index = builder.Build();

        // File i is node id i + 1 (the root is id 0).
        int[] expected = Enumerable.Range(1, fileCount)
            .OrderByDescending(id => sizes[id - 1])
            .ThenBy(id => id)
            .Take(top)
            .ToArray();

        Assert.Equal(expected, index.GetLargestFiles(top, SizeKind.Logical));
    }
}
