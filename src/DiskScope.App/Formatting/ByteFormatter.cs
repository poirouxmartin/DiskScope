using System.Globalization;
using DiskScope.Core;
using DiskScope.Storage;

namespace DiskScope.App.Formatting;

public static class ByteFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value.ToString("F1", CultureInfo.InvariantCulture) + " " + Units[unit];
    }

    /// <summary>
    /// Size of a node. A directory with an incomplete subtree is a lower bound, shown with a "≥" prefix.
    /// When nothing of the node itself could be read, its size is unknown, not zero.
    /// </summary>
    public static string FormatNodeSize(long bytes, NodeStatus status)
    {
        if (status == NodeStatus.None)
        {
            return Format(bytes);
        }

        if ((status & NodeStatusExtensions.OwnErrorMask) != 0 && bytes == 0)
        {
            return MetricFormatter.Unknown;
        }

        return "≥ " + Format(bytes);
    }
}
