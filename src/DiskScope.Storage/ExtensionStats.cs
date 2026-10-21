using System;
using System.Collections.Generic;
using System.Linq;

namespace DiskScope.Storage;

/// <summary>File count and total size for one extension.</summary>
/// <param name="Extension">Lowercase, without the dot. Empty for files without extension.</param>
public sealed record ExtensionStat(string Extension, long FileCount, long TotalSize);

/// <summary>Per-extension statistics over the files of a <see cref="NodeIndex"/>.</summary>
public static class ExtensionStats
{
    /// <summary>
    /// Files only: directories and links are ignored. Extensions are matched case-insensitively with
    /// <see cref="NodeSearch.GetExtension"/>. Sorted by total size descending, then by extension, ordinal.
    /// </summary>
    public static IReadOnlyList<ExtensionStat> Compute(NodeIndex index, SizeKind sizeKind)
    {
        ArgumentNullException.ThrowIfNull(index);

        // Span lookup: a string is allocated only when a new extension appears.
        var map = new Dictionary<string, (long Count, long Size)>(StringComparer.OrdinalIgnoreCase);
        var lookup = map.GetAlternateLookup<ReadOnlySpan<char>>();
        for (int id = 0; id < index.Count; id++)
        {
            if (index.GetKind(id) != NodeKind.File)
            {
                continue;
            }

            ReadOnlySpan<char> extension = NodeSearch.GetExtension(index.GetName(id));
            lookup.TryGetValue(extension, out var current);
            lookup[extension] = (current.Count + 1, current.Size + index.GetTotalSize(id, sizeKind));
        }

        return map
            .Select(pair => new ExtensionStat(pair.Key.ToLowerInvariant(), pair.Value.Count, pair.Value.Size))
            .OrderByDescending(stat => stat.TotalSize)
            .ThenBy(stat => stat.Extension, StringComparer.Ordinal)
            .ToList();
    }
}
