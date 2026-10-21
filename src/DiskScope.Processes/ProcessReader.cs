using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DiskScope.Processes;

/// <summary>Result of one process enumeration.</summary>
/// <param name="Inaccessible">Processes listed but not openable with PROCESS_QUERY_LIMITED_INFORMATION (protected, other session, system).</param>
public sealed record ProcessReadResult(IReadOnlyList<ProcessSnapshot> Processes, int Inaccessible, TimeSpan Duration);

/// <summary>
/// Lists processes with a Toolhelp32 snapshot (PID, parent PID, threads, image name: no handle needed), then opens each
/// one with PROCESS_QUERY_LIMITED_INFORMATION only, the least privileged right the documented calls accept
/// (GetProcessTimes, GetProcessMemoryInfo, GetProcessHandleCount, GetProcessIoCounters, QueryFullProcessImageName).
/// Read-only: no process is modified. A process that exits between the snapshot and the open is skipped; a process
/// that refuses the open is kept with its handle-dependent fields null.
/// PID 0 (System Idle Process) is not listed: its "CPU time" is idle time.
/// </summary>
public static unsafe class ProcessReader
{
    private static bool s_ex2Unsupported;

    public static ProcessReadResult ReadAll()
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        using SafeFileHandle snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
        if (snapshot.IsInvalid)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "CreateToolhelp32Snapshot failed");
        }

        var list = new List<ProcessSnapshot>(512);
        int inaccessible = 0;
        var entry = new NativeMethods.PROCESSENTRY32W { dwSize = (uint)sizeof(NativeMethods.PROCESSENTRY32W) };
        bool ok = NativeMethods.Process32FirstW(snapshot, ref entry);
        while (ok)
        {
            if (entry.th32ProcessID != 0)
            {
                string name = new string(entry.szExeFile, 0, IndexOfNul(entry.szExeFile, 260));
                ProcessSnapshot? process = Read((int)entry.th32ProcessID, (int)entry.th32ParentProcessID, name, (int)entry.cntThreads);
                if (process is not null)
                {
                    inaccessible += process.IsAccessible ? 0 : 1;
                    list.Add(process);
                }
            }

            ok = NativeMethods.Process32NextW(snapshot, ref entry);
        }

        return new ProcessReadResult(list, inaccessible, System.Diagnostics.Stopwatch.GetElapsedTime(start));
    }

    private static ProcessSnapshot? Read(int pid, int parentPid, string name, int threads)
    {
        using SafeProcessHandle h = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (h.IsInvalid)
        {
            // ERROR_INVALID_PARAMETER: the process exited after the snapshot; anything else: access refused.
            return Marshal.GetLastPInvokeError() == 87
                ? null
                : new ProcessSnapshot { Key = new ProcessKey(pid, 0), ParentProcessId = parentPid, Name = name, ThreadCount = threads, IsAccessible = false };
        }

        long creationTicks = 0;
        long? cpu = null;
        if (NativeMethods.GetProcessTimes(h, out long creation, out _, out long kernel, out long user))
        {
            creationTicks = creation > 0 ? creation + NativeMethods.FileTimeToDateTimeTicks : 0;
            cpu = kernel + user;
        }

        ReadMemory(h, out long? workingSet, out long? privateWs, out long? privateBytes);

        int? handles = NativeMethods.GetProcessHandleCount(h, out uint count) ? (int)count : null;
        long? ioRead = null, ioWrite = null;
        if (NativeMethods.GetProcessIoCounters(h, out NativeMethods.IO_COUNTERS io))
        {
            ioRead = (long)io.ReadTransferCount;
            ioWrite = (long)io.WriteTransferCount;
        }

        return new ProcessSnapshot
        {
            Key = new ProcessKey(pid, creationTicks),
            ParentProcessId = parentPid,
            Name = name,
            ThreadCount = threads,
            IsAccessible = true,
            ImagePath = QueryImagePath(h),
            CpuTimeTicks = cpu,
            WorkingSetBytes = workingSet,
            PrivateWorkingSetBytes = privateWs,
            PrivateBytes = privateBytes,
            HandleCount = handles,
            IoReadBytes = ioRead,
            IoWriteBytes = ioWrite,
        };
    }

    internal static void ReadMemory(SafeProcessHandle h, out long? workingSet, out long? privateWorkingSet, out long? privateBytes)
    {
        workingSet = privateWorkingSet = privateBytes = null;
        NativeMethods.PROCESS_MEMORY_COUNTERS_EX2 c = default;
        bool ex2 = !s_ex2Unsupported && NativeMethods.GetProcessMemoryInfo(h, out c, (uint)sizeof(NativeMethods.PROCESS_MEMORY_COUNTERS_EX2));
        if (!ex2)
        {
            // EX2 needs Windows 10/11 22H2 with the September 2023 update; older builds reject the larger size.
            if (!NativeMethods.GetProcessMemoryInfo(h, out c, NativeMethods.MemoryCountersExSize))
            {
                return;
            }

            s_ex2Unsupported = true;
        }

        workingSet = (long)c.WorkingSetSize;
        privateBytes = (long)c.PrivateUsage;

        // A build that accepts the EX2 size without filling it would leave 0 here; a running process with a
        // working set always has private pages, so 0 next to a non-zero working set is read as unknown.
        privateWorkingSet = ex2 && (c.PrivateWorkingSetSize != 0 || c.WorkingSetSize == 0) ? (long)c.PrivateWorkingSetSize : null;
    }

    private static string? QueryImagePath(SafeProcessHandle h)
    {
        char* buffer = stackalloc char[1024];
        uint size = 1024;
        return NativeMethods.QueryFullProcessImageNameW(h, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
    }

    private static int IndexOfNul(char* s, int max)
    {
        for (int i = 0; i < max; i++)
        {
            if (s[i] == '\0')
            {
                return i;
            }
        }

        return max;
    }
}
