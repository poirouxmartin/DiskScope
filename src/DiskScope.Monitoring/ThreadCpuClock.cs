using System.Runtime.InteropServices;

namespace DiskScope.Monitoring;

/// <summary>
/// CPU time (kernel + user) consumed by the calling thread, from GetThreadTimes. Separates the work a collector does
/// from the time it spends waiting: on a saturated machine the wall-clock duration of a collection mostly measures
/// how long the thread waited for a processor.
/// Windows accounts thread time in clock ticks (about 15.6 ms by default), so one short collection reads 0 or one
/// tick; only totals over many collections are meaningful.
/// </summary>
public static partial class ThreadCpuClock
{
    /// <summary>Kernel + user time of the calling thread, or null if Windows refuses the query.</summary>
    public static TimeSpan? Current()
    {
        // FILETIME is two DWORDs (low, high) in 100 ns units: on little-endian Windows it reads as one long.
        return GetThreadTimes(GetCurrentThread(), out _, out _, out long kernel, out long user)
            ? TimeSpan.FromTicks(kernel + user)
            : null;
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentThread();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetThreadTimes(nint thread, out long creation, out long exit, out long kernel, out long user);
}
