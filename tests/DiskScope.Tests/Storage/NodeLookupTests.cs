using System.IO;
using DiskScope.Storage;

namespace DiskScope.Tests.Storage;

public sealed class NodeLookupTests
{
    private static readonly long Old = new DateTime(2019, 6, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
    private static readonly long Recent = new DateTime(2025, 2, 3, 0, 0, 0, DateTimeKind.Utc).Ticks;

    [Theory]
    [InlineData(@"C:\data", "root")]
    [InlineData(@"C:\data\", "root")]
    [InlineData(@"c:\DATA\Sub\Deep\f.txt", "file")]
    [InlineData("C:/data/sub/deep", "deep")]
    [InlineData(@"C:\data\sub\deep\", "deep")]
    public void FindByPath_ResolvesCaseAndSeparatorVariants(string path, string expected)
    {
        (NodeIndex index, int deep, int file) = Build();
        int expectedId = expected switch { "root" => NodeIndex.RootId, "deep" => deep, _ => file };

        Assert.Equal(expectedId, index.FindByPath(path));
    }

    [Theory]
    [InlineData(@"C:\data2")]
    [InlineData(@"C:\other\sub")]
    [InlineData(@"C:\data\missing")]
    [InlineData(@"C:\data\sub\deep\f.txt\x")]
    [InlineData(@"C:\data\\sub")]
    public void FindByPath_ReturnsMinusOneOutsideTheIndex(string path)
    {
        (NodeIndex index, _, _) = Build();

        Assert.Equal(-1, index.FindByPath(path));
    }

    [Fact]
    public void FindByPath_UnderDriveRoot_Works()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot(@"C:\");
        int windows = builder.Add(root, "Windows", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        NodeIndex index = builder.Build();

        Assert.Equal(NodeIndex.RootId, index.FindByPath(@"C:\"));
        Assert.Equal(windows, index.FindByPath(@"C:\windows"));
    }

    [Fact]
    public void SubtreeLastWrite_IsLatestKnownDateBelowTheNode()
    {
        (NodeIndex index, int deep, int file) = Build();
        int sub = index.GetParent(deep);

        Assert.Equal(Old, index.GetLastWriteUtc(sub).Ticks);
        Assert.Equal(Recent, index.GetSubtreeLastWriteUtc(sub).Ticks);
        Assert.Equal(Recent, index.GetSubtreeLastWriteUtc(NodeIndex.RootId).Ticks);
        Assert.Equal(Recent, index.GetSubtreeLastWriteUtc(file).Ticks);
    }

    [Fact]
    public void SubtreeLastWrite_WithNoKnownDate_IsUnknown()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot(@"C:\u");
        int dir = builder.Add(root, "d", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        builder.Add(dir, "f", NodeKind.File, FileAttributes.Normal, 1, 0, 0, 0);
        NodeIndex index = builder.Build();

        Assert.Equal(0, index.GetSubtreeLastWriteUtc(dir).Ticks);
    }

    [Fact]
    public void KnownTotalSize_IsNullOnlyWhenTheNodeItselfIsUnread()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot(@"C:\k");
        int denied = builder.Add(root, "denied", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        builder.AddStatus(denied, NodeStatus.AccessDenied);
        int pending = builder.Add(root, "pending", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        builder.AddStatus(pending, NodeStatus.NotScanned);
        int empty = builder.Add(root, "empty", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        builder.Add(root, "f.bin", NodeKind.File, FileAttributes.Normal, 10, 4096, 0, 0);
        NodeIndex index = builder.Build();

        Assert.Null(index.GetKnownTotalSize(denied, SizeKind.Logical));
        Assert.Null(index.GetKnownTotalSize(pending, SizeKind.Allocated));
        Assert.Equal(0L, index.GetKnownTotalSize(empty, SizeKind.Logical));
        Assert.Equal(10L, index.GetKnownTotalSize(NodeIndex.RootId, SizeKind.Logical));
        Assert.Equal(4096L, index.GetKnownTotalSize(NodeIndex.RootId, SizeKind.Allocated));
    }

    // C:\data (date Old) > sub (Old) > deep (Old) > f.txt (Recent); C:\data\top.txt (Old).
    private static (NodeIndex Index, int Deep, int File) Build()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot(@"C:\data");
        int sub = builder.Add(root, "sub", NodeKind.Directory, FileAttributes.Directory, 0, 0, Old, 0);
        builder.Add(root, "top.txt", NodeKind.File, FileAttributes.Normal, 5, 0, Old, 0);
        int deep = builder.Add(sub, "deep", NodeKind.Directory, FileAttributes.Directory, 0, 0, Old, 0);
        int file = builder.Add(deep, "f.txt", NodeKind.File, FileAttributes.Normal, 3, 0, Recent, 0);
        return (builder.Build(), deep, file);
    }
}
