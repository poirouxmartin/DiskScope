using System.Globalization;
using System.Runtime.InteropServices;
using DiskScope.Core;

namespace DiskScope.Monitoring.CpuRam;

/// <summary>
/// Global CPU usage from GetSystemTimes and per-logical-processor usage from
/// NtQuerySystemInformation(SystemProcessorPerformanceInformation), both as deltas between two collections.
/// The first collection has no previous sample, so every value is Unavailable until the second.
/// Limits: on machines with more than 64 logical processors both sources cover only the calling thread's
/// processor group (documented for GetSystemTimes). Frequency is not published: no source exposes the effective
/// frequency reliably without a driver (see docs/MONITORING.md).
/// </summary>
public sealed unsafe class CpuCollector : IMetricCollector
{
    public const string Total = MetricIds.CpuTotal;
    public const string User = MetricIds.CpuUser;
    public const string Kernel = MetricIds.CpuKernel;

    private const string SystemSource = "GetSystemTimes (delta)";
    private const string CoreSource = "NtQuerySystemInformation SystemProcessorPerformanceInformation (delta)";

    private readonly int _coreCount;
    private readonly string[] _coreIds;
    private readonly byte[] _coreBuffer;
    private CpuTimes? _previous;
    private CpuTimes[]? _previousCores;

    public CpuCollector()
    {
        _coreCount = QueryCoreCount();
        _coreIds = Enumerable.Range(0, _coreCount).Select(CoreId).ToArray();
        _coreBuffer = new byte[Math.Max(1, _coreCount) * NativeMethods.ProcessorPerformanceEntrySize];

        var metrics = new List<MetricDescriptor>
        {
            new(Total, "CPU usage", MetricUnit.Percent, SystemSource, "(kernel + user - idle) / (kernel + user)"),
            new(User, "CPU user time", MetricUnit.Percent, SystemSource, "user / (kernel + user)"),
            new(Kernel, "CPU kernel time", MetricUnit.Percent, SystemSource, "(kernel - idle) / (kernel + user)"),
        };
        metrics.AddRange(_coreIds.Select((id, i) => new MetricDescriptor(id, $"CPU {i}", MetricUnit.Percent, CoreSource)));
        Metrics = metrics;
    }

    public string Name => "CPU";

    public IReadOnlyList<MetricDescriptor> Metrics { get; }

    public TimeSpan DefaultInterval => TimeSpan.FromSeconds(1);

    public int LogicalProcessorCount => _coreCount;

    public static string CoreId(int index) => MetricIds.CpuCorePrefix + index.ToString(CultureInfo.InvariantCulture);

    public CollectorAvailability CheckAvailability() =>
        NativeMethods.GetSystemTimes(out _, out _, out _)
            ? CollectorAvailability.Available(_coreCount > 0 ? $"{_coreCount} logical processors" : "Global usage only: per-processor source unavailable")
            : CollectorAvailability.NotAvailable($"GetSystemTimes failed ({Marshal.GetLastPInvokeError()})");

    public CollectorResult Collect(CancellationToken cancellationToken)
    {
        var values = new List<MetricValue>(3 + _coreCount);
        var errors = new List<CollectorError>();

        if (NativeMethods.GetSystemTimes(out long idle, out long kernel, out long user))
        {
            var now = new CpuTimes(idle, kernel, user);
            CpuUsage? usage = _previous is { } prev ? CpuUsageCalculator.Compute(prev, now) : null;
            _previous = now;
            values.Add(usage is { } u ? MetricValue.Derived(Total, u.Total) : MetricValue.Unavailable(Total));
            values.Add(usage is { } u2 ? MetricValue.Derived(User, u2.User) : MetricValue.Unavailable(User));
            values.Add(usage is { } u3 ? MetricValue.Derived(Kernel, u3.Kernel) : MetricValue.Unavailable(Kernel));
        }
        else
        {
            int code = Marshal.GetLastPInvokeError();
            _previous = null;
            errors.Add(new CollectorError(Name, "GetSystemTimes failed", code));
            values.Add(MetricValue.Unavailable(Total));
            values.Add(MetricValue.Unavailable(User));
            values.Add(MetricValue.Unavailable(Kernel));
        }

        CollectCores(values, errors);
        return new CollectorResult(values, errors);
    }

    private void CollectCores(List<MetricValue> values, List<CollectorError> errors)
    {
        if (_coreCount == 0)
        {
            return;
        }

        CpuTimes[]? now = QueryCores(out int status);
        if (now is null)
        {
            _previousCores = null;
            errors.Add(new CollectorError(Name, $"NtQuerySystemInformation failed, NTSTATUS 0x{status:X8}"));
            values.AddRange(_coreIds.Select(MetricValue.Unavailable));
            return;
        }

        for (int i = 0; i < _coreCount; i++)
        {
            CpuUsage? usage = _previousCores is { } prev ? CpuUsageCalculator.Compute(prev[i], now[i]) : null;
            values.Add(usage is { } u ? MetricValue.Derived(_coreIds[i], u.Total) : MetricValue.Unavailable(_coreIds[i]));
        }

        _previousCores = now;
    }

    private CpuTimes[]? QueryCores(out int status)
    {
        uint returned = 0;
        fixed (byte* p = _coreBuffer)
        {
            status = NativeMethods.NtQuerySystemInformation(NativeMethods.SystemProcessorPerformanceInformation, p, (uint)_coreBuffer.Length, &returned);
        }

        if (status < 0 || returned < _coreBuffer.Length)
        {
            return null;
        }

        var result = new CpuTimes[_coreCount];
        ReadOnlySpan<byte> span = _coreBuffer;
        for (int i = 0; i < _coreCount; i++)
        {
            ReadOnlySpan<byte> e = span.Slice(i * NativeMethods.ProcessorPerformanceEntrySize);
            result[i] = new CpuTimes(MemoryMarshal.Read<long>(e), MemoryMarshal.Read<long>(e[8..]), MemoryMarshal.Read<long>(e[16..]));
        }

        return result;
    }

    private static int QueryCoreCount()
    {
        if (NativeMethods.NtQuerySystemInformation == null)
        {
            return 0;
        }

        // Environment.ProcessorCount honours affinity and job limits, so it can be lower than the processors the call
        // reports; a generous buffer avoids a length mismatch and the returned length gives the real entry count.
        int guess = Math.Max(256, Environment.ProcessorCount);
        var buffer = new byte[guess * NativeMethods.ProcessorPerformanceEntrySize];
        uint returned = 0;
        int status;
        fixed (byte* p = buffer)
        {
            status = NativeMethods.NtQuerySystemInformation(NativeMethods.SystemProcessorPerformanceInformation, p, (uint)buffer.Length, &returned);
        }

        return status < 0 ? 0 : (int)(returned / NativeMethods.ProcessorPerformanceEntrySize);
    }
}
