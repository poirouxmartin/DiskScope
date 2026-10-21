using System.IO;
using System.Linq;
using DiskScope.App.ViewModels;
using DiskScope.Storage;

namespace DiskScope.Tests.App;

public sealed class NodeViewModelTests
{
    [Fact]
    public void RootWithChildren_HoldsPlaceholderUntilExpanded()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\data");
        builder.Add(root, "a.bin", NodeKind.File, FileAttributes.Normal, 10, 4096, 0, 0);
        NodeIndex index = builder.Build();

        var vm = new NodeViewModel(index, NodeIndex.RootId, SizeKind.Logical, parentTotal: 0);

        Assert.Single(vm.Children);
        Assert.Same(NodeViewModel.Placeholder, vm.Children[0]);
        Assert.False(vm.ChildrenLoaded);
    }

    [Fact]
    public void Expansion_OrdersChildrenBySizeDescending_AndPercentsSumTo100()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\data");
        builder.Add(root, "small", NodeKind.File, FileAttributes.Normal, 20, 0, 0, 0);
        builder.Add(root, "big", NodeKind.File, FileAttributes.Normal, 50, 0, 0, 0);
        builder.Add(root, "mid", NodeKind.File, FileAttributes.Normal, 30, 0, 0, 0);
        NodeIndex index = builder.Build();

        var vm = new NodeViewModel(index, NodeIndex.RootId, SizeKind.Logical, parentTotal: 0);
        vm.IsExpanded = true;

        Assert.True(vm.ChildrenLoaded);
        Assert.Equal(new[] { "big", "mid", "small" }, vm.Children.Select(c => c.Name).ToArray());
        Assert.Equal(new[] { 50L, 30L, 20L }, vm.Children.Select(c => c.TotalSize).ToArray());
        double sum = vm.Children.Sum(c => c.PercentOfParent);
        Assert.InRange(sum, 99.99, 100.01);
    }

    [Fact]
    public void FileNode_HasNoChildren()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\data");
        int file = builder.Add(root, "a.bin", NodeKind.File, FileAttributes.Normal, 10, 4096, 0, 0);
        NodeIndex index = builder.Build();

        var vm = new NodeViewModel(index, file, SizeKind.Logical, parentTotal: 10);

        Assert.Empty(vm.Children);
    }

    [Fact]
    public void AllocatedKind_UsesAllocatedTotals_AndOrdersByAllocatedSize()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\data");
        builder.Add(root, "a.bin", NodeKind.File, FileAttributes.Normal, 10000, 4096, 0, 0);
        builder.Add(root, "b.bin", NodeKind.File, FileAttributes.Normal, 100, 8192, 0, 0);
        NodeIndex index = builder.Build();

        var allocated = new NodeViewModel(index, NodeIndex.RootId, SizeKind.Allocated, parentTotal: 0);
        allocated.IsExpanded = true;

        Assert.Equal(index.GetTotalAllocatedSize(NodeIndex.RootId), allocated.TotalSize);
        Assert.Equal(new[] { "b.bin", "a.bin" }, allocated.Children.Select(c => c.Name).ToArray());
        Assert.Equal(new[] { 8192L, 4096L }, allocated.Children.Select(c => c.TotalSize).ToArray());

        var logical = new NodeViewModel(index, NodeIndex.RootId, SizeKind.Logical, parentTotal: 0);
        logical.IsExpanded = true;

        Assert.Equal(new[] { "a.bin", "b.bin" }, logical.Children.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void AccessDeniedDirectory_IsIncompleteWithStatusText_CleanFileIsNot()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\data");
        int locked = builder.Add(root, "locked", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        int clean = builder.Add(root, "clean.txt", NodeKind.File, FileAttributes.Normal, 10, 4096, 0, 0);
        builder.AddError(new ScanError(locked, ScanErrorKind.AccessDenied, 5, "Access denied"));
        NodeIndex index = builder.Build();

        var lockedVm = new NodeViewModel(index, locked, SizeKind.Logical, parentTotal: 0);
        var cleanVm = new NodeViewModel(index, clean, SizeKind.Logical, parentTotal: 0);

        Assert.True(lockedVm.IsIncomplete);
        Assert.Contains("AccessDenied", lockedVm.StatusText);
        Assert.False(cleanVm.IsIncomplete);
        Assert.Equal(string.Empty, cleanVm.StatusText);
    }
}
