using System.Diagnostics;
using System.Globalization;
using DiskScope.Core;
using DiskScope.Diagnostics;
using DiskScope.History;
using DiskScope.Monitoring;
using DiskScope.Monitoring.CpuRam;
using DiskScope.Monitoring.Gpu;
using DiskScope.Processes;

namespace DiskScope.Benchmarks;

/// <summary>
/// Cost of the monitoring pipeline without UI (spec section 14): every collector at its default interval, history and
/// alerts attached, plus the process refresh every 2 s, for a fixed duration. Reports per-collector latency, failures,
/// missed ticks, process refresh latency, and DiskScope's own CPU and memory over the run.
/// </summary>
internal static class MonitorBench
{
    public static int Run(int seconds)
    {
        using var gpu = new GpuCollector();
        using var disk = new DiskActivityCollector();
        var store = new InMemoryMetricStore();
        var alerts = new AlertEngine(DefaultRules.Create());
        IMetricCollector[] collectors = [new CpuCollector(), new MemoryCollector(), new VolumeSpaceCollector(), disk, gpu, new SelfCollector()];
        var scheduler = new MonitoringScheduler(collectors, store);
        long batches = 0;
        scheduler.BatchPublished += b =>
        {
            Interlocked.Increment(ref batches);
            alerts.Evaluate(b);
        };

        Process self = Process.GetCurrentProcess();
        TimeSpan cpuStart = self.TotalProcessorTime;
        long allocStart = GC.GetTotalAllocatedBytes(precise: true);
        var sw = Stopwatch.StartNew();
        var processDurations = new List<double>();
        var tracker = new ProcessCpuTracker(Environment.ProcessorCount);

        scheduler.Start();
        while (sw.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            long t0 = Stopwatch.GetTimestamp();
            ProcessReadResult read = ProcessReader.ReadAll();
            tracker.Update(read.Processes, Stopwatch.GetTimestamp());
            ProcessTree.Build(read.Processes);
            processDurations.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
            Thread.Sleep(2000);
        }

        scheduler.StopAsync().GetAwaiter().GetResult();
        sw.Stop();
        self.Refresh();
        double cpuPercent = 100.0 * (self.TotalProcessorTime - cpuStart).TotalMilliseconds / (sw.Elapsed.TotalMilliseconds * Environment.ProcessorCount);
        double allocMb = (GC.GetTotalAllocatedBytes(precise: true) - allocStart) / (1024.0 * 1024);

        var c = CultureInfo.InvariantCulture;
        Console.WriteLine(string.Format(c, "duration          {0:F0} s, {1} batches, {2} logical processors", sw.Elapsed.TotalSeconds, batches, Environment.ProcessorCount));
        // "mean ms" is wall-clock (waiting for a processor included); "cpu ms" is the collecting thread's own CPU time.
        Console.WriteLine("collector         avail  count  fail  missed  mean ms  max ms  cpu ms");
        foreach (CollectorStats s in scheduler.GetStats())
        {
            double mean = s.Collections > 0 ? s.TotalDuration.TotalMilliseconds / s.Collections : double.NaN;
            Console.WriteLine(string.Format(c, "{0,-17} {1,-6} {2,5} {3,5} {4,7} {5,8:F2} {6,7:F2} {7,7:F2}",
                s.Collector, s.Availability.IsAvailable, s.Collections, s.Failures, s.SkippedTicks, mean, s.MaxDuration.TotalMilliseconds, s.MeanCpuMilliseconds));
        }

        processDurations.Sort();
        Console.WriteLine(string.Format(c, "process refresh   {0} runs, median {1:F1} ms, max {2:F1} ms",
            processDurations.Count, processDurations[processDurations.Count / 2], processDurations[^1]));
        Console.WriteLine(string.Format(c, "self CPU          {0:F2} % of the machine (average over the run)", cpuPercent));
        Console.WriteLine(string.Format(c, "self memory       peak working set {0:F0} MB, allocated {1:F1} MB ({2:F2} MB/s)",
            self.PeakWorkingSet64 / (1024.0 * 1024), allocMb, allocMb / sw.Elapsed.TotalSeconds));
        Console.WriteLine(string.Format(c, "alerts raised     {0}", alerts.Events.Count));
        return 0;
    }
}
