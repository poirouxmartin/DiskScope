using System.Text.Json;
using DiskScope.App.ViewModels;
using DiskScope.Core;
using DiskScope.Diagnostics;
using DiskScope.History;
using DiskScope.Monitoring;
using DiskScope.Monitoring.CpuRam;
using DiskScope.Processes;
using DiskScope.Reports;
using DiskScope.Tests.Monitoring;

namespace DiskScope.Tests.Reports;

public sealed class ReportTests
{
    private static readonly DateTime T0 = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly MetricDescriptor Cpu = new("cpu.total", "CPU usage", MetricUnit.Percent, "GetSystemTimes (delta)");

    private static MetricPoint P(int s, double v) => new(T0.AddSeconds(s), v, v, v, 1);

    private static DiagnosticReport Report(string alertTitle = "Sustained high CPU") => new(
        "0.1.0", T0.AddMinutes(1), T0, T0.AddMinutes(1), "Windows test", 8,
        [new ReportCollector("CPU", true, "ok", 60, 0, 0, 1.2, 3.4)],
        [new MetricDescriptor("gpu.0.engine.compute", "GPU 0 compute", MetricUnit.Percent, "PDH")],
        [new ReportSeries(Cpu, [P(0, 10), P(1, 20), P(2, 30), P(30, 40), P(31, 50)])],
        [new AlertEvent(Guid.Empty, "cpu.high", alertTitle, FindingLevel.Alert, T0, null, "explained", [], false)],
        [],
        DefaultRules.Create(),
        ["History is kept in memory."]);

    [Fact]
    public void Html_EncodesText_AndHasNoScript()
    {
        var w = new StringWriter();

        HtmlReportWriter.Write(w, Report("<script>alert(1)</script>"));
        string html = w.ToString();

        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.DoesNotContain("<script", html);
        Assert.Contains("GPU 0 compute", html); // unavailable metrics listed
        Assert.Contains("History is kept in memory.", html);
    }

    [Fact]
    public void Html_Chart_BreaksTheLineAtGaps()
    {
        string svg = HtmlReportWriter.Svg(new ReportSeries(Cpu, [P(0, 10), P(1, 20), P(2, 30), P(30, 40), P(31, 50)]), T0, T0.AddMinutes(1));

        Assert.Equal(2, svg.Split("<polyline").Length - 1);
    }

    [Fact]
    public void Json_UsesStringEnums_AndCarriesNoMachineOrUserName()
    {
        string json = JsonReportWriter.Serialize(Report());

        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("Alert", doc.RootElement.GetProperty("alerts")[0].GetProperty("level").GetString());
        Assert.Equal("Percent", doc.RootElement.GetProperty("series")[0].GetProperty("metric").GetProperty("unit").GetString());
        Assert.DoesNotContain(Environment.MachineName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HistoryCsv_OneRowPerPoint_InvariantNumbers()
    {
        var w = new StringWriter();

        int rows = MetricHistoryCsv.Write(w, [new ReportSeries(Cpu, [new MetricPoint(T0, 12.5, 10, 15, 5)])]);

        Assert.Equal(1, rows);
        Assert.EndsWith("cpu.total,CPU usage,Percent,GetSystemTimes (delta),2026-10-21T12:00:00Z,12.5,10,15,5\r\n", w.ToString());
    }

    [Fact]
    public void ProcessCsv_UnknownValuesAreEmpty()
    {
        var row = new ProcessRowViewModel(new ProcessSnapshot { Key = new ProcessKey(4, 0), ParentProcessId = 0, Name = "System", ThreadCount = 9, IsAccessible = false });
        var w = new StringWriter();

        ProcessesViewModel.WriteCsv(w, [row]);

        Assert.EndsWith("System,4,0,,,,,,9,,,,,\r\n", w.ToString());
    }

    [Fact]
    public void BuildReport_ListsUnavailableMetrics_AndCollectorCost()
    {
        var collectors = new List<IMetricCollector>
        {
            new MonitoringSchedulerTests.FakeCollector("CPU", CpuCollector.Total, () => 50),
            new MonitoringSchedulerTests.FakeCollector("Broken", "x.broken", () => throw new InvalidOperationException("no")),
        };
        var vm = new MonitoringViewModel(collectors, new InMemoryMetricStore(), new AlertEngine(DefaultRules.Create()), new MonitoringClock());
        vm.Scheduler.CollectOnce();

        DiagnosticReport report = vm.BuildReport(TimeSpan.FromMinutes(10));

        Assert.Contains(report.UnavailableMetrics, m => m.Id == "x.broken");
        Assert.DoesNotContain(report.UnavailableMetrics, m => m.Id == CpuCollector.Total);
        Assert.Single(report.Series.Single(s => s.Metric.Id == CpuCollector.Total).Points);
        Assert.Equal(1, report.Collectors.Single(c => c.Name == "Broken").Failures);
        Assert.NotEmpty(report.Limits);
    }
}
