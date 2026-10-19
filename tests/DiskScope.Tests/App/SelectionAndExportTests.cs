using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DiskScope.App.Services;
using DiskScope.App.ViewModels;
using DiskScope.Core;

namespace DiskScope.Tests.App;

public sealed class SelectionAndExportTests
{
    [Fact]
    public void RevealNode_OnDepthThreeFile_ExpandsAncestorsAndSelectsTarget()
    {
        Fixture f = BuildFixture();
        var vm = new MainViewModel(new SaveShell());
        vm.LoadIndex(f.Index);

        vm.RevealNode(f.Deep);

        NodeViewModel root = vm.RootNodes[0];
        NodeViewModel sub = root.Children.Single(c => c.Name == "sub");
        NodeViewModel mid = sub.Children.Single(c => c.Name == "mid");
        NodeViewModel target = mid.Children.Single(c => c.Name == "deep.bin");
        Assert.True(root.IsExpanded);
        Assert.True(root.ChildrenLoaded);
        Assert.True(sub.IsExpanded);
        Assert.True(sub.ChildrenLoaded);
        Assert.True(mid.IsExpanded);
        Assert.True(mid.ChildrenLoaded);
        Assert.True(target.IsSelected);
    }

    [Fact]
    public void RevealNode_OtherNode_UnselectsPreviousTarget()
    {
        Fixture f = BuildFixture();
        var vm = new MainViewModel(new SaveShell());
        vm.LoadIndex(f.Index);
        vm.RevealNode(f.Deep);
        NodeViewModel deepVm = vm.RootNodes[0].Children.Single(c => c.Name == "sub")
            .Children.Single(c => c.Name == "mid")
            .Children.Single(c => c.Name == "deep.bin");

        vm.RevealNode(f.Other);

        NodeViewModel otherVm = vm.RootNodes[0].Children.Single(c => c.Name == "sub")
            .Children.Single(c => c.Name == "other.bin");
        Assert.False(deepVm.IsSelected);
        Assert.True(otherVm.IsSelected);
    }

    [Fact]
    public void SelectedNodeId_Set_TriggersReveal()
    {
        Fixture f = BuildFixture();
        var vm = new MainViewModel(new SaveShell());
        vm.LoadIndex(f.Index);

        vm.SelectedNodeId = f.Deep;

        NodeViewModel mid = vm.RootNodes[0].Children.Single(c => c.Name == "sub")
            .Children.Single(c => c.Name == "mid");
        NodeViewModel target = mid.Children.Single(c => c.Name == "deep.bin");
        Assert.True(mid.IsExpanded);
        Assert.True(target.IsSelected);
    }

    [Fact]
    public void LoadIndex_ResetsTreemapRootIdToZero()
    {
        Fixture f = BuildFixture();
        var vm = new MainViewModel(new SaveShell());
        vm.LoadIndex(f.Index);
        vm.TreemapRootId = f.Sub;
        Assert.Equal(f.Sub, vm.TreemapRootId);

        vm.LoadIndex(f.Index);

        Assert.Equal(0, vm.TreemapRootId);
    }

    [Fact]
    public async Task ExportCsv_WithSavePath_WritesHeaderAndReportsRows()
    {
        // System.IO.Path is qualified: UseWPF also imports System.Windows.Shapes.Path, which makes plain Path ambiguous.
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".csv");
        Fixture f = BuildFixture();
        var vm = new MainViewModel(new SaveShell { SavePath = path });
        vm.LoadIndex(f.Index);
        try
        {
            vm.ExportCsvCommand.Execute(null);
            await vm.PendingExport!;

            string firstLine = File.ReadLines(path).First().TrimStart('\uFEFF');
            Assert.Equal(CsvExporter.Header, firstLine);
            Assert.StartsWith("Exported", vm.StatusText);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportCsv_WithoutSavePath_LeavesStatusUnchanged()
    {
        Fixture f = BuildFixture();
        var vm = new MainViewModel(new SaveShell { SavePath = null });
        vm.LoadIndex(f.Index);
        string statusBefore = vm.StatusText;

        vm.ExportCsvCommand.Execute(null);
        await vm.PendingExport!;

        Assert.Equal(statusBefore, vm.StatusText);
    }

    private sealed record Fixture(NodeIndex Index, int Sub, int Deep, int Other);

    // root/ a.bin; sub/ mid/ deep.bin (depth 3); sub/ other.bin.
    private static Fixture BuildFixture()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\data");
        builder.Add(root, "a.bin", NodeKind.File, FileAttributes.Normal, 100, 4096, 0, 0);
        int sub = builder.Add(root, "sub", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        int mid = builder.Add(sub, "mid", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        int deep = builder.Add(mid, "deep.bin", NodeKind.File, FileAttributes.Normal, 200, 4096, 0, 0);
        int other = builder.Add(sub, "other.bin", NodeKind.File, FileAttributes.Normal, 300, 4096, 0, 0);
        return new Fixture(builder.Build(), sub, deep, other);
    }

    private sealed class SaveShell : IShellActions
    {
        public string? SavePath { get; set; }

        public void CopyText(string text)
        {
        }

        public void ShowInExplorer(string path)
        {
        }

        public string? AskSaveCsvPath(string suggestedFileName) => SavePath;
    }
}
