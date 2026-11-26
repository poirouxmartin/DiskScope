using System;
using System.Collections.Generic;
using System.Linq;
using DiskScope.App.ViewModels;
using DiskScope.Core;
using DiskScope.Monitoring.Gpu;
using DiskScope.Processes;

namespace DiskScope.Tests.App;

public sealed class ReviewFollowUpTests
{
    private static GpuAdapterViewModel Adapter() =>
        new(0, new GpuAdapterInfo("NVIDIA GeForce RTX 5070", "0x0_0x1", 0x10DE, 1L << 30, 1L << 30, false),
            _ => null, new Dictionary<string, MetricDescriptor>());

    [Theory]
    [InlineData("GPU 0 (NVIDIA GeForce RTX 5070) usage", "Usage")]
    [InlineData("GPU 0 (NVIDIA GeForce RTX 5070) 3d", "3d")]
    [InlineData("GPU 0 (NVIDIA GeForce RTX 5070) videodecode", "Videodecode")]
    [InlineData("GPU 1 (NVIDIA GeForce RTX 5070) usage", "GPU 1 (NVIDIA GeForce RTX 5070) usage")]
    [InlineData("GPU 0 (NVIDIA GeForce RTX 5070) ", "GPU 0 (NVIDIA GeForce RTX 5070) ")]
    [InlineData("Something else", "Something else")]
    public void Gpu_ShortName_DropsOwnAdapterPrefixOnly(string name, string expected) =>
        Assert.Equal(expected, Adapter().ShortName(name));

    private static ProcessSnapshot Proc(int pid, string name, long? privateBytes) => new()
    {
        Key = new ProcessKey(pid, pid),
        ParentProcessId = 0,
        Name = name,
        ThreadCount = 1,
        IsAccessible = privateBytes is not null,
        PrivateBytes = privateBytes,
    };

    private static ProcessesViewModel.Snapshot Snap(params ProcessSnapshot[] processes) =>
        new(new ProcessReadResult(processes, 0, TimeSpan.Zero),
            processes.ToDictionary(p => p.Key, _ => (double?)null),
            ProcessTree.Build(processes),
            new Dictionary<ProcessKey, string>(),
            new ConsumerReport([], 0, null, 0, 0));

    [Fact]
    public void Processes_DefaultOrder_IsLargestMemoryFirst_UnknownLast()
    {
        var vm = new ProcessesViewModel();
        vm.Apply(Snap(Proc(1, "small.exe", 100), Proc(2, "locked.exe", null), Proc(3, "big.exe", 400), Proc(4, "mid.exe", 200)));

        Assert.Equal(new[] { "big.exe", "mid.exe", "small.exe", "locked.exe" }, vm.Rows.Select(r => r.Name).ToArray());
        Assert.Equal(100, vm.Rows[0].MemoryShare);
        Assert.Equal(50, vm.Rows[1].MemoryShare);
        Assert.Equal(25, vm.Rows[2].MemoryShare);
        Assert.Null(vm.Rows[3].MemoryShare);
    }

    [Fact]
    public void Processes_AllMemoryUnknown_HasNoBars()
    {
        var vm = new ProcessesViewModel();
        vm.Apply(Snap(Proc(1, "a.exe", null), Proc(2, "b.exe", null)));

        Assert.All(vm.Rows, r => Assert.Null(r.MemoryShare));
    }

    [Fact]
    public void Processes_TreeToggle_ReappliesLastSnapshot()
    {
        var vm = new ProcessesViewModel();
        vm.Apply(Snap(Proc(1, "b.exe", 400), Proc(2, "a.exe", 100)));
        Assert.Equal("b.exe", vm.Rows[0].Name);

        vm.ShowTree = true;

        Assert.Equal(new[] { "a.exe", "b.exe" }, vm.Rows.Select(r => r.Name).ToArray());
        vm.ShowTree = false;
        Assert.Equal("b.exe", vm.Rows[0].Name);
    }
}
