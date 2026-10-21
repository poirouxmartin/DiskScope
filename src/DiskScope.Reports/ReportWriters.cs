using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiskScope.Core;

namespace DiskScope.Reports;

/// <summary>Metric history as CSV: one row per stored point, unknown values never written as 0 (gaps are absent rows).</summary>
public static class MetricHistoryCsv
{
    public static int Write(TextWriter writer, IEnumerable<ReportSeries> series) =>
        CsvTable.Write(
            writer,
            ["metric_id", "name", "unit", "source", "timestamp_utc", "average", "min", "max", "samples"],
            series.SelectMany(s => s.Points.Select(p => (IReadOnlyList<string?>)
            [
                s.Metric.Id, s.Metric.DisplayName, s.Metric.Unit.ToString(), s.Metric.Source,
                p.TimestampUtc.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture),
                Num(p.Average), Num(p.Min), Num(p.Max), p.Samples.ToString(CultureInfo.InvariantCulture),
            ])));

    private static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>Structured JSON for automation: same facts as the HTML report, camelCase, enums as strings, UTC ISO 8601.</summary>
public static class JsonReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static string Serialize(DiagnosticReport report) => JsonSerializer.Serialize(report, Options);

    public static void Write(TextWriter writer, DiagnosticReport report) => writer.Write(Serialize(report));
}

/// <summary>
/// Self-contained local HTML report (no script, no external resource): summary, period, one inline SVG chart per
/// series, alerts, hypotheses, rules, collectors and unavailable metrics, and the collection limits.
/// Every text is HTML-encoded.
/// </summary>
public static class HtmlReportWriter
{
    public static void Write(TextWriter w, DiagnosticReport r)
    {
        var c = CultureInfo.InvariantCulture;
        w.Write("<!DOCTYPE html>\n<html lang=\"en\"><head><meta charset=\"utf-8\"><title>DiskScope report</title><style>");
        w.Write("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#1f2933}h1{font-size:22px}h2{font-size:17px;margin-top:28px}");
        w.Write("table{border-collapse:collapse;margin:6px 0}td,th{border:1px solid #d5dae0;padding:3px 8px;text-align:left;font-size:13px;vertical-align:top}");
        w.Write(".muted{color:#6b7580}.chart{border:1px solid #d5dae0;background:#f4f6f8}</style></head><body>\n");
        w.Write($"<h1>DiskScope report</h1>\n<p>Generated {E(Time(r.GeneratedUtc))} UTC by DiskScope {E(r.AppVersion)} on {E(r.OsDescription)}, {r.LogicalProcessors.ToString(c)} logical processors.<br>");
        w.Write($"Period: {E(Time(r.FromUtc))} to {E(Time(r.ToUtc))} UTC.</p>\n");

        w.Write("<h2>Summary</h2><table><tr><th>Metric</th><th>Average</th><th>Min</th><th>Max</th><th>Points</th><th>Source</th></tr>\n");
        foreach (ReportSeries s in r.Series)
        {
            if (s.Points.Count == 0)
            {
                w.Write($"<tr><td>{E(s.Metric.DisplayName)}</td><td colspan=\"4\">no data in the period</td><td>{E(s.Metric.Source)}</td></tr>\n");
                continue;
            }

            int n = s.Points.Sum(p => p.Samples);
            double avg = s.Points.Sum(p => p.Average * p.Samples) / n;
            w.Write($"<tr><td>{E(s.Metric.DisplayName)}</td><td>{E(MetricFormatter.Format(avg, s.Metric.Unit))}</td><td>{E(MetricFormatter.Format(s.Points.Min(p => p.Min), s.Metric.Unit))}</td>");
            w.Write($"<td>{E(MetricFormatter.Format(s.Points.Max(p => p.Max), s.Metric.Unit))}</td><td>{s.Points.Count.ToString(c)}</td><td>{E(s.Metric.Source)}</td></tr>\n");
        }

        w.Write("</table>\n<h2>Charts</h2>\n");
        foreach (ReportSeries s in r.Series.Where(s => s.Points.Count > 1))
        {
            w.Write($"<p>{E(s.Metric.DisplayName)} <span class=\"muted\">({E(s.Metric.Unit.ToString())}; gaps are periods without data)</span></p>\n");
            w.Write(Svg(s, r.FromUtc, r.ToUtc));
        }

        w.Write("<h2>Alerts (measured facts)</h2>\n");
        Table(w, ["Started (UTC)", "Ended (UTC)", "Level", "Alert", "Explanation", "Acknowledged"],
            r.Alerts.Select(a => new[] { Time(a.StartedUtc), a.EndedUtc is { } e ? Time(e) : "ongoing", a.Level.ToString(), a.Title, a.Explanation, a.Acknowledged ? "yes" : "no" }),
            "No alert in the period.");

        w.Write("<h2>Hypotheses (correlations, not causes)</h2>\n");
        Table(w, ["Period (UTC)", "Confidence", "Hypothesis", "Observed", "To check", "Limits"],
            r.Findings.Select(f => new[] { $"{Time(f.FromUtc)} - {Time(f.ToUtc)}", f.Confidence.ToString(), f.Title, f.Explanation, string.Join(" ", f.Checks), f.Limits }),
            "No hypothesis in the period.");

        w.Write("<h2>Rules</h2>\n");
        Table(w, ["Rule", "Condition", "Enabled", "Rationale", "Known false positives"],
            r.Rules.Select(x => new[]
            {
                x.Name,
                $"{x.MetricId} {(x.Comparison == ThresholdComparison.Above ? ">" : "<")} {MetricFormatter.Format(x.Threshold, x.Unit)} for {x.MinDuration.TotalSeconds.ToString("0", c)} s",
                x.Enabled ? "yes" : "no", x.Rationale, x.KnownFalsePositives,
            }),
            "No rule.");

        w.Write("<h2>Collectors</h2>\n");
        Table(w, ["Collector", "Available", "Reason", "Collections", "Failures", "Missed ticks", "Mean ms", "Max ms"],
            r.Collectors.Select(x => new[]
            {
                x.Name, x.Available ? "yes" : "no", x.Reason, x.Collections.ToString(c), x.Failures.ToString(c), x.MissedTicks.ToString(c),
                x.MeanMilliseconds.ToString("F2", c), x.MaxMilliseconds.ToString("F2", c),
            }),
            "No collector.");

        w.Write("<h2>Unavailable metrics</h2>\n");
        Table(w, ["Metric", "Source"], r.UnavailableMetrics.Select(m => new[] { m.DisplayName, m.Source }), "Every metric had a value at the end of the period.");

        w.Write("<h2>Limits of this report</h2><ul>\n");
        foreach (string limit in r.Limits)
        {
            w.Write($"<li>{E(limit)}</li>\n");
        }

        w.Write("</ul></body></html>\n");
    }

