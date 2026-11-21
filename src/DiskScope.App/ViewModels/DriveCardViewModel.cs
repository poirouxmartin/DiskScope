using System.Globalization;
using System.IO;
using DiskScope.App.Formatting;
using DiskScope.Core;

namespace DiskScope.App.ViewModels;

/// <summary>What the Storage start screen needs to know about one drive. Sizes are null when unknown.</summary>
public sealed record DriveSnapshot(string RootPath, string? Label, DriveType Type, long? TotalBytes, long? FreeBytes);

/// <summary>One drive card on the Storage start screen: fill gauge, free/total and an Analyze button.</summary>
public sealed class DriveCardViewModel
{
    /// <summary>Usage from which a drive is flagged (amber).</summary>
    public const double WarningPercent = 80;

    /// <summary>Usage from which a drive is flagged as critical (red).</summary>
    public const double CriticalPercent = 90;

    public DriveCardViewModel(DriveSnapshot drive)
    {
        RootPath = drive.RootPath;
        Name = drive.RootPath.TrimEnd('\\');
        Label = string.IsNullOrWhiteSpace(drive.Label) ? DefaultLabel(drive.Type) : drive.Label!;

        if (drive.TotalBytes is > 0 and long total && drive.FreeBytes is >= 0 and long free && free <= total)
        {
            UsedPercent = (total - free) * 100.0 / total;
            UsedText = UsedPercent.Value.ToString("F1", CultureInfo.InvariantCulture) + " % used";
            FreeText = $"{ByteFormatter.Format(free)} free of {ByteFormatter.Format(total)}";
        }
        else
        {
            UsedText = MetricFormatter.Unknown;
            FreeText = "Size unknown (" + MetricFormatter.Unknown + ")";
        }
    }

    public string RootPath { get; }

    /// <summary>Drive letter without the trailing backslash, e.g. "C:".</summary>
    public string Name { get; }

    public string Label { get; }

    public string Title => $"{Name} {Label}";

    /// <summary>Used share in percent, null when the size is unknown (never shown as 0).</summary>
    public double? UsedPercent { get; }

    /// <summary>Gauge value 0..100; 0 when unknown, paired with <see cref="IsSizeKnown"/> so the gauge is hidden.</summary>
    public double GaugeValue => UsedPercent ?? 0;

    public bool IsSizeKnown => UsedPercent.HasValue;

    public string UsedText { get; }

    public string FreeText { get; }

    public bool IsWarning => UsedPercent >= WarningPercent && UsedPercent < CriticalPercent;

    public bool IsCritical => UsedPercent >= CriticalPercent;

    private static string DefaultLabel(DriveType type) => type switch
    {
        DriveType.Removable => "Removable drive",
        DriveType.Network => "Network drive",
        DriveType.CDRom => "Optical drive",
        _ => "Local disk",
    };

    /// <summary>Fixed, removable and network drives that are ready. Never throws: an unreadable drive keeps unknown sizes.</summary>
    public static IReadOnlyList<DriveSnapshot> ReadSystemDrives()
    {
        var list = new List<DriveSnapshot>();
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return list;
        }

        foreach (DriveInfo d in drives)
        {
            if (d.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Network))
            {
                continue;
            }

            string? label = null;
            long? total = null;
            long? free = null;
            try
            {
                if (!d.IsReady)
                {
                    continue;
                }

                label = d.VolumeLabel;
                total = d.TotalSize;
                free = d.TotalFreeSpace;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Keep the drive with unknown sizes.
            }

            list.Add(new DriveSnapshot(d.RootDirectory.FullName, label, d.DriveType, total, free));
        }

        return list;
    }
}
