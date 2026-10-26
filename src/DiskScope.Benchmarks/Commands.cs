using System.Diagnostics;
using System.Globalization;
using DiskScope.Storage;
using DiskScope.Scanner;
using DiskScope.Scanner.Standard;

namespace DiskScope.Benchmarks;

internal static class Commands
{
    private const int DirectoryFanout = 10;
    private const long GenerateCapBytes = 8L * 1024 * 1024 * 1024;
    private const long GenerateReserveBytes = 2L * 1024 * 1024 * 1024;
    private const double GiB = 1073741824.0;

    private sealed class TraceObserver : IScanObserver
    {
        public void OnProgress(in ScanProgress p) =>
            // Invariant like the rest of the CLI output, so log parsers do not depend on the machine's culture.
            Console.Error.WriteLine(FormattableString.Invariant($"[trace] {p.Elapsed.TotalSeconds,7:F1} s nodes={p.Nodes} errors={p.Errors} dir={p.CurrentDirectory}"));
    }

    internal sealed record Baseline(long Files, long LogicalBytes, long SkippedLinks, long LengthFailures, long VanishedDirectories);

    public static int Scan(string path, int runs, int warmup)
    {
        Stats.PrintHeader("scan", path);
        Console.WriteLine("Cache state:  warm (repeated runs); cold-cache runs need a reboot or standby-list flush, see docs/BENCHMARK-PROTOCOL.md");
        Console.WriteLine();

        using Process process = Process.GetCurrentProcess();
        var measured = new List<RunSample>();
        for (int i = 0; i < warmup + runs; i++)
        {
            bool isWarmup = i < warmup;
            RunSample sample = RunOnce(path, isWarmup, process);
            Console.WriteLine(Stats.FormatRun(i + 1, sample));
            if (!isWarmup)
            {
                measured.Add(sample);
            }
        }

        double[] elapsed = measured.Select(s => s.ElapsedMs).ToArray();
        double[] throughput = measured.Select(s => s.NodesPerSecond).ToArray();
        double[] firstProgress = measured.Select(s => s.FirstProgressMs).ToArray();

        Console.WriteLine();
        Console.WriteLine($"Summary over {measured.Count} measured run(s):");
        Console.WriteLine($"  elapsed ms:        median {Stats.Fixed(Stats.Median(elapsed), "F1")}, min {Stats.Fixed(elapsed.Min(), "F1")}");
        Console.WriteLine($"  nodes/s:           median {Stats.Fixed(Stats.Median(throughput), "F0")}, min {Stats.Fixed(throughput.Min(), "F0")}");
        Console.WriteLine($"  first progress ms: median {Stats.Fixed(Stats.Median(firstProgress), "F1")}");

        return measured.All(s => s.Outcome == ScanOutcome.Completed) ? 0 : 1;
    }