    internal static string Svg(ReportSeries s, DateTime from, DateTime to)
    {
        const double W = 720, H = 120;
        var c = CultureInfo.InvariantCulture;
        double max = s.Metric.Unit == MetricUnit.Percent ? 100 : Math.Max(1e-9, s.Points.Max(p => p.Max));
        double span = Math.Max(1, (to - from).Ticks);
        long median = Median(s.Points);
        var sb = new StringBuilder();
        sb.Append(c, $"<svg class=\"chart\" width=\"{W}\" height=\"{H}\" viewBox=\"0 0 {W} {H}\" xmlns=\"http://www.w3.org/2000/svg\">");
        var run = new List<string>();
        MetricPoint? previous = null;
        foreach (MetricPoint p in s.Points)
        {
            if (previous is { } prev && median > 0 && (p.TimestampUtc - prev.TimestampUtc).Ticks > median * 5 / 2)
            {
                Flush(sb, run);
            }

            double x = (p.TimestampUtc - from).Ticks / span * W;
            double y = H - 2 - (Math.Clamp(p.Average / max, 0, 1) * (H - 4));
            run.Add(string.Create(c, $"{x:F1},{y:F1}"));
            previous = p;
        }

        Flush(sb, run);
        sb.Append("</svg>\n");
        return sb.ToString();
    }

    private static void Flush(StringBuilder sb, List<string> run)
    {
        if (run.Count > 1)
        {
            sb.Append("<polyline fill=\"none\" stroke=\"steelblue\" stroke-width=\"1.5\" points=\"").Append(string.Join(' ', run)).Append("\"/>");
        }

        run.Clear();
    }

    private static long Median(IReadOnlyList<MetricPoint> points)
    {
        if (points.Count < 2)
        {
            return 0;
        }

        long[] gaps = new long[points.Count - 1];
        for (int i = 1; i < points.Count; i++)
        {
            gaps[i - 1] = (points[i].TimestampUtc - points[i - 1].TimestampUtc).Ticks;
        }

        Array.Sort(gaps);
        return gaps[gaps.Length / 2];
    }

    private static void Table(TextWriter w, string[] header, IEnumerable<string[]> rows, string empty)
    {
        string[][] all = rows.ToArray();
        if (all.Length == 0)
        {
            w.Write($"<p class=\"muted\">{E(empty)}</p>\n");
            return;
        }

        w.Write("<table><tr>");
        foreach (string h in header)
        {
            w.Write($"<th>{E(h)}</th>");
        }

        w.Write("</tr>\n");
        foreach (string[] row in all)
        {
            w.Write("<tr>");
            foreach (string cell in row)
            {
                w.Write($"<td>{E(cell)}</td>");
            }

            w.Write("</tr>\n");
        }

        w.Write("</table>\n");
    }

    private static string Time(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
}
