using System.Globalization;
using System.Resources;
using System.Windows.Markup;
using DiskScope.Core;

namespace DiskScope.App.Localization;

/// <summary>
/// UI strings. English is the neutral resource (Resources/Strings.resx); French comes from Strings.fr.resx
/// and is picked when the Windows display language is French (CurrentUICulture). A missing key shows the key
/// itself, so a gap is visible instead of silently empty.
/// </summary>
public static class Loc
{
    private static readonly ResourceManager Resources = new("DiskScope.App.Resources.Strings", typeof(Loc).Assembly);

    /// <summary>Culture used for lookups; null = CurrentUICulture. Tests pin it to keep assertions stable.</summary>
    public static CultureInfo? Culture { get; set; }

    public static string Get(string key) => TryGet(key) ?? key;

    public static string? TryGet(string key) => Resources.GetString(key, Culture ?? CultureInfo.CurrentUICulture);

    /// <summary>Composite format with invariant number formatting (values are pre-formatted by MetricFormatter).</summary>
    public static string Format(string key, params object?[] args) => string.Format(CultureInfo.InvariantCulture, Get(key), args);

    /// <summary>Display name of a metric: "Metric.{id}" when translated, else the collector's English name.</summary>
    public static string MetricName(MetricDescriptor descriptor) =>
        TryGet("Metric." + descriptor.Id) ?? TranslateSuffix(descriptor.DisplayName);

    // Per-volume and per-adapter names are built at run time ("C: used", "GPU 0 (name) usage"):
    // the fixed English suffix is translated and the prefix (volume letter, adapter name) kept. Longest first.
    private static readonly (string English, string Key)[] MetricSuffixes =
    [
        (" dedicated memory used", "MetricSuffix_GpuDedicated"),
        (" shared memory used", "MetricSuffix_GpuShared"),
        (" usage", "MetricSuffix_GpuUsage"),
        (": used", "MetricSuffix_VolumeUsed"),
        (": free", "MetricSuffix_VolumeFree"),
        (": size", "MetricSuffix_VolumeSize"),
    ];

    public static string TranslateSuffix(string name)
    {
        foreach ((string english, string key) in MetricSuffixes)
        {
            if (name.Length > english.Length && name.EndsWith(english, StringComparison.Ordinal) && TryGet(key) is { } translated)
            {
                return name[..^english.Length] + translated;
            }
        }

        return name;
    }

    /// <summary>Definition of a metric: "MetricDef.{id}" when translated, else the collector's English text.</summary>
    public static string? MetricDefinition(MetricDescriptor descriptor) =>
        descriptor.Definition is null ? null : TryGet("MetricDef." + descriptor.Id) ?? descriptor.Definition;
}

/// <summary>XAML converter: MetricDescriptor to its translated display name (lists of metrics).</summary>
public sealed class MetricNameConverter : System.Windows.Data.IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is MetricDescriptor d ? Loc.MetricName(d) : value?.ToString();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>XAML: <c>Text="{loc:Loc Storage_Title}"</c>. Resolved once at load; the language follows Windows at start-up.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Get(Key);
}
