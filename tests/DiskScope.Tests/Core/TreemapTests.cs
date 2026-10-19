using System;
using System.IO;
using DiskScope.Core;
using DiskScope.Core.Treemap;

namespace DiskScope.Tests;

public sealed class TreemapTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    public void Compute_SingleItem_FillsBounds()
    {
        var bounds = new LayoutRect(3, 4, 5, 6);

        LayoutRect[] rects = SquarifiedLayout.Compute(new long[] { 42 }, bounds);

        AssertRectEqual(bounds, rects[0]);
    }

    [Fact]
    public void Compute_AreasAreProportionalToSizes()
    {
        long[] sizes = { 6, 5, 3, 1 };
        var bounds = new LayoutRect(0, 0, 10, 10);

        LayoutRect[] rects = SquarifiedLayout.Compute(sizes, bounds);

        for (int i = 0; i < sizes.Length; i++)
        {
            double expected = sizes[i] * bounds.Area / 15.0;
            Assert.True(Math.Abs(rects[i].Area - expected) <= 1e-6 * expected, $"item {i}: area {rects[i].Area}, expected {expected}");
        }
    }

    [Fact]
    public void Compute_RandomSizes_TileBoundsWithoutOverlap()
    {
        var random = new Random(3);
        long[] sizes = new long[50];
        for (int i = 0; i < sizes.Length; i++)
        {
            sizes[i] = random.NextInt64(1, 10_000);
        }

        Array.Sort(sizes);
        Array.Reverse(sizes);
        var bounds = new LayoutRect(10, 20, 800, 600);

        LayoutRect[] rects = SquarifiedLayout.Compute(sizes, bounds);

        double total = 0;
        foreach (long size in sizes)
        {
            total += size;
        }

        double areaSum = 0;
        for (int i = 0; i < rects.Length; i++)
        {
            AssertInside(rects[i], bounds);
            double expected = sizes[i] * bounds.Area / total;
            Assert.True(Math.Abs(rects[i].Area - expected) <= 1e-6 * expected, $"item {i}: area {rects[i].Area}, expected {expected}");
            areaSum += rects[i].Area;

            for (int j = i + 1; j < rects.Length; j++)
            {
                Assert.True(IntersectionArea(rects[i], rects[j]) < Tolerance, $"items {i} and {j} overlap");
            }
        }

        Assert.True(Math.Abs(areaSum - bounds.Area) <= 1e-9 * bounds.Area, $"area sum {areaSum}, bounds {bounds.Area}");
    }

    [Fact]
    public void Compute_PaperExample_WorstAspectRatioIsAtMost3()
    {
        long[] sizes = { 6, 6, 4, 3, 2, 2, 1 };

        LayoutRect[] rects = SquarifiedLayout.Compute(sizes, new LayoutRect(0, 0, 6, 4));

        double worst = 0;
        foreach (LayoutRect r in rects)
        {
            worst = Math.Max(worst, Math.Max(r.Width / r.Height, r.Height / r.Width));
        }

        Assert.True(worst <= 3.0, $"worst aspect ratio {worst}");
    }

    [Fact]
    public void Compute_ZeroSizes_GetEmptyRectsAtBoundsOrigin()
    {
        var bounds = new LayoutRect(1, 2, 10, 10);

        LayoutRect[] rects = SquarifiedLayout.Compute(new long[] { 5, 0, 0 }, bounds);

        AssertRectEqual(bounds, rects[0]);
        Assert.Equal(new LayoutRect(1, 2, 0, 0), rects[1]);
        Assert.Equal(new LayoutRect(1, 2, 0, 0), rects[2]);

        LayoutRect[] allZero = SquarifiedLayout.Compute(new long[] { 0, 0 }, bounds);
        Assert.Equal(new LayoutRect(1, 2, 0, 0), allZero[0]);
        Assert.Equal(new LayoutRect(1, 2, 0, 0), allZero[1]);
    }

    [Fact]
    public void Inset_ShrinksEachSideAndNeverGoesNegative()
    {
        var rect = new LayoutRect(0, 0, 10, 10);

        Assert.Equal(new LayoutRect(1, 1, 8, 8), rect.Inset(1));
        Assert.Equal(0.0, rect.Inset(6).Width);
        Assert.Equal(0.0, rect.Inset(6).Height);
    }

    [Fact]
    public void Build_RootFirst_ChildrenInsidePaddedParent_ParentsPrecedeChildren()
    {
        NodeIndex index = BuildSampleIndex(out int root, out int big, out int sub, out int inner, out _);
        var bounds = new LayoutRect(0, 0, 1000, 800);

        TreemapItem[] items = TreemapLayout.Build(index, root, SizeKind.Logical, bounds);

        Assert.Equal(new[] { root, big, sub, inner }, Array.ConvertAll(items, t => t.NodeId));
        Assert.Equal(0, items[0].Depth);
        AssertRectEqual(bounds, items[0].Rect);
        Assert.Equal(2, items[3].Depth);
        Assert.Equal(NodeKind.File, items[3].Kind);

        LayoutRect rootInner = bounds.Inset(TreemapLayout.DirectoryPadding);
        Assert.True(Within(items[1].Rect, rootInner), "big.bin is outside the padded root");
        Assert.True(Within(items[2].Rect, rootInner), "sub is outside the padded root");
        Assert.True(Within(items[3].Rect, items[2].Rect.Inset(TreemapLayout.DirectoryPadding)), "inner.bin is outside the padded sub");
    }

    [Fact]
    public void Build_DropsItemsBelowMinArea()
    {
        NodeIndex index = BuildSampleIndex(out int root, out _, out _, out _, out int tiny);

        TreemapItem[] items = TreemapLayout.Build(index, root, SizeKind.Logical, new LayoutRect(0, 0, 1000, 800));

        Assert.False(Array.Exists(items, t => t.NodeId == tiny));
    }

    [Fact]
    public void Build_MaxItems_CapsOutput()
    {
        NodeIndex index = BuildSampleIndex(out int root, out _, out _, out _, out _);

        TreemapItem[] items = TreemapLayout.Build(index, root, SizeKind.Logical, new LayoutRect(0, 0, 1000, 800), maxItems: 2);

        Assert.Equal(2, items.Length);
        Assert.Equal(root, items[0].NodeId);
    }

    [Fact]
    public void HitTest_ReturnsDeepestContainingItem_MinusOneOutside()
    {
        NodeIndex index = BuildSampleIndex(out int root, out _, out _, out int inner, out _);
        TreemapItem[] items = TreemapLayout.Build(index, root, SizeKind.Logical, new LayoutRect(0, 0, 1000, 800));
        LayoutRect innerRect = items[Array.FindIndex(items, t => t.NodeId == inner)].Rect;

        Assert.Equal(inner, TreemapLayout.HitTest(items, innerRect.X + innerRect.Width / 2, innerRect.Y + innerRect.Height / 2));
        Assert.Equal(-1, TreemapLayout.HitTest(items, -10, -10));
    }

    // root
    // +-- big.bin   1,000,000
    // +-- sub/      500,000
    // |   +-- inner.bin 500,000
    // +-- tiny.bin  1 (dropped by minArea)
    private static NodeIndex BuildSampleIndex(out int root, out int big, out int sub, out int inner, out int tiny)
    {
        var builder = new NodeIndexBuilder();
        root = builder.AddRoot("C:\\treemap");
        big = builder.Add(root, "big.bin", NodeKind.File, FileAttributes.Normal, 1_000_000, 1_000_000, 0, 0);
        sub = builder.Add(root, "sub", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        inner = builder.Add(sub, "inner.bin", NodeKind.File, FileAttributes.Normal, 500_000, 500_000, 0, 0);
        tiny = builder.Add(root, "tiny.bin", NodeKind.File, FileAttributes.Normal, 1, 1, 0, 0);
        return builder.Build();
    }

    private static void AssertRectEqual(LayoutRect expected, LayoutRect actual)
    {
        Assert.True(
            Math.Abs(expected.X - actual.X) <= Tolerance
            && Math.Abs(expected.Y - actual.Y) <= Tolerance
            && Math.Abs(expected.Width - actual.Width) <= Tolerance
            && Math.Abs(expected.Height - actual.Height) <= Tolerance,
            $"expected {expected}, actual {actual}");
    }

    private static bool Within(LayoutRect r, LayoutRect outer) =>
        r.X >= outer.X - Tolerance
        && r.Y >= outer.Y - Tolerance
        && r.X + r.Width <= outer.X + outer.Width + Tolerance
        && r.Y + r.Height <= outer.Y + outer.Height + Tolerance;

    private static void AssertInside(LayoutRect r, LayoutRect bounds) =>
        Assert.True(Within(r, bounds), $"{r} is outside {bounds}");

    private static double IntersectionArea(LayoutRect a, LayoutRect b)
    {
        double w = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X);
        double h = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y);
        return w > 0 && h > 0 ? w * h : 0;
    }
}
