using System.IO;
using System.Text;

namespace DiskScope.Storage;

/// <summary>
/// Immutable, struct-of-arrays index of a scanned tree. Created by <see cref="NodeIndexBuilder.Build"/>.
/// Parent ids are always smaller than child ids; NodeIndex aggregation relies on it.
/// </summary>
/// <remarks>
/// Counting rules v1: sizes come from the file system as reported per directory entry. Directories own no size (0); their totals are the sum of descendants. Links (junctions, symlinks) are not traversed. Hard links are counted once per occurrence (per path), as Explorer does; deduplication by file reference is planned for the NTFS engine. Logical and allocated sizes are never mixed.
/// </remarks>
public sealed class NodeIndex
{
    public const int RootId = 0;

    private readonly int[] _parent;
    private readonly int[] _nameOffset;
    private readonly int[] _nameLength;
    private readonly char[] _names;
    private readonly NodeKind[] _kind;
    private readonly FileAttributes[] _attributes;
    private readonly long[] _logical;
    private readonly long[] _allocated;
    private readonly long[] _lastWriteTicks;
    private readonly ulong[] _fileReference;
    private readonly NodeStatus[] _status;
    private readonly long[] _totalLogical;
    private readonly long[] _totalAllocated;
    private readonly int[] _directFiles;
    private readonly long[] _recursiveFiles;
    private readonly long[] _recursiveDirectories;
    private readonly long[] _subtreeLastWriteTicks;
    private readonly int[] _childStart;
    private readonly int[] _children;
    private readonly IReadOnlyList<ScanError> _errors;

    internal NodeIndex(
        int count,
        int[] parent,
        int[] nameOffset,
        int[] nameLength,
        char[] names,
        NodeKind[] kind,
        FileAttributes[] attributes,
        long[] logical,
        long[] allocated,
        long[] lastWriteTicks,
        ulong[] fileReference,
        NodeStatus[] status,
        List<ScanError> errors)
    {
        Count = count;
        _parent = parent;
        _nameOffset = nameOffset;
        _nameLength = nameLength;
        _names = names;
        _kind = kind;
        _attributes = attributes;
        _logical = logical;
        _allocated = allocated;
        _lastWriteTicks = lastWriteTicks;
        _fileReference = fileReference;
        _status = status;
        _errors = errors.AsReadOnly();

        _totalLogical = new long[count];
        _totalAllocated = new long[count];
        _directFiles = new int[count];
        _recursiveFiles = new long[count];
        _recursiveDirectories = new long[count];
        _subtreeLastWriteTicks = new long[count];
        Aggregate(count, parent, kind, logical, allocated, lastWriteTicks, status,
            _totalLogical, _totalAllocated, _directFiles, _recursiveFiles, _recursiveDirectories, _subtreeLastWriteTicks);

        _childStart = new int[count + 1];
        _children = new int[count - 1];
        BuildChildren(count, parent, _childStart, _children);
    }

    public int Count { get; }

    public int GetParent(int id)
    {
        Check(id);
        return _parent[id];
    }

    public ReadOnlySpan<char> GetName(int id)
    {
        Check(id);
        return _names.AsSpan(_nameOffset[id], _nameLength[id]);
    }

    public string GetNameString(int id) => new string(GetName(id));

    public NodeKind GetKind(int id)
    {
        Check(id);
        return _kind[id];
    }

    public FileAttributes GetAttributes(int id)
    {
        Check(id);
        return _attributes[id];
    }

    public long GetLogicalSize(int id)
    {
        Check(id);
        return _logical[id];
    }

    public long GetAllocatedSize(int id)
    {
        Check(id);
        return _allocated[id];
    }

    public DateTime GetLastWriteUtc(int id)
    {
        Check(id);
        return new DateTime(_lastWriteTicks[id], DateTimeKind.Utc);
    }

    /// <summary>
    /// Latest known last-write time of the node and all its descendants: when anything in the subtree last changed.
    /// A directory's own date moves only when its direct entries change, so this is the date to use when looking
    /// for abandoned folders. Ticks 0 means no date is known in the subtree. For an incomplete subtree
    /// (<see cref="NodeStatus.SubtreeIncomplete"/>) it is a lower bound: an unread part may be more recent.
    /// </summary>
    public DateTime GetSubtreeLastWriteUtc(int id)
    {
        Check(id);
        return new DateTime(_subtreeLastWriteTicks[id], DateTimeKind.Utc);
    }

