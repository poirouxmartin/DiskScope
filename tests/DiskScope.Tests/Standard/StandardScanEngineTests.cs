using System.Security.AccessControl;
using System.Security.Principal;
using DiskScope.Storage;
using DiskScope.Scanner;
using DiskScope.Scanner.Standard;

namespace DiskScope.Tests.Standard;

public sealed class StandardScanEngineTests
{
    [Fact]
    public void ReferenceTree_AggregatesSizesAndCounts()
    {
        using var tree = new TempTree();
        tree.File("a.bin", 1000);
        tree.File("sub/b.bin", 2000);
        tree.File("sub/deep/c.bin", 3000);
        tree.Dir("empty");

        ScanResult result = Scan(tree.Root);
        NodeIndex idx = result.Index;

        Assert.Equal(ScanOutcome.Completed, result.Outcome);
        Assert.Empty(idx.Errors);
        Assert.Equal(6000L, idx.GetTotalLogicalSize(NodeIndex.RootId));
        Assert.Equal(3L, idx.GetRecursiveFileCount(NodeIndex.RootId));
        Assert.Equal(3L, idx.GetRecursiveDirectoryCount(NodeIndex.RootId));

        int sub = Find(idx, "sub");
        int empty = Find(idx, "empty");
        Assert.Equal(5000L, idx.GetTotalLogicalSize(sub));
        Assert.Equal(1, idx.GetDirectFileCount(sub));
        Assert.Equal(0L, idx.GetTotalLogicalSize(empty));
        Assert.Equal(NodeStatus.None, idx.GetStatus(empty));

        foreach (string rel in new[] { "a.bin", "sub/b.bin", "sub/deep/c.bin" })
        {
            Assert.True(idx.GetAllocatedSize(Find(idx, rel)) >= 0);
        }
    }

    [Fact]
    public void RandomTree_MatchesDotNetEnumeration()
    {
        using var tree = new TempTree();
        var rng = new Random(42);
        for (int i = 0; i < 20; i++)
        {
            for (int j = 0; j < 15; j++)
            {
                tree.File($"d{i}/f{j}.bin", rng.Next(0, 20000));
            }
        }

        ScanResult result = Scan(tree.Root);
        FileInfo[] files = new DirectoryInfo(tree.Root).EnumerateFiles("*", SearchOption.AllDirectories).ToArray();

        Assert.Equal(ScanOutcome.Completed, result.Outcome);
        Assert.Equal(files.Sum(f => f.Length), result.Index.GetTotalLogicalSize(NodeIndex.RootId));
        Assert.Equal((long)files.Length, result.Index.GetRecursiveFileCount(NodeIndex.RootId));
    }

    [Fact]
    public void DeepTree_CountsEveryLevel()
    {
        using var tree = new TempTree();
        string rel = "";
        for (int i = 0; i < 120; i++)
        {
            rel = Path.Combine(rel, "d");
        }

        tree.File(Path.Combine(rel, "f.bin"), 1);

        NodeIndex idx = Scan(tree.Root).Index;

        Assert.Equal(1L, idx.GetTotalLogicalSize(NodeIndex.RootId));
        Assert.Equal(120L, idx.GetRecursiveDirectoryCount(NodeIndex.RootId));
    }

    [Fact]
    public void LongPath_IsScannedAndRoundTrips()
    {
        using var tree = new TempTree();
        string current = tree.Root;
        string rel = "";
        int level = 0;
        while (current.Length <= 400)
        {
            string name = new string('x', 50) + level;
            current = Path.Combine(current, name);
            rel = Path.Combine(rel, name);
            Directory.CreateDirectory(current);
            level++;
        }

        string filePath = Path.Combine(current, "f.bin");
        File.WriteAllBytes(filePath, new byte[10]);
        rel = Path.Combine(rel, "f.bin");

        NodeIndex idx = Scan(tree.Root).Index;
        int fileId = Find(idx, rel);

        Assert.Equal(10L, idx.GetTotalLogicalSize(NodeIndex.RootId));
        Assert.True(fileId >= 0, $"file not found: {rel}");
        Assert.Equal(filePath, idx.GetFullPath(fileId));
    }

