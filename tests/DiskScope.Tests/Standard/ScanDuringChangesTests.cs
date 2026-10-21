using DiskScope.Core;
using DiskScope.Scanner;
using DiskScope.Scanner.Standard;

namespace DiskScope.Tests.Standard;

/// <summary>Tree changes between listing and enumeration, injected through the engine's test seam.</summary>
public sealed class ScanDuringChangesTests
{
    [Fact]
    public void DirectoryDeletedAfterListing_IsVanished_AndSiblingsStayCounted()
    {
        using var tree = new TempTree();
        tree.File("keep/a.bin", 100);
        string gone = tree.Dir("gone");
        tree.File("gone/b.bin", 500);

        ScanResult result = ScanWithHook(tree.Root, path =>
        {
            if (string.Equals(path, gone, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(gone, true);
            }
        });
        NodeIndex idx = result.Index;
        int goneId = FindChild(idx, NodeIndex.RootId, "gone");

        Assert.Equal(ScanOutcome.Completed, result.Outcome);
        Assert.True((idx.GetStatus(goneId) & NodeStatus.Vanished) != 0);
        Assert.Contains(idx.Errors, e => e.NodeId == goneId && e.Kind == ScanErrorKind.NotFound);
        Assert.True((idx.GetStatus(NodeIndex.RootId) & NodeStatus.SubtreeIncomplete) != 0);
        Assert.Equal(100L, idx.GetTotalLogicalSize(NodeIndex.RootId));
    }

    [Fact]
    public void DirectoryRenamedAfterListing_OldNameIsVanished_NewNameIsNotInvented()
    {
        using var tree = new TempTree();
        string old = tree.Dir("old");
        tree.File("old/c.bin", 42);

        ScanResult result = ScanWithHook(tree.Root, path =>
        {
            if (string.Equals(path, old, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(old, Path.Combine(tree.Root, "new"));
            }
        });
        NodeIndex idx = result.Index;

        Assert.True((idx.GetStatus(FindChild(idx, NodeIndex.RootId, "old")) & NodeStatus.Vanished) != 0);
        Assert.Equal(-1, FindChild(idx, NodeIndex.RootId, "new"));
    }

    [Fact]
    public void FileAddedBeforeItsDirectoryIsOpened_IsCounted()
    {
        using var tree = new TempTree();
        string sub = tree.Dir("sub");

        ScanResult result = ScanWithHook(tree.Root, path =>
        {
            if (string.Equals(path, sub, StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllBytes(Path.Combine(sub, "late.bin"), new byte[7]);
            }
        });

        Assert.Equal(7L, result.Index.GetTotalLogicalSize(NodeIndex.RootId));
        Assert.Equal(NodeStatus.None, result.Index.GetStatus(NodeIndex.RootId));
    }

    [Fact]
    public async Task PartialResults_AreFlaggedIncomplete_AndFinalResultIsComplete()
    {
        using var tree = new TempTree();
        for (int i = 0; i < 8; i++)
        {
            tree.File($"d{i}/f.bin", 10);
        }

        var observer = new CollectingObserver();
        var options = new ScanOptions { PartialResultInterval = TimeSpan.FromMilliseconds(1) };
        var engine = new StandardScanEngine(_ => Thread.Sleep(5));
        ScanResult result = await engine.ScanAsync(new ScanRequest(tree.Root, options), observer, default);

        Assert.NotEmpty(observer.Partials);
        foreach (NodeIndex partial in observer.Partials)
        {
            Assert.True((partial.GetStatus(NodeIndex.RootId) & NodeStatus.SubtreeIncomplete) != 0);
            Assert.True(partial.Count <= result.Index.Count);
            Assert.True(partial.GetTotalLogicalSize(NodeIndex.RootId) <= 80);
        }

        Assert.Equal(NodeStatus.None, result.Index.GetStatus(NodeIndex.RootId));
        Assert.Equal(80L, result.Index.GetTotalLogicalSize(NodeIndex.RootId));
    }

    [Fact]
    public async Task PartialResults_AreNotPublishedByDefault()
    {
        using var tree = new TempTree();
        tree.File("d/f.bin", 1);
        var observer = new CollectingObserver();

        await new StandardScanEngine(_ => Thread.Sleep(5))
            .ScanAsync(new ScanRequest(tree.Root), observer, default);

        Assert.Empty(observer.Partials);
    }

    private static ScanResult ScanWithHook(string root, Action<string> hook) =>
        new StandardScanEngine(hook).ScanAsync(new ScanRequest(root), null, default).GetAwaiter().GetResult();

    private static int FindChild(NodeIndex idx, int parent, string name)
    {
        foreach (int child in idx.GetChildren(parent))
        {
            if (string.Equals(idx.GetNameString(child), name, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return -1;
    }

    private sealed class CollectingObserver : IScanObserver
    {
        public List<NodeIndex> Partials { get; } = new();

        public void OnProgress(in ScanProgress progress)
        {
        }

        public void OnPartialResult(NodeIndex partial) => Partials.Add(partial);
    }
}
