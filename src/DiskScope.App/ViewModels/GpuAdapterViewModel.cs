using System.Collections.ObjectModel;
using System.Globalization;
using DiskScope.App.Localization;
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
        // The section title already names the adapter: tiles and rows drop the "GPU n (name)" prefix.
        SetTitle(UsageTile, "Gpu_Tile_Usage");
        SetTitle(DedicatedTile, "Gpu_Tile_Dedicated");
        SetTitle(SharedTile, "Gpu_Tile_Shared");

        // Engine names (3d, copy, videodecode...) are the driver's own labels and stay untranslated.
        string? engineDefinition = Loc.TryGet("Gpu_EngineDefinition");
        EngineRows = new ObservableCollection<MetricRowViewModel>(
            GpuCollector.EngineGroups
                .Select(g => descriptors.TryGetValue(GpuCollector.EngineId(index, g), out MetricDescriptor? d)
                    ? new MetricRowViewModel(d, ShortName(d.DisplayName), d.Definition is null ? null : engineDefinition)
                    : null)
                .OfType<MetricRowViewModel>());
    }

    private void SetTitle(MetricTileViewModel? tile, string key)
    {
        if (tile is not null)
        {
            tile.Title = Loc.TryGet(key) ?? ShortName(tile.Title);
        }
    }

    /// <summary>Removes the leading adapter label ("GPU 0 (NVIDIA ...) ") and capitalizes the rest; other names are unchanged.</summary>
    public string ShortName(string displayName)
    {
        string prefix = string.Create(CultureInfo.InvariantCulture, $"GPU {Index} ({Info.Name}) ");
        if (!displayName.StartsWith(prefix, StringComparison.Ordinal) || displayName.Length == prefix.Length)
        {
            return displayName;
        }

        string rest = displayName[prefix.Length..];
        return char.ToUpperInvariant(rest[0]) + rest[1..];
    }

    public int Index { get; }

    public GpuAdapterInfo Info { get; }

    public string Title => $"GPU {Index}: {Info.Name}";

    public override string ToString() => Title;

    public string Identity => Loc.Format("Gpu_Identity", Info.VendorId.ToString("X4", CultureInfo.InvariantCulture), Info.Luid,
        MetricFormatter.FormatBytes(Info.DedicatedVideoMemory), MetricFormatter.FormatBytes(Info.SharedSystemMemory));

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
