using System.Globalization;

namespace DiskScope.Core;

/// <summary>Invariant-culture display of a value with its unit. Unknown values render as "n/a", never "0".</summary>
public static class MetricFormatter
{
    public const string Unknown = "n/a";

    private static readonly string[] ByteUnits = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Format(double? value, MetricUnit unit) => value is { } v ? Format(v, unit) : Unknown;

    /// <summary>NaN or infinity (for example an average over zero samples) is unknown, never printed as "NaN".</summary>
    public static string Format(double value, MetricUnit unit) => !double.IsFinite(value) ? Unknown : unit switch
    {
        MetricUnit.Percent => value.ToString("F1", CultureInfo.InvariantCulture) + " %",
        MetricUnit.Bytes => FormatBytes(value),
        MetricUnit.BytesPerSecond => FormatBytes(value) + "/s",
        MetricUnit.Count => value.ToString("N0", CultureInfo.InvariantCulture),
        MetricUnit.Megahertz => value.ToString("N0", CultureInfo.InvariantCulture) + " MHz",
        MetricUnit.Celsius => value.ToString("F1", CultureInfo.InvariantCulture) + " °C",
        MetricUnit.Watts => value.ToString("F1", CultureInfo.InvariantCulture) + " W",
        MetricUnit.Rpm => value.ToString("N0", CultureInfo.InvariantCulture) + " RPM",
        MetricUnit.Milliseconds => value.ToString("F1", CultureInfo.InvariantCulture) + " ms",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>Binary multiples (1 KB = 1024 B), as Windows displays them.</summary>
    public static string FormatBytes(double bytes)
    {
        if (!double.IsFinite(bytes))
        {
            return Unknown;
        }

        double v = Math.Abs(bytes);
        int i = 0;
        while (v >= 1024 && i < ByteUnits.Length - 1)
        {
            v /= 1024;
            i++;
        }

        string text = (i == 0 ? v.ToString("N0", CultureInfo.InvariantCulture) : v.ToString("F1", CultureInfo.InvariantCulture)) + " " + ByteUnits[i];
        return bytes < 0 ? "-" + text : text;
    }
}
