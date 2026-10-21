using System.ComponentModel;
using DiskScope.Core;
using DiskScope.Scanner.Ntfs;

namespace DiskScope.Tests.Ntfs;

public sealed class UsnTreeBuilderTests
{
    private const uint Dir = 0x10;
    private const uint File = 0x20;
    private static readonly UInt128 Root = 5;

    [Fact]
    public void ChildrenBeforeParents_AreReordered_ParentIdBelowChildId()
    {
        var store = new UsnRecordStore();
        store.Add(30, 20, File, "deep.txt");
        store.Add(20, 10, Dir, "sub");
        store.Add(10, Root, Dir, "top");
        store.Add(11, Root, File, "a.txt");

        UsnTree tree = UsnTreeBuilder.Build(store, Root, "C:\\");
        NodeIndex idx = tree.Index;

        Assert.Equal(0, tree.Orphans);
        Assert.Equal(5, idx.Count);
        for (int id = 1; id < idx.Count; id++)
        {
            Assert.True(idx.GetParent(id) < id);
        }

        Assert.Equal("C:\\top\\sub\\deep.txt", idx.GetFullPath(Find(idx, "deep.txt")));
        Assert.Equal(2L, idx.GetRecursiveFileCount(NodeIndex.RootId));
        Assert.Equal(NodeStatus.None, idx.GetStatus(NodeIndex.RootId));
    }

    [Fact]
    public void MissingParent_GoesUnderOrphans_WithItsSubtree_AndRootIsIncomplete()
    {
        var store = new UsnRecordStore();
        store.Add(10, 999, Dir, "lost");
        store.Add(11, 10, File, "inside.txt");
        store.Add(12, Root, File, "ok.txt");

        UsnTree tree = UsnTreeBuilder.Build(store, Root, "C:\\");
        NodeIndex idx = tree.Index;
        int orphans = Find(idx, UsnTreeBuilder.OrphansName);

        Assert.Equal(2, tree.Orphans);
        Assert.Equal("C:\\<orphans>\\lost\\inside.txt", idx.GetFullPath(Find(idx, "inside.txt")));
        Assert.Contains(idx.Errors, e => e.NodeId == orphans && e.Kind == ScanErrorKind.Unexpected);
        Assert.True((idx.GetStatus(NodeIndex.RootId) & NodeStatus.SubtreeIncomplete) != 0);
    }

    [Fact]
    public void ParentCycle_IsPlacedOnce_UnderOrphans()
    {
        var store = new UsnRecordStore();
        store.Add(10, 11, Dir, "a");
        store.Add(11, 10, Dir, "b");

        UsnTree tree = UsnTreeBuilder.Build(store, Root, "C:\\");

        Assert.Equal(2, tree.Orphans);
        Assert.Equal(4, tree.Index.Count); // root, <orphans>, a, b
    }

    [Fact]
    public void FileAsParent_AndSelfParent_AreOrphans()
    {
        var store = new UsnRecordStore();
        store.Add(10, Root, File, "file.bin");
        store.Add(11, 10, File, "under-a-file");
        store.Add(12, 12, Dir, "self");

        UsnTree tree = UsnTreeBuilder.Build(store, Root, "C:\\");

        Assert.Equal(2, tree.Orphans);
        Assert.Equal(NodeIndex.RootId, tree.Index.GetParent(Find(tree.Index, "file.bin")));
    }

    [Fact]
    public void DuplicateReference_IsKeptAsOrphan_NeverDropped()
    {
        var store = new UsnRecordStore();
        store.Add(10, Root, File, "first");
        store.Add(10, Root, File, "second");

        UsnTree tree = UsnTreeBuilder.Build(store, Root, "C:\\");

        Assert.Equal(1, tree.DuplicateReferences);
        Assert.Equal(1, tree.Orphans);
        Assert.NotEqual(-1, Find(tree.Index, "second"));
    }

    [Fact]
    public void RootRecordItself_IsNotDuplicatedAsAChild()
    {
        var store = new UsnRecordStore();
        store.Add(Root, Root, Dir, ".");
        store.Add(10, Root, File, "a");

        UsnTree tree = UsnTreeBuilder.Build(store, Root, "C:\\");

        Assert.Equal(2, tree.Index.Count);
        Assert.Equal(0, tree.Orphans);
    }

    [Fact]
    public void GetRootReference_ReadsTheSystemDrive_WithoutElevation()
    {
        char drive = Path.GetPathRoot(Environment.SystemDirectory)![0];

        UInt128 root = UsnVolumeEnumerator.GetRootReference(drive);

        // NTFS root directory is MFT record 5; the upper 16 bits of a 64-bit reference are the sequence number.
        Assert.Equal(5UL, (ulong)root & 0xFFFF_FFFF_FFFFUL);
    }

    [Fact]
    public void Enumerate_WithoutElevation_FailsWithAccessDenied_NotSilently()
    {
        if (Environment.IsPrivilegedProcess)
        {
            return; // Elevated runs exercise the real path in the usn-probe benchmark command instead.
        }

        char drive = Path.GetPathRoot(Environment.SystemDirectory)![0];

        var ex = Assert.Throws<Win32Exception>(() => UsnVolumeEnumerator.Enumerate(drive, new UsnRecordStore(), default));
        Assert.Equal(5, ex.NativeErrorCode);
    }

    private static int Find(NodeIndex idx, string name)
    {
        for (int id = 0; id < idx.Count; id++)
        {
            if (idx.GetNameString(id) == name)
            {
                return id;
            }
        }

        return -1;
    }
}
