namespace DiskScope.Monitoring.CpuRam;

/// <summary>
/// Cumulative processor times in 100 ns ticks, as returned by GetSystemTimes or per processor by
/// NtQuerySystemInformation(SystemProcessorPerformanceInformation). Kernel time INCLUDES idle time (documented
/// for GetSystemTimes), so busy = kernel + user - idle.
/// </summary>
public readonly record struct CpuTimes(long Idle, long Kernel, long User);

/// <summary>Usage over one interval, in percent of the processors' capacity (0-100).</summary>
public readonly record struct CpuUsage(double Total, double User, double Kernel);

public static class CpuUsageCalculator
{
    /// <summary>
    /// Usage between two samples, or null when it cannot be computed honestly: no time elapsed (two samples in the
    /// same tick), a counter going backwards (wrap or reset), or idle growing faster than kernel time (inconsistent source).
    /// Values are clamped to [0, 100] only against rounding, never to hide an inconsistency.
    /// </summary>
    public static CpuUsage? Compute(CpuTimes previous, CpuTimes current)
    {
        long idle = current.Idle - previous.Idle;
        long kernel = current.Kernel - previous.Kernel;
        long user = current.User - previous.User;
        long total = kernel + user;
        if (idle < 0 || kernel < 0 || user < 0 || total <= 0 || idle > kernel)
        {
            return null;
        }

        double scale = 100.0 / total;
        return new CpuUsage(
            Clamp((total - idle) * scale),
            Clamp(user * scale),
            Clamp((kernel - idle) * scale));
    }

    private static double Clamp(double percent) => Math.Clamp(percent, 0, 100);
}
