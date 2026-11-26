using DiskScope.Benchmarks;
using DiskScope.Tests.Standard;

namespace DiskScope.Tests.Benchmarks;

/// <summary>The compare baseline walk on a live tree, where directories can vanish between listing and visit.</summary>
public sealed class BaselineWalkTests
{
    [Fact]
    public void DirectoryDeletedBeforeVisit_IsCountedAsVanished_AndSiblingsStayCounted()
    {
        using var tree = new TempTree();
        tree.File("keep/a.bin", 100);
        string gone = tree.Dir("gone");
        tree.File("gone/b.bin", 500);

        Commands.Baseline baseline = Commands.WalkBaseline(tree.Root, path =>
        {
            if (string.Equals(path, gone, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(gone, true);
            }
        });

        Assert.Equal(1, baseline.Files);
        Assert.Equal(100L, baseline.LogicalBytes);
        Assert.Equal(1, baseline.VanishedDirectories);
    }

    [Fact]
    public void StableTree_HasNoVanishedDirectory()
    {
        using var tree = new TempTree();
        tree.File("a/x.bin", 10);
        tree.File("b/y.bin", 20);

        Commands.Baseline baseline = Commands.WalkBaseline(tree.Root);

        Assert.Equal(2, baseline.Files);
        Assert.Equal(30L, baseline.LogicalBytes);
        Assert.Equal(0, baseline.VanishedDirectories);
    }
}