    private static RunSample RunOnce(string path, bool isWarmup, Process process)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long managedBefore = GC.GetTotalAllocatedBytes(precise: true);
        process.Refresh();
        TimeSpan cpuBefore = process.TotalProcessorTime;
        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);

        var clock = Stopwatch.StartNew();
        var observer = new FirstProgressObserver(clock);
        var engine = new StandardScanEngine();
        ScanResult result = engine.ScanAsync(new ScanRequest(path), observer, CancellationToken.None).GetAwaiter().GetResult();

        process.Refresh();
        TimeSpan cpuAfter = process.TotalProcessorTime;
        long managedAfter = GC.GetTotalAllocatedBytes(precise: true);
        NodeIndex index = result.Index;

        return new RunSample(
            isWarmup,
            result.Outcome,
            result.Elapsed.TotalMilliseconds,
            observer.FirstProgressMs,
            index.Count,
            index.GetRecursiveFileCount(NodeIndex.RootId),
            index.GetTotalLogicalSize(NodeIndex.RootId),
            index.GetTotalAllocatedSize(NodeIndex.RootId),
            index.Errors.Count,
            managedAfter - managedBefore,
            (cpuAfter - cpuBefore).TotalMilliseconds,
            GC.CollectionCount(0) - gen0Before,
            GC.CollectionCount(1) - gen1Before,
            GC.CollectionCount(2) - gen2Before,
            process.PeakWorkingSet64);
    }

    public static int Compare(string path)
    {
        // Progress goes to stderr so a slow directory can be located without polluting the result on stdout.
        var request = new ScanRequest(path, new ScanOptions { ProgressInterval = TimeSpan.FromSeconds(2) });
        ScanResult result = new StandardScanEngine()
            .ScanAsync(request, new TraceObserver(), CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Console.Error.WriteLine(FormattableString.Invariant($"[trace] engine done in {result.Elapsed.TotalSeconds:F1} s, starting baseline walk"));
        NodeIndex index = result.Index;
        long engineFiles = index.GetRecursiveFileCount(NodeIndex.RootId);
        long engineBytes = index.GetTotalLogicalSize(NodeIndex.RootId);
        int engineErrors = index.Errors.Count;

        Baseline baseline = WalkBaseline(path);
        bool equal = engineFiles == baseline.Files && engineBytes == baseline.LogicalBytes;

        Console.WriteLine($"Path:     {path}");
        Console.WriteLine($"Engine:   {result.EngineName} outcome={result.Outcome} files={engineFiles} logical={engineBytes} bytes errors={engineErrors}");
        Console.WriteLine($"Baseline: files={baseline.Files} logical={baseline.LogicalBytes} bytes links-skipped={baseline.SkippedLinks} length-failures={baseline.LengthFailures} vanished-dirs={baseline.VanishedDirectories}");
        Console.WriteLine($"Delta:    files {Signed(engineFiles - baseline.Files)} ({Percent(engineFiles, baseline.Files)}), logical {Signed(engineBytes - baseline.LogicalBytes)} bytes ({Percent(engineBytes, baseline.LogicalBytes)})");
        Console.WriteLine("Baseline caveats: skips every entry carrying ReparsePoint (junctions, symlinks, and cloud placeholders that carry the attribute); enumeration errors are hidden by IgnoreInaccessible and not counted; on a live tree (profile, volume) the two passes run minutes apart, so a small mismatch is expected.");
        Console.WriteLine(equal ? "RESULT: equal" : "RESULT: MISMATCH");
        return equal ? 0 : 1;
    }

    /// <summary>One scan, then every unreadable directory with its error, and optionally the details of one node.</summary>
    public static int Inspect(string path, string? nodePath)
    {
        ScanResult result = new StandardScanEngine()
            .ScanAsync(new ScanRequest(path), null, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Console.WriteLine($"Path:    {path}");
        Console.WriteLine($"Outcome: {result.Outcome} in {result.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s");
        List<string> lines = InspectLines(result.Index, nodePath, out bool found);
        foreach (string line in lines)
        {
            Console.WriteLine(line);
        }

        return result.Outcome == ScanOutcome.Completed && found ? 0 : 1;
    }

    /// <summary>
    /// Error list (kind, Win32 code, path), then the node at <paramref name="nodePath"/> if given.
    /// <paramref name="found"/> is false when the node is not in the index. Unknown values print as n/a, never 0.
    /// </summary>
    internal static List<string> InspectLines(NodeIndex index, string? nodePath, out bool found)
    {
        var lines = new List<string> { $"Errors:  {index.Errors.Count}" };
        foreach (ScanError error in index.Errors)
        {
            lines.Add($"  {error.Kind} {error.Win32Error} {index.GetFullPath(error.NodeId)}");
        }

        found = true;
        if (nodePath is null)
        {
            return lines;
        }

        int id = index.FindByPath(nodePath);
        if (id < 0)
        {
            found = false;
            lines.Add($"Node not found: {nodePath}");
            return lines;
        }

        lines.Add($"Node:    {index.GetFullPath(id)} ({index.GetKind(id)})");
        lines.Add($"  status:             {index.GetStatus(id)}");
        lines.Add($"  logical bytes:      {Known(index.GetKnownTotalSize(id, SizeKind.Logical))}");
        lines.Add($"  allocated bytes:    {Known(index.GetKnownTotalSize(id, SizeKind.Allocated))}");
        lines.Add($"  files:              {index.GetRecursiveFileCount(id).ToString(CultureInfo.InvariantCulture)}");
        lines.Add($"  directories:        {index.GetRecursiveDirectoryCount(id).ToString(CultureInfo.InvariantCulture)}");
        lines.Add($"  last write:         {Date(index.GetLastWriteUtc(id))}");
        lines.Add($"  subtree last write: {Date(index.GetSubtreeLastWriteUtc(id))}");
        if (index.GetStatus(id) != NodeStatus.None)
        {
            lines.Add("  incomplete: sizes, counts and the subtree date are lower bounds.");
        }

        return lines;

        static string Known(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "n/a";

        static string Date(DateTime utc) =>
            utc.Ticks == 0 ? "n/a" : utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    /// <param name="beforeEnumerate">Test seam: called with each directory path just before it is enumerated.</param>
    internal static Baseline WalkBaseline(string path, Action<string>? beforeEnumerate = null)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false,
        };

        long files = 0;
        long bytes = 0;
        long links = 0;
        long lengthFailures = 0;
        long vanished = 0;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(path));

        while (pending.Count > 0)
        {
            DirectoryInfo directory = pending.Pop();
            beforeEnumerate?.Invoke(directory.FullName);
            try
            {
                foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos("*", options))
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        links++;
                        continue;
                    }

                    if (entry is DirectoryInfo subdirectory)
                    {
                        pending.Push(subdirectory);
                    }
                    else if (entry is FileInfo file)
                    {
                        try
                        {
                            bytes += file.Length;
                            files++;
                        }
                        catch (IOException)
                        {
                            lengthFailures++;
                        }
                    }
                }
            }
            catch (DirectoryNotFoundException)
            {
                // A live tree (temp folders, build output) loses directories between listing and visit. Aborting the
                // whole walk would throw away minutes of work; count it so the caller sees the baseline is partial.
                vanished++;
            }
        }

        return new Baseline(files, bytes, links, lengthFailures, vanished);
    }

    public static int Synthetic(int nodeCount)
    {
        Stats.PrintHeader("synthetic", $"{nodeCount} nodes");

        long managedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var builder = new NodeIndexBuilder();
        var dirs = new List<int>();
        var random = new Random(1);
        Span<char> name = stackalloc char[16];
        int dirTarget = nodeCount / DirectoryFanout;

        var addClock = Stopwatch.StartNew();
        dirs.Add(builder.AddRoot("synthetic".AsSpan()));

        int nextParent = 0;
        while (dirs.Count < dirTarget && builder.Count < nodeCount)
        {
            int parent = dirs[nextParent++];
            for (int k = 0; k < DirectoryFanout && dirs.Count < dirTarget && builder.Count < nodeCount; k++)
            {
                int length = WriteName(name, builder.Count);
                dirs.Add(builder.Add(parent, name[..length], NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0));
            }
        }

        int fileOrdinal = 0;
        while (builder.Count < nodeCount)
        {
            int parent = dirs[fileOrdinal % dirs.Count];
            int length = WriteName(name, builder.Count);
            long size = random.NextInt64(0, 1L << 20);
            long allocated = (size + 4095) / 4096 * 4096;
            builder.Add(parent, name[..length], NodeKind.File, FileAttributes.Normal, size, allocated, 0, 0);
            fileOrdinal++;
        }
        addClock.Stop();

        var buildClock = Stopwatch.StartNew();
        NodeIndex index = builder.Build();
        buildClock.Stop();

        long managedAfter = GC.GetTotalAllocatedBytes(precise: true);
        long allocated8 = managedAfter - managedBefore;
        using Process process = Process.GetCurrentProcess();
        process.Refresh();

        Console.WriteLine($"Nodes:            {index.Count} (requested {nodeCount}), directories incl. root {dirs.Count}");
        Console.WriteLine($"Add:              {Stats.Fixed(addClock.Elapsed.TotalMilliseconds, "F1")} ms");
        Console.WriteLine($"Build:            {Stats.Fixed(buildClock.Elapsed.TotalMilliseconds, "F1")} ms (aggregation + CSR)");
        Console.WriteLine($"Managed alloc:    {Stats.Mb(allocated8)} MB, {Stats.Fixed((double)allocated8 / index.Count, "F1")} bytes/node");
        Console.WriteLine($"Peak working set: {Stats.Mb(process.PeakWorkingSet64)} MB");

        // Search and extension stats are linear scans: measured here, best of 3, instead of assumed instant.
        SearchResult search = NodeSearch.Find(index, new NodeFilter { NameContains = "n9" }, 1000);
        double searchMs = BestOf3(() => NodeSearch.Find(index, new NodeFilter { NameContains = "n9" }, 1000));
        double statsMs = BestOf3(() => ExtensionStats.Compute(index, SizeKind.Logical));
        Console.WriteLine($"Search (name):    {Stats.Fixed(searchMs, "F1")} ms, {search.TotalMatches} matches");
        Console.WriteLine($"Extension stats:  {Stats.Fixed(statsMs, "F1")} ms (synthetic names have no extension: 1 bucket)");
        return index.Count == nodeCount ? 0 : 1;
    }

    private static double BestOf3(Action action)
    {
        double best = double.MaxValue;
        for (int i = 0; i < 3; i++)
        {
            var clock = Stopwatch.StartNew();
            action();
            best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
        }

        return best;
    }

    private static int WriteName(Span<char> buffer, int ordinal)
    {
        buffer[0] = 'n';
        ordinal.TryFormat(buffer[1..], out int written, default, CultureInfo.InvariantCulture);
        return written + 1;
    }

    /// <summary>
    /// Refusal exit code is 3 (non-empty target, insufficient free space); caller passes a full path.
    /// </summary>
    public static int Generate(string root, int fileCount, int seed)
    {
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            Console.Error.WriteLine($"Refused: {root} exists and is not empty.");
            return 3;
        }

        var random = new Random(seed);
        var sizes = new long[fileCount];
        long total = 0;
        int capped = 0;
        for (int i = 0; i < fileCount; i++)
        {
            long size = DrawSize(random);
            if (total + size > GenerateCapBytes)
            {
                size = GenerateCapBytes - total;
                capped++;
            }

            sizes[i] = size;
            total += size;
        }

        string drive = Path.GetPathRoot(root) ?? throw new ArgumentException("Target has no drive root.", nameof(root));
        long free = new DriveInfo(drive).AvailableFreeSpace;
        Console.WriteLine($"Plan: {fileCount} files, logical total {total} bytes ({Stats.Fixed(total / GiB, "F2")} GiB), files clamped by 8 GiB cap: {capped}");
        Console.WriteLine($"Free space on {drive} {Stats.Fixed(free / GiB, "F2")} GiB; required total + 2 GiB.");
        if (free < total + GenerateReserveBytes)
        {
            Console.Error.WriteLine("Refused: not enough free space.");
            return 3;
        }

        var leaves = new List<string>(DirectoryFanout * DirectoryFanout * DirectoryFanout);
        for (int a = 0; a < DirectoryFanout; a++)
        {
            for (int b = 0; b < DirectoryFanout; b++)
            {
                for (int c = 0; c < DirectoryFanout; c++)
                {
                    string leaf = Path.Combine(root, $"d{a}", $"d{b}", $"d{c}");
                    Directory.CreateDirectory(leaf);
                    leaves.Add(leaf);
                }
            }
        }

        int maxSize = sizes.Length == 0 ? 0 : (int)sizes.Max();
        byte[] buffer = new byte[maxSize];
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < fileCount; i++)
        {
            string path = Path.Combine(leaves[i % leaves.Count], $"f{i}.bin");
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(buffer.AsSpan(0, (int)sizes[i]));
        }

        Console.WriteLine($"Wrote {fileCount} files in {leaves.Count} leaf directories under {root} in {Stats.Fixed(clock.Elapsed.TotalSeconds, "F1")} s.");
        return 0;
    }

    private static long DrawSize(Random random)
    {
        int bucket = random.Next(100);
        if (bucket < 70)
        {
            return random.NextInt64(0, 4L * 1024 + 1);
        }

        if (bucket < 95)
        {
            return random.NextInt64(4L * 1024, 1024L * 1024 + 1);
        }

        return random.NextInt64(1024L * 1024, 16L * 1024 * 1024 + 1);
    }

    private static string Signed(long value) => value.ToString("+#;-#;0", CultureInfo.InvariantCulture);

    private static string Percent(long engine, long baseline) =>
        baseline == 0 ? "n/a" : Stats.Fixed((engine - baseline) * 100.0 / baseline, "F3") + " %";
}
