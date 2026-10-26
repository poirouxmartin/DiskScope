using System.Collections.ObjectModel;
using System.Globalization;
using DiskScope.Core;
using DiskScope.Monitoring.Gpu;

namespace DiskScope.App.ViewModels;

/// <summary>One adapter of the GPU view: overall and per-engine usage, dedicated and shared memory, identity.</summary>
public sealed class GpuAdapterViewModel
{
    public GpuAdapterViewModel(int index, GpuAdapterInfo info, Func<string, MetricTileViewModel?> tile, IReadOnlyDictionary<string, MetricDescriptor> descriptors)
    {
        Index = index;
        Info = info;
        UsageTile = tile(GpuCollector.UtilizationId(index));
        DedicatedTile = tile(GpuCollector.DedicatedUsedId(index));
        SharedTile = tile(GpuCollector.SharedUsedId(index));
        EngineRows = new ObservableCollection<MetricRowViewModel>(
            GpuCollector.EngineGroups
                .Select(g => descriptors.TryGetValue(GpuCollector.EngineId(index, g), out MetricDescriptor? d) ? new MetricRowViewModel(d) : null)
                .OfType<MetricRowViewModel>());
    }

    public int Index { get; }

    public GpuAdapterInfo Info { get; }

    public string Title => $"GPU {Index}: {Info.Name}";

    public override string ToString() => Title;

    public string Identity => string.Create(CultureInfo.InvariantCulture,
        $"Vendor 0x{Info.VendorId:X4}, LUID {Info.Luid}, dedicated video memory {MetricFormatter.FormatBytes(Info.DedicatedVideoMemory)}, " +
        $"shared system memory limit {MetricFormatter.FormatBytes(Info.SharedSystemMemory)} (DXGI)");

    public MetricTileViewModel? UsageTile { get; }

    public MetricTileViewModel? DedicatedTile { get; }

    public MetricTileViewModel? SharedTile { get; }

    public ObservableCollection<MetricRowViewModel> EngineRows { get; }
}

/// <summary>A process using the GPU, by its busiest engine, as attributed by the GPU Engine counters.</summary>
public sealed record GpuProcessRow(int Pid, string Name, double Percent)
{
    public string PercentText => MetricFormatter.Format(Percent, MetricUnit.Percent);
}
