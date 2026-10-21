using System.Globalization;

namespace DiskScope.Monitoring.Gpu;

/// <summary>
/// Parsed instance name of the Windows "GPU Engine" counter set, for example
/// "pid_1234_luid_0x00000000_0x0000D1F3_phys_0_eng_3_engtype_VideoDecode".
/// </summary>
public readonly record struct GpuEngineInstance(int ProcessId, string Luid, int PhysicalAdapter, int Engine, string EngineType)
{
    public static bool TryParse(string name, out GpuEngineInstance instance)
    {
        instance = default;
        string[] p = name.Split('_');

        // pid N luid 0xH 0xL phys N eng N engtype TYPE (the type itself may contain '_', e.g. "Compute_0")
        if (p.Length < 11 || p[0] != "pid" || p[2] != "luid" || p[5] != "phys" || p[7] != "eng" || p[9] != "engtype")
        {
            return false;
        }

        if (!int.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out int pid)
            || !int.TryParse(p[6], NumberStyles.None, CultureInfo.InvariantCulture, out int phys)
            || !int.TryParse(p[8], NumberStyles.None, CultureInfo.InvariantCulture, out int eng))
        {
            return false;
        }

        instance = new GpuEngineInstance(pid, $"{p[3]}_{p[4]}".ToUpperInvariant().Replace("0X", "0x", StringComparison.Ordinal), phys, eng, string.Join('_', p[10..]));
        return true;
    }
}

/// <summary>Usage of one engine type of one adapter, and which processes use it.</summary>
public sealed record GpuEngineUsage(string Luid, string EngineType, double Percent);

/// <summary>
/// Aggregation of per-process, per-engine utilization samples, using the Task Manager definitions:
/// an engine's usage is the sum over processes (capped at 100); an engine type's usage is the busiest engine of that
/// type (several Copy or Compute engines may exist); a process's usage is its busiest engine.
/// Summing engine types would be wrong: they run in parallel, so 3D 60 % + Copy 30 % is not 90 % of the GPU.
/// </summary>
public static class GpuCounterMath
{
    public static IReadOnlyList<GpuEngineUsage> AggregateEngines(IEnumerable<(GpuEngineInstance Instance, double Percent)> samples)
    {
        var perEngine = new Dictionary<(string Luid, int Phys, int Eng, string Type), double>();
        foreach (var (i, percent) in samples)
        {
            var key = (i.Luid, i.PhysicalAdapter, i.Engine, i.EngineType);
            perEngine[key] = perEngine.GetValueOrDefault(key) + percent;
        }

        return perEngine
            .GroupBy(kv => (kv.Key.Luid, kv.Key.Type))
            .Select(g => new GpuEngineUsage(g.Key.Luid, g.Key.Type, Math.Min(100, g.Max(kv => kv.Value))))
            .OrderBy(u => u.Luid, StringComparer.Ordinal)
            .ThenBy(u => u.EngineType, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyDictionary<int, double> AggregateProcesses(IEnumerable<(GpuEngineInstance Instance, double Percent)> samples)
    {
        var perProcessEngine = new Dictionary<(int Pid, string Luid, int Phys, int Eng), double>();
        foreach (var (i, percent) in samples)
        {
            var key = (i.ProcessId, i.Luid, i.PhysicalAdapter, i.Engine);
            perProcessEngine[key] = perProcessEngine.GetValueOrDefault(key) + percent;
        }

        return perProcessEngine.GroupBy(kv => kv.Key.Pid).ToDictionary(g => g.Key, g => Math.Min(100, g.Max(kv => kv.Value)));
    }

    /// <summary>"luid_0x00000000_0x0000D1F3_phys_0" -> "0x00000000_0x0000D1F3"; null if the name has another shape.</summary>
    public static string? LuidOfAdapterInstance(string name)
    {
        string[] p = name.Split('_');
        return p.Length >= 3 && p[0] == "luid"
            ? $"{p[1]}_{p[2]}".ToUpperInvariant().Replace("0X", "0x", StringComparison.Ordinal)
            : null;
    }

    public static string FormatLuid(uint lowPart, int highPart) =>
        string.Create(CultureInfo.InvariantCulture, $"0x{(uint)highPart:X8}_0x{lowPart:X8}");
}