    public ulong GetFileReference(int id)
    {
        Check(id);
        return _fileReference[id];
    }

    public NodeStatus GetStatus(int id)
    {
        Check(id);
        return _status[id];
    }

    public long GetTotalLogicalSize(int id)
    {
        Check(id);
        return _totalLogical[id];
    }

    public long GetTotalAllocatedSize(int id)
    {
        Check(id);
        return _totalAllocated[id];
    }

    public int GetDirectFileCount(int id)
    {
        Check(id);
        return _directFiles[id];
    }

    public long GetRecursiveFileCount(int id)
    {
        Check(id);
        return _recursiveFiles[id];
    }

    /// <summary>Number of directories below <paramref name="id"/>, excluding the node itself.</summary>
    public long GetRecursiveDirectoryCount(int id)
    {
        Check(id);
        return _recursiveDirectories[id];
    }

    public ReadOnlySpan<int> GetChildren(int id)
    {
        Check(id);
        int start = _childStart[id];
        return _children.AsSpan(start, _childStart[id + 1] - start);
    }

    /// <summary>
    /// Rebuilds the path from the parent chain. Root name is the full root path (e.g. "C:\\").
    /// </summary>
    public string GetFullPath(int id)
    {
        Check(id);
        var segments = new List<int>();
        for (int current = id; current != RootId; current = _parent[current])
        {
            segments.Add(current);
        }

        var path = new StringBuilder(GetNameString(RootId));
        for (int i = segments.Count - 1; i >= 0; i--)
        {
            if (path.Length == 0 || path[path.Length - 1] != '\\')
            {
                path.Append('\\');
            }

            path.Append(GetName(segments[i]));
        }

        return path.ToString();
    }

    public IReadOnlyList<ScanError> Errors => _errors;

    /// <summary>
    /// Computes totals and counts in one pass. Parents have smaller ids than children, so walking ids
    /// in descending order folds each node into its parent only after all of its descendants were folded
    /// into it. The loop is iterative on purpose: a deep tree must not overflow the stack.
    /// </summary>
    private static void Aggregate(
        int count,
        int[] parent,
        NodeKind[] kind,
        long[] logical,
        long[] allocated,
        long[] lastWriteTicks,
        NodeStatus[] status,
        long[] totalLogical,
        long[] totalAllocated,
        int[] directFiles,
        long[] recursiveFiles,
        long[] recursiveDirectories,
        long[] subtreeLastWriteTicks)
    {
        for (int id = 0; id < count; id++)
        {
            totalLogical[id] = logical[id];
            totalAllocated[id] = allocated[id];
            recursiveFiles[id] = kind[id] == NodeKind.File ? 1 : 0;
            subtreeLastWriteTicks[id] = lastWriteTicks[id];

            // A node whose own contents are unknown is itself an incomplete subtree.
            if ((status[id] & NodeStatusExtensions.OwnErrorMask) != 0)
            {
                status[id] |= NodeStatus.SubtreeIncomplete;
            }
        }

        for (int id = count - 1; id > RootId; id--)
        {
            int p = parent[id];
            totalLogical[p] += totalLogical[id];
            totalAllocated[p] += totalAllocated[id];
            recursiveFiles[p] += recursiveFiles[id];
            recursiveDirectories[p] += recursiveDirectories[id] + (kind[id] == NodeKind.Directory ? 1 : 0);

            // Unknown dates are 0, so Max ignores them without a special case.
            subtreeLastWriteTicks[p] = Math.Max(subtreeLastWriteTicks[p], subtreeLastWriteTicks[id]);
            if (kind[id] == NodeKind.File)
            {
                directFiles[p]++;
            }

            if (status[id].IsIncomplete())
            {
                status[p] |= NodeStatus.SubtreeIncomplete;
            }
        }
    }

    /// <summary>
    /// CSR layout: childStart[p]..childStart[p+1] indexes the children of p in children.
    /// Ids are visited in ascending order, so children of each parent are ascending too.
    /// </summary>
    private static void BuildChildren(int count, int[] parent, int[] childStart, int[] children)
    {
        for (int id = 1; id < count; id++)
        {
            childStart[parent[id] + 1]++;
        }

        for (int i = 0; i < count; i++)
        {
            childStart[i + 1] += childStart[i];
        }

        int[] next = new int[count];
        Array.Copy(childStart, next, count);
        for (int id = 1; id < count; id++)
        {
            children[next[parent[id]]++] = id;
        }
    }

    private void Check(int id)
    {
        if ((uint)id >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }
    }
}
