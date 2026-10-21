using DiskScope.Core;

namespace DiskScope.Monitoring;

/// <summary>
/// Free and total space of the fixed volumes present at start-up (DriveInfo, i.e. GetDiskFreeSpaceEx).
/// "Free" is the space available to the current user (quotas apply), as Explorer shows it.
/// A volume that disappears or is not ready publishes Unavailable values.
/// </summary>
public sealed class VolumeSpaceCollector : IMetricCollector
{
    private const string Source = "DriveInfo (GetDiskFreeSpaceEx)";
    private readonly string[] _roots;

    public VolumeSpaceCollector()
    {
        _roots = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && SafeIsReady(d))
            .Select(d => d.RootDirectory.FullName)
            .ToArray();
        Metrics = _roots.SelectMany(r =>
        {
            string l = Letter(r);
            return new[]
            {
                new MetricDescriptor(FreeId(l), $"{l}: free", MetricUnit.Bytes, Source, "Available to the current user"),
                new MetricDescriptor(TotalId(l), $"{l}: size", MetricUnit.Bytes, Source),
                new MetricDescriptor(UsedPercentId(l), $"{l}: used", MetricUnit.Percent, Source, "(size - free) / size"),
            };
        }).ToArray();
    }

    public string Name => "Volumes";

    public IReadOnlyList<MetricDescriptor> Metrics { get; }

    public TimeSpan DefaultInterval => TimeSpan.FromSeconds(5);

    public IReadOnlyList<string> Letters => _roots.Select(Letter).ToArray();

    public static string FreeId(string letter) => $"disk.{letter}.free";

    public static string TotalId(string letter) => $"disk.{letter}.total";

    public static string UsedPercentId(string letter) => $"disk.{letter}.used";

    public CollectorAvailability CheckAvailability() =>
        _roots.Length > 0
            ? CollectorAvailability.Available($"{_roots.Length} fixed volumes")
            : CollectorAvailability.NotAvailable("No ready fixed volume");

    public CollectorResult Collect(CancellationToken cancellationToken)
    {
        var values = new List<MetricValue>(_roots.Length * 3);
        var errors = new List<CollectorError>();
        foreach (string root in _roots)
        {
            string l = Letter(root);
            try
            {
                var d = new DriveInfo(root);
                long free = d.AvailableFreeSpace, total = d.TotalSize;
                values.Add(MetricValue.Measured(FreeId(l), free));
                values.Add(MetricValue.Measured(TotalId(l), total));
                values.Add(total > 0 ? MetricValue.Derived(UsedPercentId(l), 100.0 * (total - free) / total) : MetricValue.Unavailable(UsedPercentId(l)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add(new CollectorError(Name, $"{root}: {ex.Message}"));
                values.Add(MetricValue.Unavailable(FreeId(l)));
                values.Add(MetricValue.Unavailable(TotalId(l)));
                values.Add(MetricValue.Unavailable(UsedPercentId(l)));
            }
        }

        return new CollectorResult(values, errors);
    }

    private static string Letter(string root) => root[..1].ToUpperInvariant();

    private static bool SafeIsReady(DriveInfo d)
    {
        try
        {
            return d.IsReady;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
