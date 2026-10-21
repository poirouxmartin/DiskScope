using System.ComponentModel;
using System.Diagnostics;
using DiskScope.Core;
using DiskScope.Scanner.Ntfs;

namespace DiskScope.Benchmarks;

/// <summary>
/// Step 1-3 of the NTFS prototype plan (docs/NTFS-RISKS.md section 8): enumerate a volume, rebuild the tree, report coherence.
/// Prints record counts and timings only; sizes are not available from USN records.
/// </summary>
internal static class UsnProbe
{
    public static int Run(char drive)
    {
        if (!Environment.IsPrivilegedProcess)
        {
            Console.Error.WriteLine("usn-probe needs an elevated console (opening the volume handle requires administrator rights).");
            return 3;
        }

        UInt128 root = UsnVolumeEnumerator.GetRootReference(drive);
        var store = new UsnRecordStore();
        long before = GC.GetTotalAllocatedBytes(precise: true);

        UsnEnumerationStats stats;
        try
        {
            stats = UsnVolumeEnumerator.Enumerate(drive, store, CancellationToken.None);
        }
        catch (Win32Exception ex)
        {
            Console.Error.WriteLine($"FSCTL_ENUM_USN_DATA failed: {ex.NativeErrorCode} {ex.Message}");
            return 1;
        }

        var sw = Stopwatch.StartNew();
        UsnTree tree = UsnTreeBuilder.Build(store, root, $"{drive}:\\");
        TimeSpan buildTime = sw.Elapsed;
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        NodeIndex idx = tree.Index;

        Console.WriteLine($"volume            {drive}:  root reference 0x{root:X}");
        Console.WriteLine($"ioctl calls       {stats.Calls:N0}");
        Console.WriteLine($"records accepted  {stats.Accepted:N0}");
        Console.WriteLine($"unsupported ver.  {stats.UnsupportedVersion:N0}");
        Console.WriteLine($"malformed         {stats.Malformed:N0}");
        Console.WriteLine($"truncated buffers {stats.TruncatedBuffers:N0}");
        Console.WriteLine($"enumeration       {stats.Elapsed.TotalMilliseconds:N0} ms");
        Console.WriteLine($"tree rebuild      {buildTime.TotalMilliseconds:N0} ms");
        Console.WriteLine($"nodes             {idx.Count:N0} (files {idx.GetRecursiveFileCount(NodeIndex.RootId):N0}, directories {idx.GetRecursiveDirectoryCount(NodeIndex.RootId):N0})");
        Console.WriteLine($"orphans           {tree.Orphans:N0}  duplicate references {tree.DuplicateReferences:N0}");
        Console.WriteLine($"allocated         {allocated / (1024 * 1024):N0} MB");
        Console.WriteLine($"peak working set  {Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024):N0} MB");
        return 0;
    }
}
