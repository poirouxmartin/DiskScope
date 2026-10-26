using System;
using System.Collections.Generic;

namespace DiskScope.Storage;

/// <summary>
/// Read-only queries over a <see cref="NodeIndex"/> that need sorting or ranking.
/// Every query reads one <see cref="SizeKind"/> column; logical and allocated sizes are never mixed.
/// </summary>
public static class NodeIndexQueries
{
    /// <summary>
    /// Total size of the subtree rooted at <paramref name="id"/>: the node's own size for a file,
    /// the sum of its descendants for a directory.
    /// </summary>
    public static long GetTotalSize(this NodeIndex index, int id, SizeKind kind) =>
        kind == SizeKind.Logical ? index.GetTotalLogicalSize(id) : index.GetTotalAllocatedSize(id);

    /// <summary>
    /// Total size, or null when it is unknown: the node's own contents could not be read or were not read yet
    /// (<see cref="NodeStatusExtensions.OwnErrorMask"/>), so a 0 there would be a guess.
    /// When only a descendant is incomplete (<see cref="NodeStatus.SubtreeIncomplete"/>) the value is returned
    /// and is a lower bound.
    /// </summary>
    public static long? GetKnownTotalSize(this NodeIndex index, int id, SizeKind kind) =>
        (index.GetStatus(id) & NodeStatusExtensions.OwnErrorMask) != 0 ? null : index.GetTotalSize(id, kind);

    /// <summary>
    /// Id of the node at <paramref name="path"/>, or -1 when the path is not in the index.
    /// The path must start with the root path; comparison ignores case, as Windows does, and accepts '/' and
    /// a trailing separator. Cost: one linear pass over the children of each directory on the way.
    /// </summary>
    public static int FindByPath(this NodeIndex index, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        ReadOnlySpan<char> rest = path.Replace('/', '\\').AsSpan().TrimEnd('\\');
        ReadOnlySpan<char> root = index.GetName(NodeIndex.RootId).TrimEnd('\\');
        if (!rest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        rest = rest.Slice(root.Length);
        if (rest.Length > 0 && rest[0] != '\\')
        {
            return -1; // "C:\data2" must not match the root "C:\data".
        }

        int current = NodeIndex.RootId;
        while (rest.Length > 0)
        {
            rest = rest.Slice(1);
            int end = rest.IndexOf('\\');
            ReadOnlySpan<char> segment = end < 0 ? rest : rest.Slice(0, end);
            rest = end < 0 ? ReadOnlySpan<char>.Empty : rest.Slice(end);

            int next = -1;
            foreach (int child in index.GetChildren(current))
            {
                if (index.GetName(child).Equals(segment, StringComparison.OrdinalIgnoreCase))
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

    /// <summary>
    /// Direct children of <paramref name="id"/> sorted by total size, descending. Ties are broken by name
    /// (ordinal, ignoring case), then by id, so the order is deterministic.
    /// </summary>
    public static int[] GetChildrenSortedBySize(this NodeIndex index, int id, SizeKind kind)
    {
        int[] children = index.GetChildren(id).ToArray();
        Array.Sort(children, (a, b) =>
        {
            int bySize = index.GetTotalSize(b, kind).CompareTo(index.GetTotalSize(a, kind));
            if (bySize != 0)
            {
                return bySize;
            }

            int byName = MemoryExtensions.CompareTo(index.GetName(a), index.GetName(b), StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : a.CompareTo(b);
        });

        return children;
    }

    /// <summary>
    /// The <paramref name="count"/> largest file nodes (directories and links are never returned), sorted by size
    /// descending, ties by ascending id.
    /// </summary>
    /// <remarks>
    /// Uses a bounded heap instead of sorting every node: sorting a multi-million-node index to keep the first
    /// few entries costs O(n log n) time and an n-sized temporary array. A min-heap of at most <paramref name="count"/>
    /// entries costs O(n log k) time and O(k) memory, and most candidates are rejected with one comparison
    /// against the heap root. The root is the weakest kept file: smallest size and, among equal sizes, highest id
    /// (ties are ordered by ascending id, so a higher id loses).
    /// </remarks>
    public static int[] GetLargestFiles(this NodeIndex index, int count, SizeKind kind)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count must not be negative.");
        }

        if (count == 0)
        {
            return Array.Empty<int>();
        }

        var heap = new PriorityQueue<int, (long Size, int NegId)>();
        for (int id = 0; id < index.Count; id++)
        {
            if (index.GetKind(id) != NodeKind.File)
            {
                continue;
            }

            var priority = (Size: index.GetTotalSize(id, kind), NegId: -id);
            if (heap.Count < count)
            {
                heap.Enqueue(id, priority);
            }
            else if (heap.TryPeek(out _, out var weakest) && priority.CompareTo(weakest) > 0)
            {
                heap.DequeueEnqueue(id, priority);
            }
        }

        // Dequeue yields the weakest entry first, so fill the result from the end.
        var result = new int[heap.Count];
        for (int i = result.Length - 1; i >= 0; i--)
        {
            result[i] = heap.Dequeue();
        }

        return result;
    }
}
