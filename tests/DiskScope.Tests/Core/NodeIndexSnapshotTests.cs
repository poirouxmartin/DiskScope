using System.IO;
using DiskScope.Core;

namespace DiskScope.Tests;

public sealed class NodeIndexSnapshotTests
{
    [Fact]
    public void Snapshot_FlagsPendingDirectories_AndLeavesBuilderWritable()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\r");
        int done = builder.Add(root, "done", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        builder.Add(done, "a", NodeKind.File, FileAttributes.Normal, 10, 4096, 0, 0);
        int pending = builder.Add(root, "pending", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);

        NodeIndex partial = builder.Snapshot(new[] { pending });
        builder.Add(pending, "b", NodeKind.File, FileAttributes.Normal, 20, 4096, 0, 0);
        NodeIndex final = builder.Build();

        Assert.Equal(4, partial.Count);
        Assert.Equal(10L, partial.GetTotalLogicalSize(root));
        Assert.True((partial.GetStatus(pending) & NodeStatus.NotScanned) != 0);
        Assert.True((partial.GetStatus(root) & NodeStatus.SubtreeIncomplete) != 0);
        Assert.Equal(NodeStatus.None, partial.GetStatus(done));

        Assert.Equal(5, final.Count);
        Assert.Equal(30L, final.GetTotalLogicalSize(root));
        Assert.Equal(NodeStatus.None, final.GetStatus(pending));
        Assert.Equal(NodeStatus.None, final.GetStatus(root));
    }

    [Fact]
    public void Snapshot_ErrorListIsACopy()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\r");
        int d = builder.Add(root, "d", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);

        NodeIndex partial = builder.Snapshot(Array.Empty<int>());
        builder.AddError(new ScanError(d, ScanErrorKind.AccessDenied, 5, "denied"));

        Assert.Empty(partial.Errors);
        Assert.Single(builder.Build().Errors);
    }

    [Fact]
    public void Snapshot_InvalidPendingId_Throws()
    {
        var builder = new NodeIndexBuilder();
        builder.AddRoot("C:\\r");

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Snapshot(new[] { 5 }));
    }
}
