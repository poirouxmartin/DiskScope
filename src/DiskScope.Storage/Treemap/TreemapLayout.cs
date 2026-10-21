using System;
using System.Collections.Generic;

namespace DiskScope.Storage.Treemap;

/// <summary>
/// One laid-out node. Output order: a parent always precedes its descendants.
/// </summary>
public readonly record struct TreemapItem(int NodeId, LayoutRect Rect, int Depth, NodeKind Kind);

/// <summary>
/// Builds a squarified treemap from a <see cref="NodeIndex"/>, breadth-first without recursion.
/// </summary>
public static class TreemapLayout
{
    /// <summary>Inset applied to a directory's rect before its children are laid out.</summary>
    public const double DirectoryPadding = 2;

    /// <summary>
    /// Lays out <paramref name="rootId"/> in <paramref name="bounds"/>, then its descendants breadth-first.
    /// Only children with a total size greater than 0 are laid out. Items with an area below
    /// <paramref name="minArea"/> are dropped and not descended. Stops once <paramref name="maxItems"/> items exist.
    /// Drawing in output order paints children over parents; the last item containing a point is the deepest.
    /// </summary>
    public static TreemapItem[] Build(NodeIndex index, int rootId, SizeKind sizeKind, LayoutRect bounds, double minArea = 9, int maxItems = 100_000)
    {
        if (maxItems <= 0 || bounds.Area < minArea)
        {
            return Array.Empty<TreemapItem>();
        }

        // The list doubles as the queue: items[head..] are added but not yet expanded.
        var items = new List<TreemapItem>();
        items.Add(new TreemapItem(rootId, bounds, 0, index.GetKind(rootId)));
        for (int head = 0; head < items.Count; head++)
        {
            TreemapItem parent = items[head];
            if (parent.Kind != NodeKind.Directory)
            {
                continue;
            }

            LayoutRect inner = parent.Rect.Inset(DirectoryPadding);
            if (inner.Width < 1 || inner.Height < 1)
            {
                continue;
            }

            // GetChildrenSortedBySize is descending, so zero-size children form a suffix.
            int[] children = index.GetChildrenSortedBySize(parent.NodeId, sizeKind);
            int count = 0;
            while (count < children.Length && index.GetTotalSize(children[count], sizeKind) > 0)
            {
                count++;
            }

            var sizes = new long[count];
            for (int i = 0; i < count; i++)
            {
                sizes[i] = index.GetTotalSize(children[i], sizeKind);
            }

            LayoutRect[] rects = SquarifiedLayout.Compute(sizes, inner);
            for (int i = 0; i < count; i++)
            {
                if (items.Count >= maxItems)
                {
                    return items.ToArray();
                }

                if (rects[i].Area < minArea)
                {
                    continue;
                }

                items.Add(new TreemapItem(children[i], rects[i], parent.Depth + 1, index.GetKind(children[i])));
            }
        }

        return items.ToArray();
    }

    /// <summary>
    /// NodeId of the last item containing the point, or -1 if none. Items are in parent-first order,
    /// so the last match is the deepest node.
    /// </summary>
    public static int HitTest(ReadOnlySpan<TreemapItem> items, double x, double y)
    {
        for (int i = items.Length - 1; i >= 0; i--)
        {
            if (items[i].Rect.Contains(x, y))
            {
                return items[i].NodeId;
            }
        }

        return -1;
    }
}
