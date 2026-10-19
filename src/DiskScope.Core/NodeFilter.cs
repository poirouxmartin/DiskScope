using System;
using System.Collections.Generic;

namespace DiskScope.Core;

/// <summary>Which node kinds a <see cref="NodeFilter"/> accepts. Links never match, whatever the value.</summary>
public enum KindFilter { Any, Files, Directories }

/// <summary>
/// Search criteria for <see cref="NodeSearch.Find"/>. All set criteria must hold (logical AND).
/// Size criteria apply to the total size of the node in <see cref="SizeKind"/> (subtree total for directories).
/// Date criteria are on the UTC last-write ticks; a node with unknown date (ticks 0) never matches a date criterion.
/// The root never matches.
/// </summary>
public sealed record NodeFilter
{
    /// <summary>Case-insensitive substring of the node name. Null or empty means no criterion.</summary>
    public string? NameContains { get; init; }

    /// <summary>
    /// Files only. Normalized by <see cref="NodeSearch"/>: trimmed, one leading '.' removed, case-insensitive.
    /// "" matches files without extension. Null or empty means no criterion.
    /// </summary>
    public IReadOnlyCollection<string>? Extensions { get; init; }

    /// <summary>Inclusive lower bound on the total size of <see cref="SizeKind"/>.</summary>
    public long? MinSize { get; init; }

    /// <summary>Inclusive upper bound on the total size of <see cref="SizeKind"/>.</summary>
    public long? MaxSize { get; init; }

    /// <summary>Inclusive lower bound on the UTC last-write time.</summary>
    public DateTime? ModifiedFromUtc { get; init; }

    /// <summary>Inclusive upper bound on the UTC last-write time.</summary>
    public DateTime? ModifiedToUtc { get; init; }

    public KindFilter Kind { get; init; } = KindFilter.Any;

    public SizeKind SizeKind { get; init; } = SizeKind.Logical;

    /// <summary>True when no criterion is set (Kind Any, every other criterion null or empty).</summary>
    public bool IsEmpty =>
        string.IsNullOrEmpty(NameContains)
        && Extensions is not { Count: > 0 }
        && MinSize is null
        && MaxSize is null
        && ModifiedFromUtc is null
        && ModifiedToUtc is null
        && Kind == KindFilter.Any;
}
