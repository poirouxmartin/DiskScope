using System.Collections.Generic;

namespace DiskScope.Storage;

/// <summary>
/// Bounded min-heap keeping the <c>k</c> largest candidates by size. Ties are ordered by ascending id, so the
/// root is the weakest kept entry: smallest size and, among equal sizes, highest id.
/// </summary>
internal sealed class TopKBySize
{
    private readonly int _k;
    private readonly PriorityQueue<int, (long Size, int NegId)> _heap = new();

    public TopKBySize(int k)
    {
        _k = k;
    }

    public void Add(int id, long size)
    {
        var priority = (Size: size, NegId: -id);
        if (_heap.Count < _k)
        {
            _heap.Enqueue(id, priority);
        }
        else if (_heap.TryPeek(out _, out var weakest) && priority.CompareTo(weakest) > 0)
        {
            _heap.DequeueEnqueue(id, priority);
        }
    }

    /// <summary>Drains the heap: ids sorted by size descending, ties by ascending id.</summary>
    public int[] ToSortedArray()
    {
        // Dequeue yields the weakest entry first, so fill the result from the end.
        var result = new int[_heap.Count];
        for (int i = result.Length - 1; i >= 0; i--)
        {
            result[i] = _heap.Dequeue();
        }

        return result;
    }
}
