using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using DiskScope.Scanner;

namespace DiskScope.Benchmarks;

/// <summary>One measured scan run. Times in milliseconds, sizes in bytes.</summary>
internal sealed record RunSample(
    bool IsWarmup,
    ScanOutcome Outcome,
    double ElapsedMs,
    double FirstProgressMs,
    long Nodes,
    long Files,
    long LogicalBytes,
    long DiskAllocatedBytes,
    int Errors,
    long ManagedAllocatedBytes,
    double CpuMs,
    long Gen0,
    long Gen1,
    long Gen2,
    long PeakWorkingSetBytes)
{
    public double NodesPerSecond => ElapsedMs > 0 ? Nodes / (ElapsedMs / 1000.0) : 0.0;
}

/// <summary>Records the time of the first progress callback, relative to a stopwatch started before the scan.</summary>
internal sealed class FirstProgressObserver(Stopwatch clock) : IScanObserver
{
    private long _firstTicks = -1;

    public double FirstProgressMs
    {
        get
        {
            long ticks = Interlocked.Read(ref _firstTicks);
            return ticks < 0 ? double.NaN : TimeSpan.FromTicks(ticks).TotalMilliseconds;
        }
    }

    public void OnProgress(in ScanProgress progress)
    {
        Interlocked.CompareExchange(ref _firstTicks, clock.Elapsed.Ticks, -1);
    }
}

internal static class Stats
{
    public static double Median(IReadOnlyList<double> values)
    {
        double[] sorted = values.Order().ToArray();
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    public static string Fixed(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    public static string Mb(long bytes) => Fixed(bytes / 1048576.0, "F1");

    public static string FormatRun(int number, RunSample sample)
    {
        string tag = sample.IsWarmup ? " [warmup]" : string.Empty;
        string timing = $"elapsed {Fixed(sample.ElapsedMs, "F1")} ms, first progress {Fixed(sample.FirstProgressMs, "F1")} ms";
        string throughput = $"nodes {sample.Nodes} ({Fixed(sample.NodesPerSecond, "F0")} nodes/s)";
        string sizes = $"files {sample.Files}, logical {Mb(sample.LogicalBytes)} MB, disk-allocated {Mb(sample.DiskAllocatedBytes)} MB";
        string runtime = $"managed-alloc {Mb(sample.ManagedAllocatedBytes)} MB, cpu {Fixed(sample.CpuMs, "F1")} ms, gc {sample.Gen0}/{sample.Gen1}/{sample.Gen2}, peak-ws {Mb(sample.PeakWorkingSetBytes)} MB";
        return $"run {number}{tag}: outcome={sample.Outcome} errors={sample.Errors}; {timing}; {throughput}; {sizes}; {runtime}";
    }

    public static void PrintHeader(string command, string target)
    {
        Console.WriteLine($"DiskScope.Benchmarks {command}");
        Console.WriteLine($"Machine:      {Environment.MachineName}");
        Console.WriteLine($"OS:           {Environment.OSVersion.VersionString}");
        Console.WriteLine($"Processors:   {Environment.ProcessorCount}");
        Console.WriteLine($".NET:         {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"Server GC:    {GCSettings.IsServerGC}");
        Console.WriteLine($"Date (UTC):   {DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)}");
        Console.WriteLine($"Target:       {target}");
    }
}
