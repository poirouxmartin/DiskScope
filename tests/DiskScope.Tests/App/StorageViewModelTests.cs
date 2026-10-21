using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiskScope.App.Services;
using DiskScope.App.ViewModels;
using DiskScope.Storage;

namespace DiskScope.Tests.App;

public sealed class StorageViewModelTests
{
    [Fact]
    public void LoadIndex_BuildsSingleExpandedRoot()
    {
        NodeIndex index = BuildIndex();
        var vm = new StorageViewModel(new FakeShell());

        vm.LoadIndex(index);

        Assert.Single(vm.RootNodes);
        NodeViewModel root = vm.RootNodes[0];
        Assert.True(root.IsExpanded);
        Assert.True(root.ChildrenLoaded);
    }

    [Fact]
    public void LargestFiles_ContainsFilesOnly_SortedBySizeDescending()
    {
        NodeIndex index = BuildIndex();
        var vm = new StorageViewModel(new FakeShell());

        vm.LoadIndex(index);

        Assert.Equal(
            new[] { "C:\\data\\a.bin", "C:\\data\\sub\\d.bin", "C:\\data\\sub\\c.bin", "C:\\data\\b.bin" },
            vm.LargestFiles.Select(r => r.Path).ToArray());
        Assert.Equal(new[] { 10000L, 6000L, 5000L, 100L }, vm.LargestFiles.Select(r => r.Size).ToArray());
        Assert.All(vm.LargestFiles, row => Assert.Equal(NodeKind.File, index.GetKind(row.Id)));
    }

    [Fact]
    public void SwitchingToAllocated_RebuildsLargestFilesByAllocatedSize()
    {
        NodeIndex index = BuildIndex();
        var vm = new StorageViewModel(new FakeShell());
        vm.LoadIndex(index);
        Assert.Equal("C:\\data\\a.bin", vm.LargestFiles[0].Path);

        vm.IsAllocated = true;

        Assert.Equal(SizeKind.Allocated, vm.SizeKind);
        Assert.Equal(
            new[] { "C:\\data\\b.bin", "C:\\data\\a.bin" },
            vm.LargestFiles.Take(2).Select(r => r.Path).ToArray());
        Assert.Equal(8192L, vm.LargestFiles[0].Size);
    }

    [Fact]
    public void CopyPath_RecordsFullPathOfLargestFile()
    {
        NodeIndex index = BuildIndex();
        var fake = new FakeShell();
        var vm = new StorageViewModel(fake);
        vm.LoadIndex(index);
        FileRowViewModel top = vm.LargestFiles[0];

        vm.CopyPathCommand.Execute(top);

        Assert.Single(fake.Copied);
        Assert.Equal(index.GetFullPath(top.Id), fake.Copied[0]);
        Assert.Empty(fake.Shown);
    }

    [Fact]
    public void OpenInExplorer_RecordsFullPathOfChildNode()
    {
        NodeIndex index = BuildIndex();
        var fake = new FakeShell();
        var vm = new StorageViewModel(fake);
        vm.LoadIndex(index);
        NodeViewModel sub = vm.RootNodes[0].Children.Single(c => c.Name == "sub");

        vm.OpenInExplorerCommand.Execute(sub);

        Assert.Single(fake.Shown);
        Assert.Equal(index.GetFullPath(sub.Id), fake.Shown[0]);
        Assert.Empty(fake.Copied);
    }

    [Fact]
    public void Placeholder_IsIgnoredByBothCommands()
    {
        NodeIndex index = BuildIndex();
        var fake = new FakeShell();
        var vm = new StorageViewModel(fake);
        vm.LoadIndex(index);

        vm.CopyPathCommand.Execute(NodeViewModel.Placeholder);
        vm.OpenInExplorerCommand.Execute(NodeViewModel.Placeholder);

        Assert.Empty(fake.Copied);
        Assert.Empty(fake.Shown);
    }

    // Logical ranking: a.bin 10000, sub/d.bin 6000, sub/c.bin 5000, b.bin 100 (sub dir totals 11000, must be excluded).
    // Allocated ranking: b.bin 8192, a.bin 4096, sub/c.bin 1024, sub/d.bin 1024.
    private static NodeIndex BuildIndex()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\data");
        builder.Add(root, "a.bin", NodeKind.File, FileAttributes.Normal, 10000, 4096, 0, 0);
        builder.Add(root, "b.bin", NodeKind.File, FileAttributes.Normal, 100, 8192, 0, 0);
        int sub = builder.Add(root, "sub", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        builder.Add(sub, "c.bin", NodeKind.File, FileAttributes.Normal, 5000, 1024, 0, 0);
        builder.Add(sub, "d.bin", NodeKind.File, FileAttributes.Normal, 6000, 1024, 0, 0);
        return builder.Build();
    }

    private sealed class FakeShell : IShellActions
    {
        public List<string> Copied { get; } = new();

        public List<string> Shown { get; } = new();

        public void CopyText(string text) => Copied.Add(text);

        public void ShowInExplorer(string path) => Shown.Add(path);

        public string? SavePath { get; set; }

        public string? AskSaveCsvPath(string suggestedFileName) => SavePath;
    }
}
