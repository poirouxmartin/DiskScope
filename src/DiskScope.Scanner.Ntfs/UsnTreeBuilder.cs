using DiskScope.Storage;

namespace DiskScope.Scanner.Ntfs;

/// <summary>Structure-only tree rebuilt from USN records, with the counts needed to judge its coherence.</summary>
/// <param name="Index">Names, kinds and parent links. Sizes and dates are NOT known (USN records carry none): never display them.</param>
/// <param name="Orphans">Records placed under the synthetic orphans node (missing, non-directory or cyclic parent, duplicate reference).</param>
/// <param name="DuplicateReferences">Records whose file reference was already seen; kept as orphans, never dropped.</param>
public sealed record UsnTree(NodeIndex Index, int Orphans, int DuplicateReferences);

/// <summary>
/// Rebuilds the parent/child tree from records that arrive in MFT order (parents may follow children).
/// Breadth-first from the root restores the NodeIndex invariant parent id &lt; child id.
/// Records that cannot be reached from the root go under a synthetic "&lt;orphans&gt;" directory, which carries an
/// Unexpected error so the root reads SubtreeIncomplete. Cycles cannot loop: every record is placed at most once.
/// </summary>
public static class UsnTreeBuilder
{
    public const string OrphansName = "<orphans>";

    private const int RootParent = -1;
    private const int NoParent = -2;

    public static UsnTree Build(UsnRecordStore store, UInt128 rootReference, string rootPath)
    {
        ArgumentNullException.ThrowIfNull(store);
        int n = store.Count;

        var byRef = new Dictionary<UInt128, int>(n);
        var isDuplicate = new bool[n];
        int duplicates = 0;
        for (int i = 0; i < n; i++)
        {
            UInt128 frn = store.GetFileReference(i);
            if (frn == rootReference)
            {
                continue; // The root's own record; the root node comes from rootPath.
            }

            if (!byRef.TryAdd(frn, i))
            {
                isDuplicate[i] = true;
                duplicates++;
            }
        }

        // Parent slot per record: another record, the root, or none (orphan).
        var parentOf = new int[n];
        for (int i = 0; i < n; i++)
        {
            UInt128 frn = store.GetFileReference(i);
            UInt128 parent = store.GetParentReference(i);
            if (frn == rootReference)
            {
                parentOf[i] = NoParent; // Skipped below.
            }
            else if (isDuplicate[i])
            {
                parentOf[i] = NoParent;
            }
            else if (parent == rootReference)
            {
                parentOf[i] = RootParent;
            }
            else if (byRef.TryGetValue(parent, out int p) && p != i && IsDirectory(store, p))
            {
                parentOf[i] = p;
            }
            else
            {
                parentOf[i] = NoParent;
            }
        }

        // Children in CSR form; slot n holds the root's children.
        var start = new int[n + 2];
        for (int i = 0; i < n; i++)
        {
            int p = parentOf[i];
            if (p != NoParent)
            {
                start[(p == RootParent ? n : p) + 1]++;
            }
        }

        for (int i = 1; i < start.Length; i++)
        {
            start[i] += start[i - 1];
        }

        var children = new int[start[n + 1]];
        var fill = (int[])start.Clone();
        for (int i = 0; i < n; i++)
        {
            int p = parentOf[i];
            if (p != NoParent)
            {
                children[fill[p == RootParent ? n : p]++] = i;
            }
        }

        var builder = new NodeIndexBuilder(Math.Max(1, n + 2));
        int rootId = builder.AddRoot(rootPath);
        var placed = new bool[n];
        var queue = new Queue<(int Record, int Node)>();

        EnqueueChildren(n, rootId);
        Drain();

        int orphans = 0;
        int orphansId = -1;

        // First the orphan forest roots (no usable parent), then whatever is left: members of parent cycles.
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < n; i++)
            {
                if (placed[i] || store.GetFileReference(i) == rootReference || (pass == 0 && parentOf[i] != NoParent))
                {
                    continue;
                }

                if (orphansId < 0)
                {
                    orphansId = builder.Add(rootId, OrphansName, NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
                }

                int before = CountPlaced();
                Place(i, orphansId);
                Drain();
                orphans += CountPlaced() - before;
            }
        }

        if (orphansId >= 0)
        {
            builder.AddError(new ScanError(orphansId, ScanErrorKind.Unexpected, 0,
                $"{orphans} records without a parent reachable from the root"));
        }

        return new UsnTree(builder.Build(), orphans, duplicates);

        void EnqueueChildren(int slot, int nodeId)
        {
            for (int c = start[slot]; c < start[slot + 1]; c++)
            {
                int child = children[c];
                if (!placed[child])
                {
                    Place(child, nodeId);
                }
            }
        }

        void Place(int record, int parentNode)
        {
            placed[record] = true;
            FileAttributes attrs = store.GetAttributes(record);
            NodeKind kind = (attrs & FileAttributes.Directory) != 0 ? NodeKind.Directory : NodeKind.File;
            int id = builder.Add(parentNode, store.GetName(record), kind, attrs, 0, 0, 0, (ulong)store.GetFileReference(record));
            if (kind == NodeKind.Directory)
            {
                queue.Enqueue((record, id));
            }
        }

        void Drain()
        {
            while (queue.Count > 0)
            {
                var (record, node) = queue.Dequeue();
                EnqueueChildren(record, node);
            }
        }

        int CountPlaced() => builder.Count;
    }

    private static bool IsDirectory(UsnRecordStore store, int i) =>
        (store.GetAttributes(i) & FileAttributes.Directory) != 0;
}
