using DiskScope.Core;

namespace DiskScope.Reports;

/// <summary>History of one metric over the report period.</summary>
public sealed record ReportSeries(MetricDescriptor Metric, IReadOnlyList<MetricPoint> Points);

/// <summary>State and cost of one collector during the session.</summary>
public sealed record ReportCollector(string Name, bool Available, string Reason, long Collections, long Failures, long MissedTicks, double MeanMilliseconds, double MaxMilliseconds);

/// <summary>
/// Everything a report shows, gathered once so the CSV, JSON and HTML writers render the same facts.
/// Privacy (spec section 12): no machine name, user name or command line is part of the model; process data, if any,
/// is limited to image names.
/// </summary>
public sealed record DiagnosticReport(
    string AppVersion,
    DateTime GeneratedUtc,
    DateTime FromUtc,
    DateTime ToUtc,
    string OsDescription,
    int LogicalProcessors,
    IReadOnlyList<ReportCollector> Collectors,
    IReadOnlyList<MetricDescriptor> UnavailableMetrics,
    IReadOnlyList<ReportSeries> Series,
    IReadOnlyList<AlertEvent> Alerts,
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<ThresholdRule> Rules,
    IReadOnlyList<string> Limits);
