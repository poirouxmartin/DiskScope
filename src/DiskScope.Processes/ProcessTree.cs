namespace DiskScope.Processes;

/// <summary>
/// Parent/child tree of one process snapshot. The recorded parent PID may belong to a process that exited, and Windows
/// may have reused that PID for a newer process: a parent is accepted only if it was created before (or at the same
/// time as) the child. When either creation time is unknown, the link is accepted but marked unverified.
/// Processes without an accepted parent are roots.
/// </summary>
public sealed class ProcessTree
{
    private readonly Dictionary<ProcessKey, List<ProcessSnapshot>> _children = new();

    private ProcessTree(IReadOnlyList<ProcessSnapshot> roots, IReadOnlySet<ProcessKey> unverified)
    {
        Roots = roots;
        UnverifiedLinks = unverified;
    }

    public IReadOnlyList<ProcessSnapshot> Roots { get; }

    /// <summary>Children whose parent link could not be checked against creation times.</summary>
    public IReadOnlySet<ProcessKey> UnverifiedLinks { get; }

    public IReadOnlyList<ProcessSnapshot> GetChildren(ProcessKey parent) =>
        _children.TryGetValue(parent, out List<ProcessSnapshot>? c) ? c : Array.Empty<ProcessSnapshot>();

    public static ProcessTree Build(IReadOnlyList<ProcessSnapshot> processes)
    {
        ArgumentNullException.ThrowIfNull(processes);

        // One live process per PID at a time, so the PID is a valid lookup key inside one snapshot.
        var byPid = new Dictionary<int, ProcessSnapshot>(processes.Count);
        foreach (ProcessSnapshot p in processes)
        {
            byPid.TryAdd(p.ProcessId, p);
        }

        var roots = new List<ProcessSnapshot>();
        var unverified = new HashSet<ProcessKey>();
        var tree = new ProcessTree(roots, unverified);
        foreach (ProcessSnapshot p in processes)
        {
            if (p.ParentProcessId != p.ProcessId
                && byPid.TryGetValue(p.ParentProcessId, out ProcessSnapshot? parent)
                && IsPlausibleParent(parent, p, out bool verified))
            {
                if (!tree._children.TryGetValue(parent.Key, out List<ProcessSnapshot>? list))
                {
                    tree._children[parent.Key] = list = new List<ProcessSnapshot>();
                }

                list.Add(p);
                if (!verified)
                {
                    unverified.Add(p.Key);
                }
            }
            else
            {
                roots.Add(p);
            }
        }

        // A parent cycle (possible only through unverified links) would leave its members unreachable: promote them.
        var reachable = new HashSet<ProcessKey>();
        var stack = new Stack<ProcessSnapshot>(roots);
        while (stack.Count > 0)
        {
            ProcessSnapshot current = stack.Pop();
            if (reachable.Add(current.Key))
            {
                foreach (ProcessSnapshot child in tree.GetChildren(current.Key))
                {
                    stack.Push(child);
                }
            }
        }

        foreach (ProcessSnapshot p in processes)
        {
            if (!reachable.Contains(p.Key))
            {
                roots.Add(p);
                reachable.Add(p.Key);
                foreach (List<ProcessSnapshot> list in tree._children.Values)
                {
                    list.Remove(p);
                }
            }
        }

        return tree;
    }

    private static bool IsPlausibleParent(ProcessSnapshot parent, ProcessSnapshot child, out bool verified)
    {
        verified = parent.Key.IsCreationTimeKnown && child.Key.IsCreationTimeKnown;
        return !verified || parent.Key.CreationTimeUtcTicks <= child.Key.CreationTimeUtcTicks;
    }
}
