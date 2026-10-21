using System.IO;
using DiskScope.Storage;

namespace DiskScope.Tests;

public sealed class NodeIndexTests
{
    [Fact]
    public void RootOnly_HasZeroTotalsAndNoChildren()
    {
        var builder = new NodeIndexBuilder();
        builder.AddRoot("C:\\");
        NodeIndex index = builder.Build();

        Assert.Equal(1, index.Count);
        Assert.Equal(0L, index.GetTotalLogicalSize(NodeIndex.RootId));
        Assert.Equal(0L, index.GetTotalAllocatedSize(NodeIndex.RootId));
        Assert.Equal(0, index.GetDirectFileCount(NodeIndex.RootId));
        Assert.Equal(0L, index.GetRecursiveFileCount(NodeIndex.RootId));
        Assert.Equal(0L, index.GetRecursiveDirectoryCount(NodeIndex.RootId));
        Assert.Empty(index.GetChildren(NodeIndex.RootId).ToArray());
    }

    [Fact]
    public void SimpleTree_AggregatesSizesAndCounts()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\data");
        builder.Add(root, "a.txt", NodeKind.File, FileAttributes.Normal, 10, 4096, 0, 0);
        int sub = builder.Add(root, "sub", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        builder.Add(sub, "b.bin", NodeKind.File, FileAttributes.Normal, 5, 0, 0, 0);
        builder.Add(sub, "c", NodeKind.File, FileAttributes.Normal, 7, 4096, 0, 0);
        NodeIndex index = builder.Build();

        Assert.Equal(22L, index.GetTotalLogicalSize(root));
        Assert.Equal(8192L, index.GetTotalAllocatedSize(root));
        Assert.Equal(1, index.GetDirectFileCount(root));
        Assert.Equal(3L, index.GetRecursiveFileCount(root));
        Assert.Equal(1L, index.GetRecursiveDirectoryCount(root));
        Assert.Equal(12L, index.GetTotalLogicalSize(sub));
        Assert.Equal(4096L, index.GetTotalAllocatedSize(sub));
        Assert.Equal(2, index.GetDirectFileCount(sub));
    }