    [Fact]
    public void UnusualNames_AreFoundAndRoundTrip()
    {
        using var tree = new TempTree();
        string[] names = { "with space.txt", "é ü 中文.txt", "#%&{}.txt", "a.b.c" };
        foreach (string name in names)
        {
            tree.File(name, 1);
        }

        NodeIndex idx = Scan(tree.Root).Index;

        Assert.Equal(4L, idx.GetRecursiveFileCount(NodeIndex.RootId));
        foreach (string name in names)
        {
            int id = Find(idx, name);
            Assert.True(id >= 0, $"node not found: {name}");
            Assert.Equal(name, idx.GetNameString(id));
        }
    }

    [Fact]
    public void Junction_IsRecordedAsLinkAndNotTraversed()
    {
        using var tree = new TempTree();
        string target = tree.Dir("target");
        tree.File("target/f.bin", 100);
        tree.Junction("link", target);

        ScanResult result = Scan(tree.Root);
        NodeIndex idx = result.Index;

        Assert.Equal(ScanOutcome.Completed, result.Outcome);
        Assert.Equal(NodeKind.Link, idx.GetKind(Find(idx, "link")));
        Assert.Equal(100L, idx.GetTotalLogicalSize(NodeIndex.RootId));
        Assert.Equal(1L, idx.GetRecursiveFileCount(NodeIndex.RootId));
        Assert.Empty(idx.Errors);
    }

    [Fact]
    public void JunctionLoop_PointingToAncestor_Terminates()
    {
        using var tree = new TempTree();
        string a = tree.Dir("a");
        tree.Junction("a/loop", a);

        ScanResult result = Scan(tree.Root);

        Assert.Equal(ScanOutcome.Completed, result.Outcome);
    }

    [Fact]
    public void AccessDenied_MarksDirectoryAndAncestors()
    {
        using var tree = new TempTree();
        string locked = tree.Dir("locked");
        tree.File("locked/x.bin", 50);

        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier sid = identity.User!;
        var info = new DirectoryInfo(locked);
        var rule = new FileSystemAccessRule(sid, FileSystemRights.ListDirectory, AccessControlType.Deny);

        DirectorySecurity acl = FileSystemAclExtensions.GetAccessControl(info);
        acl.AddAccessRule(rule);
        FileSystemAclExtensions.SetAccessControl(info, acl);
        try
        {
            ScanResult result = Scan(tree.Root);
            NodeIndex idx = result.Index;
            int lockedId = Find(idx, "locked");

            Assert.Equal(ScanOutcome.Completed, result.Outcome);
            Assert.Equal(NodeStatus.AccessDenied | NodeStatus.SubtreeIncomplete, idx.GetStatus(lockedId));
            Assert.True(idx.GetStatus(NodeIndex.RootId).HasFlag(NodeStatus.SubtreeIncomplete));
            ScanError error = Assert.Single(idx.Errors);
            Assert.Equal(ScanErrorKind.AccessDenied, error.Kind);
            Assert.Equal(5, error.Win32Error);
        }
        finally
        {
            DirectorySecurity restored = FileSystemAclExtensions.GetAccessControl(info);
            restored.RemoveAccessRuleSpecific(rule);
            FileSystemAclExtensions.SetAccessControl(info, restored);
        }
    }

    [Fact]
    public void Cancellation_LeavesConsistentNotScannedStatus()
    {
        using var tree = new TempTree();
        for (int i = 0; i < 200; i++)
        {
            for (int j = 0; j < 5; j++)
            {
                tree.File($"d{i}/f{j}.bin", 1);
            }
        }

        using var cts = new CancellationTokenSource();
        var observer = new CancelOnProgress(cts);
        ScanResult result = Scan(tree.Root, observer, cts.Token, new ScanOptions { ProgressInterval = TimeSpan.Zero });
        NodeIndex idx = result.Index;

        if (result.Outcome == ScanOutcome.Cancelled)
        {
            Assert.True(AnyNodeHas(idx, NodeStatus.NotScanned));
            Assert.True(idx.GetStatus(NodeIndex.RootId).HasFlag(NodeStatus.SubtreeIncomplete));
        }
        else
        {
            Assert.Equal(ScanOutcome.Completed, result.Outcome);
            Assert.False(AnyNodeHas(idx, NodeStatus.NotScanned));
        }
    }

