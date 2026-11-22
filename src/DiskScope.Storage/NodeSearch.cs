using System;
using System.Collections.Generic;
using System.Threading;

namespace DiskScope.Storage;

/// <summary>Result of <see cref="NodeSearch.Find"/>.</summary>
/// <param name="Ids">Top matches, size descending, ties by ascending id. Empty when maxResults is 0.</param>
/// <param name="TotalMatches">All matches, before truncation.</param>
/// <param name="UnknownDateExcluded">Nodes that matched every other criterion but have an unknown date. Counted only when a date criterion is set.</param>
/// <param name="Truncated">True when TotalMatches is greater than maxResults.</param>
public sealed record SearchResult(int[] Ids, int TotalMatches, int UnknownDateExcluded, bool Truncated);

/// <summary>
/// Filtered search over a <see cref="NodeIndex"/>.
/// </summary>
/// <remarks>
/// The scan is linear on purpose: no secondary index. Measured in the benchmarks (`synthetic`), a single pass is fast
/// enough that an index is not needed below ~10M nodes. Unknown last-write dates (ticks 0) are never treated as matching.
/// </remarks>
public static class NodeSearch
{
    private const int CancellationCheckMask = 0xFFFF; // cancellation is checked every 65536 nodes

    /// <summary>
    /// Single pass over the index. Keeps the top <paramref name="maxResults"/> matches by total size in
    /// <see cref="NodeFilter.SizeKind"/>, using a bounded min-heap as in <see cref="NodeIndexQueries.GetLargestFiles"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxResults"/> is negative.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static SearchResult Find(NodeIndex index, NodeFilter filter, int maxResults, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(filter);
        if (maxResults < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults), maxResults, "Max results must not be negative.");
        }

        string? name = string.IsNullOrEmpty(filter.NameContains) ? null : filter.NameContains;
        HashSet<string>? extensions = NormalizeExtensions(filter.Extensions);
        bool dateFilter = filter.ModifiedFromUtc is not null || filter.ModifiedToUtc is not null;
        long fromTicks = filter.ModifiedFromUtc?.Ticks ?? long.MinValue;
        long toTicks = filter.ModifiedToUtc?.Ticks ?? long.MaxValue;
        long minSize = filter.MinSize ?? long.MinValue;
        long maxSize = filter.MaxSize ?? long.MaxValue;
        SizeKind sizeKind = filter.SizeKind;

        var top = new TopKBySize(maxResults);
        int totalMatches = 0;
        int unknownDateExcluded = 0;
        for (int id = 1; id < index.Count; id++)
        {
            if ((id & CancellationCheckMask) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            NodeKind kind = index.GetKind(id);
            bool kindMatches = filter.Kind switch
            {
                KindFilter.Files => kind == NodeKind.File,
                KindFilter.Directories => kind == NodeKind.Directory,
                _ => kind != NodeKind.Link,
            };
            if (!kindMatches)
            {
                continue;
            }

            if (name is not null && index.GetName(id).IndexOf(name.AsSpan(), StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (extensions is not null
                && (kind != NodeKind.File || !extensions.GetAlternateLookup<ReadOnlySpan<char>>().Contains(GetExtension(index.GetName(id)))))
            {
                continue;
            }

            long size = index.GetTotalSize(id, sizeKind);
            if (size < minSize || size > maxSize)
            {
                continue;
            }

            if (dateFilter)
            {
                long ticks = index.GetLastWriteUtc(id).Ticks;
                if (ticks == 0)
                {
                    unknownDateExcluded++;
                    continue;
                }

                if (ticks < fromTicks || ticks > toTicks)
                {
                    continue;
                }
            }

            totalMatches++;
            top.Add(id, size);
        }

        return new SearchResult(top.ToSortedArray(),totalMatches, unknownDateExcluded, totalMatches > maxResults);
    }

    /// <summary>
    /// Extension of a file name: the text after the last '.', only when that dot is not the first or the last character.
    /// ".gitignore" gives "", "a.tar.gz" gives "gz", "a." gives "".
    /// </summary>
    public static ReadOnlySpan<char> GetExtension(ReadOnlySpan<char> name)
    {
        int dot = name.LastIndexOf('.');
        return dot > 0 && dot < name.Length - 1 ? name[(dot + 1)..] : ReadOnlySpan<char>.Empty;
    }

    /// <summary>Trims each entry and removes one leading '.'. Returns null when there is no criterion.</summary>
    private static HashSet<string>? NormalizeExtensions(IReadOnlyCollection<string>? extensions)
    {
        if (extensions is null || extensions.Count == 0)
        {
            return null;
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in extensions)
        {
            string trimmed = raw.Trim();
            set.Add(trimmed.StartsWith('.') ? trimmed[1..] : trimmed);
        }

        return set;
    }
}