    [Fact]
    public void DeepChain_AggregatesWithoutStackOverflow()
    {
        var builder = new NodeIndexBuilder(initialCapacity: 4);
        int parent = builder.AddRoot("C:\\deep");
        for (int i = 0; i < 10000; i++)
        {
            parent = builder.Add(parent, "d", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        }

        builder.Add(parent, "f", NodeKind.File, FileAttributes.Normal, 1, 0, 0, 0);
        NodeIndex index = builder.Build();

        Assert.Equal(1L, index.GetTotalLogicalSize(NodeIndex.RootId));
        Assert.Equal(10000L, index.GetRecursiveDirectoryCount(NodeIndex.RootId));
        Assert.Equal(1L, index.GetRecursiveFileCount(NodeIndex.RootId));
    }

    [Fact]
    public void EmptyDirectories_HaveZeroTotalsAndCountedDirectories()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\empty");
        int d1 = builder.Add(root, "d1", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        builder.Add(d1, "d2", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        builder.Add(root, "d3", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        NodeIndex index = builder.Build();

        Assert.Equal(0L, index.GetTotalLogicalSize(root));
        Assert.Equal(0L, index.GetTotalAllocatedSize(root));
        Assert.Equal(0L, index.GetRecursiveFileCount(root));
        Assert.Equal(3L, index.GetRecursiveDirectoryCount(root));
        Assert.Equal(1L, index.GetRecursiveDirectoryCount(d1));
    }

    [Fact]
    public void LinkNode_IsNotCountedAsFileOrDirectory()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\linked");
        builder.Add(root, "junction", NodeKind.Link, FileAttributes.ReparsePoint | FileAttributes.Directory, 0, 0, 0, 0);
        NodeIndex index = builder.Build();

        Assert.Equal(0, index.GetDirectFileCount(root));
        Assert.Equal(0L, index.GetRecursiveFileCount(root));
        Assert.Equal(0L, index.GetRecursiveDirectoryCount(root));
        Assert.Equal(0L, index.GetTotalLogicalSize(root));
    }

    [Fact]
    public void AccessDenied_PropagatesSubtreeIncompleteToAncestorsOnly()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\");
        int a = builder.Add(root, "a", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        int b = builder.Add(a, "b", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        int locked = builder.Add(b, "locked", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        int sibling = builder.Add(root, "sibling", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        int siblingFile = builder.Add(sibling, "x", NodeKind.File, FileAttributes.Normal, 1, 0, 0, 0);
        builder.AddError(new ScanError(locked, ScanErrorKind.AccessDenied, 5, "Access denied"));
        NodeIndex index = builder.Build();

        Assert.Equal(NodeStatus.AccessDenied | NodeStatus.SubtreeIncomplete, index.GetStatus(locked));
        Assert.Equal(NodeStatus.SubtreeIncomplete, index.GetStatus(b));
        Assert.Equal(NodeStatus.SubtreeIncomplete, index.GetStatus(a));
        Assert.Equal(NodeStatus.SubtreeIncomplete, index.GetStatus(root));
        Assert.Equal(NodeStatus.None, index.GetStatus(sibling));
        Assert.Equal(NodeStatus.None, index.GetStatus(siblingFile));
        Assert.Single(index.Errors);
    }

    [Fact]
    public void NotFoundError_SetsVanished()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\");
        int gone = builder.Add(root, "gone", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        builder.AddError(new ScanError(gone, ScanErrorKind.NotFound, 2, "Not found"));
        NodeIndex index = builder.Build();

        Assert.Equal(NodeStatus.Vanished | NodeStatus.SubtreeIncomplete, index.GetStatus(gone));
    }

    [Fact]
    public void GetChildren_ReturnsAscendingChildIds()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\");
        int x = builder.Add(root, "x", NodeKind.File, FileAttributes.Normal, 0, 0, 0, 0);
        int sub = builder.Add(root, "sub", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        int y = builder.Add(root, "y", NodeKind.File, FileAttributes.Normal, 0, 0, 0, 0);
        int bin = builder.Add(sub, "b.bin", NodeKind.File, FileAttributes.Normal, 0, 0, 0, 0);
        NodeIndex index = builder.Build();

        Assert.Equal(new[] { x, sub, y }, index.GetChildren(root).ToArray());
        Assert.Equal(new[] { bin }, index.GetChildren(sub).ToArray());
        Assert.Equal("C:\\sub\\b.bin", index.GetFullPath(bin));
    }

    [Fact]
    public void GetFullPath_JoinsWithoutDoubleSeparator()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("D:\\data");
        int x = builder.Add(root, "x", NodeKind.File, FileAttributes.Normal, 0, 0, 0, 0);
        NodeIndex index = builder.Build();

        Assert.Equal("D:\\data\\x", index.GetFullPath(x));
        Assert.Equal("D:\\data", index.GetFullPath(NodeIndex.RootId));
    }

    [Fact]
    public void LogicalAndAllocatedSizes_StayDistinct()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\");
        builder.Add(root, "tiny.txt", NodeKind.File, FileAttributes.Normal, 1, 4096, 0, 0);
        NodeIndex index = builder.Build();

        Assert.Equal(1L, index.GetTotalLogicalSize(root));
        Assert.Equal(4096L, index.GetTotalAllocatedSize(root));
    }

    [Fact]
    public void InvalidOperations_Throw()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\");
        int file = builder.Add(root, "f", NodeKind.File, FileAttributes.Normal, 0, 0, 0, 0);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            builder.Add(5, "x", NodeKind.File, FileAttributes.Normal, 0, 0, 0, 0);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            builder.Add(file, "x", NodeKind.File, FileAttributes.Normal, 0, 0, 0, 0);
        });
        Assert.Throws<InvalidOperationException>(() =>
        {
            builder.AddRoot("C:\\other");
        });

        builder.Build();
        Assert.Throws<InvalidOperationException>(() =>
        {
            builder.Build();
        });
    }

    [Fact]
    public void NamePool_RoundTripsManyDistinctNames()
    {
        const int nodeCount = 100000;
        var builder = new NodeIndexBuilder(initialCapacity: 16);
        int root = builder.AddRoot("C:\\big");
        for (int i = 0; i < nodeCount; i++)
        {
            builder.Add(root, $"name_{i}", NodeKind.File, FileAttributes.Normal, 0, 0, 0, 0);
        }

        NodeIndex index = builder.Build();

        Assert.Equal(nodeCount + 1, index.Count);
        for (int i = 0; i < nodeCount; i++)
        {
            Assert.Equal($"name_{i}", index.GetNameString(i + 1));
        }
    }

    [Fact]
    public void Build_without_root_throws()
    {
        var builder = new NodeIndexBuilder();

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }
}