    [Fact]
    public void PreCancelledToken_ReturnsCancelledWithRootNotScanned()
    {
        using var tree = new TempTree();
        tree.File("a.bin", 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        ScanResult result = Scan(tree.Root, null, cts.Token);

        Assert.Equal(ScanOutcome.Cancelled, result.Outcome);
        Assert.True(result.Index.GetStatus(NodeIndex.RootId).HasFlag(NodeStatus.NotScanned));
    }

    [Fact]
    public void MissingRoot_CompletesAsVanishedWithNotFound()
    {
        using var tree = new TempTree();
        string missing = Path.Combine(tree.Root, "does-not-exist");

        EngineAvailability availability = new StandardScanEngine().CheckAvailability(new ScanRequest(missing));
        ScanResult result = Scan(missing);
        NodeIndex idx = result.Index;

        Assert.False(availability.IsAvailable);
        Assert.Equal(ScanOutcome.Completed, result.Outcome);
        Assert.Equal(NodeStatus.Vanished | NodeStatus.SubtreeIncomplete, idx.GetStatus(NodeIndex.RootId));
        ScanError error = Assert.Single(idx.Errors);
        Assert.Equal(ScanErrorKind.NotFound, error.Kind);
    }

    [Fact]
    public void Progress_LastReportMatchesFinalNodeCountWithoutRoot()
    {
        using var tree = new TempTree();
        for (int i = 0; i < 5; i++)
        {
            tree.File($"d{i}/f.bin", 10);
        }

        var observer = new RecordingObserver();
        ScanResult result = Scan(tree.Root, observer);

        Assert.NotEmpty(observer.Calls);
        ScanProgress last = observer.Calls[observer.Calls.Count - 1];
        Assert.Equal((long)result.Index.Count - 1, last.Nodes);
    }

    private static ScanResult Scan(
        string root,
        IScanObserver? observer = null,
        CancellationToken cancellationToken = default,
        ScanOptions? options = null)
    {
        var request = new ScanRequest(root, options ?? new ScanOptions());
        return new StandardScanEngine().ScanAsync(request, observer, cancellationToken).GetAwaiter().GetResult();
    }

    /// <summary>Returns the node id at the given relative path (split on '\' or '/', case-insensitive), or -1.</summary>
    private static int Find(NodeIndex idx, string relPath)
    {
        int current = NodeIndex.RootId;
        foreach (string segment in relPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int next = -1;
            foreach (int child in idx.GetChildren(current))
            {
                if (string.Equals(idx.GetNameString(child), segment, StringComparison.OrdinalIgnoreCase))
                {
                    next = child;
                    break;
                }
            }

            if (next < 0)
            {
                return -1;
            }

            current = next;
        }

        return current;
    }

    private static bool AnyNodeHas(NodeIndex idx, NodeStatus flag)
    {
        for (int i = 0; i < idx.Count; i++)
        {
            if (idx.GetStatus(i).HasFlag(flag))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class CancelOnProgress : IScanObserver
    {
        private readonly CancellationTokenSource _cts;

        public CancelOnProgress(CancellationTokenSource cts)
        {
            _cts = cts;
        }

        public void OnProgress(in ScanProgress progress)
        {
            _cts.Cancel();
        }
    }

    private sealed class RecordingObserver : IScanObserver
    {
        public List<ScanProgress> Calls { get; } = new();

        public void OnProgress(in ScanProgress progress)
        {
            Calls.Add(progress);
        }
    }
}
