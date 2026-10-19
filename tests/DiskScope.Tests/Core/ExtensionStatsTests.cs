using System.IO;
using DiskScope.Core;

namespace DiskScope.Tests;

public sealed class ExtensionStatsTests
{
    [Fact]
    public void Compute_SumsCountAndSizePerExtension()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\ext");
        AddFile(builder, root, "a.txt", 10, 4096);
        AddFile(builder, root, "b.txt", 30, 4096);
        AddFile(builder, root, "c.log", 5, 512);
        NodeIndex index = builder.Build();

        Assert.Equal<ExtensionStat>(
            new[] { new ExtensionStat("txt", 2, 40), new ExtensionStat("log", 1, 5) },
            ExtensionStats.Compute(index, SizeKind.Logical));
    }

    [Fact]
    public void Compute_MergesExtensionsCaseInsensitively()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\ext");
        AddFile(builder, root, "A.JPG", 10, 10);
        AddFile(builder, root, "b.jpg", 20, 20);
        NodeIndex index = builder.Build();

        Assert.Equal<ExtensionStat>(
            new[] { new ExtensionStat("jpg", 2, 30) },
            ExtensionStats.Compute(index, SizeKind.Logical));
    }

    [Fact]
    public void Compute_FilesWithoutExtensionShareEmptyBucket()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\ext");
        AddFile(builder, root, "README", 7, 7);
        AddFile(builder, root, ".gitignore", 3, 3);
        NodeIndex index = builder.Build();

        Assert.Equal<ExtensionStat>(
            new[] { new ExtensionStat("", 2, 10) },
            ExtensionStats.Compute(index, SizeKind.Logical));
    }

    [Fact]
    public void Compute_IgnoresDirectoriesAndLinks()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\ext");
        builder.Add(root, "photos.jpg", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        builder.Add(root, "l.jpg", NodeKind.Link, FileAttributes.ReparsePoint | FileAttributes.Directory, 100, 100, 0, 0);
        AddFile(builder, root, "x.jpg", 4, 4);
        NodeIndex index = builder.Build();

        Assert.Equal<ExtensionStat>(
            new[] { new ExtensionStat("jpg", 1, 4) },
            ExtensionStats.Compute(index, SizeKind.Logical));
    }

    [Fact]
    public void Compute_OrdersBySizeDescThenExtension()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\ext");
        AddFile(builder, root, "b.dat", 10, 10);
        AddFile(builder, root, "a.zip", 10, 10);
        AddFile(builder, root, "c.big", 100, 100);
        NodeIndex index = builder.Build();

        Assert.Equal<ExtensionStat>(
            new[] { new ExtensionStat("big", 1, 100), new ExtensionStat("dat", 1, 10), new ExtensionStat("zip", 1, 10) },
            ExtensionStats.Compute(index, SizeKind.Logical));
    }

    [Fact]
    public void Compute_AllocatedKindUsesAllocatedSizes()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\ext");
        AddFile(builder, root, "a.txt", 1, 4096);
        AddFile(builder, root, "b.txt", 2, 4096);
        NodeIndex index = builder.Build();

        Assert.Equal<ExtensionStat>(
            new[] { new ExtensionStat("txt", 2, 8192) },
            ExtensionStats.Compute(index, SizeKind.Allocated));
        Assert.Equal<ExtensionStat>(
            new[] { new ExtensionStat("txt", 2, 3) },
            ExtensionStats.Compute(index, SizeKind.Logical));
    }

    private static void AddFile(NodeIndexBuilder builder, int parent, string name, long logical, long allocated)
    {
        builder.Add(parent, name, NodeKind.File, FileAttributes.Normal, logical, allocated, 0, 0);
    }
}
