using System.Runtime.InteropServices;

namespace DiskScope.Monitoring.CpuRam;

internal static unsafe partial class NativeMethods
{
    internal const int SystemProcessorPerformanceInformation = 8;

    // SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION: IdleTime, KernelTime, UserTime (LARGE_INTEGER), Reserved1[2] (LARGE_INTEGER),
    // Reserved2 (ULONG); 44 bytes padded to 48 by the 8-byte alignment of LARGE_INTEGER.
    internal const int ProcessorPerformanceEntrySize = 48;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PERFORMANCE_INFORMATION
    {
        public uint cb;
        public nuint CommitTotal;
        public nuint CommitLimit;
        public nuint CommitPeak;
        public nuint PhysicalTotal;
        public nuint PhysicalAvailable;
        public nuint SystemCache;
        public nuint KernelTotal;
        public nuint KernelPaged;
        public nuint KernelNonpaged;
        public nuint PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetPerformanceInfo(out PERFORMANCE_INFORMATION info, uint cb);

    /// <summary>
    /// NtQuerySystemInformation is resolved at run time, as its documentation requires ("may be altered or
    /// unavailable in future versions"); null when the export is missing.
    /// </summary>
    internal static readonly delegate* unmanaged[Stdcall]<int, void*, uint, uint*, int> NtQuerySystemInformation = Resolve();

    private static delegate* unmanaged[Stdcall]<int, void*, uint, uint*, int> Resolve()
    {
        if (NativeLibrary.TryLoad("ntdll.dll", out nint ntdll) && NativeLibrary.TryGetExport(ntdll, "NtQuerySystemInformation", out nint address))
        {
            return (delegate* unmanaged[Stdcall]<int, void*, uint, uint*, int>)address;
        }

        return null;
    }
}
