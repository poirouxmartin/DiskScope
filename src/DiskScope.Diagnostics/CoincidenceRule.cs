using System.Globalization;
using DiskScope.Core;

namespace DiskScope.Diagnostics;

/// <summary>
/// "During each episode of alert rule A, metric M averaged at least T": a Hypothesis, never a cause.
/// Formula: over [episode start, episode end or now], sample-weighted average of M's stored points; the history must
/// cover at least <see cref="MinCoverage"/> of the episode, otherwise nothing is concluded.
/// </summary>
public sealed class CoincidenceRule : IDiagnosticRule
{
    public CoincidenceRule(
        string id,
        string triggerRuleId,
        string metricId,
        MetricUnit unit,
        double minAverage,
        Confidence confidence,
        string title,
        string mechanism,
        IReadOnlyList<string> checks,
        string knownFalsePositives)
    {
        Id = id;
        TriggerRuleId = triggerRuleId;
        MetricId = metricId;
        Unit = unit;
        MinAverage = minAverage;
        Confidence = confidence;
        Title = title;
        Mechanism = mechanism;
        Checks = checks;
        KnownFalsePositives = knownFalsePositives;
    }

    public string Id { get; }

    public string TriggerRuleId { get; }

    public string MetricId { get; }

    public MetricUnit Unit { get; }

    public double MinAverage { get; }

    public Confidence Confidence { get; }

    public string Title { get; }

    public string Mechanism { get; }

    public IReadOnlyList<string> Checks { get; }

    public string KnownFalsePositives { get; }

    /// <summary>Share of the episode the history must cover (by expected 1 s samples) before anything is concluded.</summary>
    public double MinCoverage { get; init; } = 0.5;

    public string Formula => string.Create(CultureInfo.InvariantCulture,
        $"During an episode of '{TriggerRuleId}', average of {MetricId} >= {MetricFormatter.Format(MinAverage, Unit)} with at least {MinCoverage:P0} history coverage");

    public IReadOnlyList<Finding> Evaluate(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var findings = new List<Finding>();
        foreach (AlertEvent alert in context.Alerts.Where(a => a.RuleId == TriggerRuleId))
        {
            DateTime from = alert.StartedUtc;
            DateTime to = alert.EndedUtc ?? context.NowUtc;
            if (to <= from)
            {
                continue;
            }

            IReadOnlyList<MetricPoint> points = context.History.Query(MetricId, from, to);
            int samples = points.Sum(p => p.Samples);
            double expected = Math.Max(1, (to - from).TotalSeconds);
            if (samples == 0 || samples / expected < MinCoverage)
            {
                continue; // Not enough data: no conclusion rather than a weak one.
            }

            double average = points.Sum(p => p.Average * p.Samples) / samples;
            if (average < MinAverage)
            {
                continue;
            }

            double max = points.Max(p => p.Max);
            findings.Add(new Finding(
                Id,
                FindingLevel.Hypothesis,
                Confidence,
                Title,
                string.Create(CultureInfo.InvariantCulture,
                    $"While '{alert.Title}' was active, {MetricId} averaged {MetricFormatter.Format(average, Unit)} (max {MetricFormatter.Format(max, Unit)}). {Mechanism}"),
                [
                    new Evidence(MetricId, "Average during the episode", average, Unit, to),
                    new Evidence(MetricId, "Maximum during the episode", max, Unit, to),
                ],
                Checks,
                "A coincidence in time is not proof of causality: either may cause the other, or a third activity may cause both. Known false positives: " + KnownFalsePositives,
                from,
                to));
        }

        return findings;
    }
}

/// <summary>Runs every diagnostic rule; a rule that throws is reported as a finding-free error, not fatal.</summary>
public sealed class DiagnosticEngine(IReadOnlyList<IDiagnosticRule> rules)
{
    public IReadOnlyList<IDiagnosticRule> Rules { get; } = rules;

    public (IReadOnlyList<Finding> Findings, IReadOnlyList<string> Errors) Evaluate(DiagnosticContext context)
    {
        var findings = new List<Finding>();
        var errors = new List<string>();
        foreach (IDiagnosticRule rule in Rules)
        {
            try
            {
                findings.AddRange(rule.Evaluate(context));
            }
            catch (Exception ex)
            {
                errors.Add($"{rule.Id}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return (findings, errors);
    }

    public static DiagnosticEngine CreateDefault() => new(
    [
        new CoincidenceRule(
            "cpu.high.with.disk", DefaultRules.HighCpu, MetricIds.DiskActive, MetricUnit.Percent, 60, Confidence.Low,
            "High CPU coincided with heavy disk activity",
            "Both resources were busy at the same time.",
            ["Processes tab: sort by CPU, then by I/O read and I/O write, and look for the same process at the top.",
             "Common shared causes: search indexing, antivirus scans, Windows Update, backups."],
            "builds and installers read many files while compiling or unpacking."),
        new CoincidenceRule(
            "mem.low.with.disk", DefaultRules.LowAvailableMemory, MetricIds.DiskActive, MetricUnit.Percent, 50, Confidence.Medium,
            "Low memory coincided with heavy disk activity: paging is likely",
            "When available memory is short, Windows writes pages to the page file and reads them back, which keeps the disk busy.",
            ["Compare commit charge with physical memory in the CPU & memory tab.",
             "Processes tab: sort by private bytes to find the largest consumers."],
            "a large file copy during a memory-hungry task produces the same picture without any paging."),
    ]);
}
