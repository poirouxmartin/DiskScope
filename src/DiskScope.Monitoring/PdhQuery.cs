namespace DiskScope.Monitoring;

/// <summary>
/// Minimal PDH query with English counter paths (PdhAddEnglishCounterW), so counter names do not depend on the
/// Windows display language. Wildcard instances are read with PdhGetFormattedCounterArrayW.
/// </summary>
public sealed unsafe class PdhQuery : IDisposable
{
    private nint _query;

    public PdhQuery()
    {
        int status = NativeMethods.PdhOpenQueryW(null, 0, out _query);
        if (status != 0)
        {
            throw new InvalidOperationException($"PdhOpenQuery failed, 0x{status:X8}");
        }
    }

    /// <summary>Adds an English counter path; returns 0 when the counter set does not exist on this machine.</summary>
    public nint TryAdd(string englishPath, out int status)
    {
        status = NativeMethods.PdhAddEnglishCounterW(_query, englishPath, 0, out nint counter);
        return status == 0 ? counter : 0;
    }

    public int Collect() => NativeMethods.PdhCollectQueryData(_query);

    /// <summary>Instances with valid data. Rate counters return nothing valid until the second collection.</summary>
    public List<(string Instance, double Value)> Read(nint counter, bool noCap = false)
    {
        var list = new List<(string, double)>();
        uint size = 0, count = 0;
        uint format = NativeMethods.PDH_FMT_DOUBLE | (noCap ? NativeMethods.PDH_FMT_NOCAP100 : 0);
        int status = NativeMethods.PdhGetFormattedCounterArrayW(counter, format, ref size, out count, null);
        if (status != NativeMethods.PDH_MORE_DATA || size == 0)
        {
            return list;
        }

        byte[] buffer = new byte[size];
        fixed (byte* p = buffer)
        {
            status = NativeMethods.PdhGetFormattedCounterArrayW(counter, format, ref size, out count, (NativeMethods.Item*)p);
            if (status != 0)
            {
                return list;
            }

            var items = (NativeMethods.Item*)p;
            for (int i = 0; i < count; i++)
            {
                if (items[i].CStatus is NativeMethods.PDH_CSTATUS_VALID_DATA or NativeMethods.PDH_CSTATUS_NEW_DATA && items[i].Name != null)
                {
                    list.Add((new string(items[i].Name), items[i].Value));
                }
            }
        }

        return list;
    }

    public void Dispose()
    {
        if (_query != 0)
        {
            _ = NativeMethods.PdhCloseQuery(_query);
            _query = 0;
        }
    }
}
