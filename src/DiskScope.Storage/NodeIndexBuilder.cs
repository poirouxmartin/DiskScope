using System.IO;

namespace DiskScope.Storage;

/// <summary>
/// Single-writer, append-only builder for <see cref="NodeIndex"/>.
/// Storage is struct-of-arrays: one array per column, no per-node object, names copied into one char pool.
/// Parent ids are always smaller than child ids; NodeIndex aggregation relies on it.
/// </summary>
public sealed class NodeIndexBuilder
{
    private int _count;
    private int _nameEnd;
    private bool _built;

    private int[] _parent;
    private int[] _nameOffset;
    private int[] _nameLength;
    private char[] _names;
    private NodeKind[] _kind;
    private FileAttributes[] _attributes;
    private long[] _logical;
    private long[] _allocated;
    private long[] _lastWriteTicks;
    private ulong[] _fileReference;
    private NodeStatus[] _status;
    private readonly List<ScanError> _errors = new();

    public NodeIndexBuilder(int initialCapacity = 1024)
    {
        if (initialCapacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        }

        _parent = new int[initialCapacity];
        _nameOffset = new int[initialCapacity];
        _nameLength = new int[initialCapacity];
        _names = new char[initialCapacity * 16];
        _kind = new NodeKind[initialCapacity];
        _attributes = new FileAttributes[initialCapacity];
        _logical = new long[initialCapacity];
        _allocated = new long[initialCapacity];
        _lastWriteTicks = new long[initialCapacity];
        _fileReference = new ulong[initialCapacity];
        _status = new NodeStatus[initialCapacity];
    }

    public int Count => _count;

    public int AddRoot(ReadOnlySpan<char> fullPath)
    {
        ThrowIfBuilt();
        if (_count != 0)
        {
            throw new InvalidOperationException("The root has already been added; it must be the first node.");
        }

        return Append(-1, fullPath, NodeKind.Directory, default, 0, 0, 0, 0);
    }

    public int Add(
        int parentId,
        ReadOnlySpan<char> name,
        NodeKind kind,
        FileAttributes attributes,
        long logicalSize,
        long allocatedSize,
        long lastWriteUtcTicks,
        ulong fileReference)
    {
        ThrowIfBuilt();
        if ((uint)parentId >= (uint)_count)
        {
            throw new ArgumentOutOfRangeException(nameof(parentId), parentId, "Parent id does not refer to an existing node.");
        }

        if (_kind[parentId] != NodeKind.Directory)
        {
            throw new ArgumentException("Parent node must be a directory.", nameof(parentId));
        }

        return Append(parentId, name, kind, attributes, logicalSize, allocatedSize, lastWriteUtcTicks, fileReference);
    }

    public void AddStatus(int id, NodeStatus flags)
    {
        ThrowIfBuilt();
        CheckId(id, nameof(id));
        _status[id] |= flags;
    }

    public void AddError(ScanError error)
    {
        ThrowIfBuilt();
        CheckId(error.NodeId, nameof(error));
        _errors.Add(error);
        _status[error.NodeId] |= error.Kind switch
        {
            ScanErrorKind.AccessDenied => NodeStatus.AccessDenied,
            ScanErrorKind.NotFound => NodeStatus.Vanished,
            _ => NodeStatus.IoError, // IoError and Unexpected
        };
    }

    public NodeKind GetKind(int id)
    {
        CheckId(id, nameof(id));
        return _kind[id];
    }

    public NodeIndex Build()
    {
        ThrowIfBuilt();
        if (_count == 0)
        {
            throw new InvalidOperationException("Build requires a root node (call AddRoot first).");
        }

        _built = true;
        return CreateIndex(_status.AsSpan(0, _count).ToArray(), _errors);
    }

    /// <summary>
    /// Provisional index of everything added so far; the builder stays writable.
    /// Must be called from the writer thread (the builder is single-writer, so no lock is needed).
    /// Directories still waiting to be enumerated are flagged NotScanned in the copy, so their ancestors
    /// read SubtreeIncomplete and the snapshot can never pass for a complete result.
    /// Cost: one copy of every column, O(Count).
    /// </summary>
    public NodeIndex Snapshot(IEnumerable<int> pendingDirectories)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(pendingDirectories);
        if (_count == 0)
        {
            throw new InvalidOperationException("Snapshot requires a root node (call AddRoot first).");
        }

        NodeStatus[] status = _status.AsSpan(0, _count).ToArray();
        foreach (int id in pendingDirectories)
        {
            CheckId(id, nameof(pendingDirectories));
            status[id] |= NodeStatus.NotScanned;
        }

        return CreateIndex(status, new List<ScanError>(_errors));
    }

    private NodeIndex CreateIndex(NodeStatus[] status, List<ScanError> errors) =>
        new(
            _count,
            _parent.AsSpan(0, _count).ToArray(),
            _nameOffset.AsSpan(0, _count).ToArray(),
            _nameLength.AsSpan(0, _count).ToArray(),
            _names.AsSpan(0, _nameEnd).ToArray(),
            _kind.AsSpan(0, _count).ToArray(),
            _attributes.AsSpan(0, _count).ToArray(),
            _logical.AsSpan(0, _count).ToArray(),
            _allocated.AsSpan(0, _count).ToArray(),
            _lastWriteTicks.AsSpan(0, _count).ToArray(),
            _fileReference.AsSpan(0, _count).ToArray(),
            status,
            errors);

    private int Append(
        int parentId,
        ReadOnlySpan<char> name,
        NodeKind kind,
        FileAttributes attributes,
        long logicalSize,
        long allocatedSize,
        long lastWriteUtcTicks,
        ulong fileReference)
    {
        int id = _count;
        EnsureNodeCapacity(id + 1);
        EnsureNameCapacity(_nameEnd + name.Length);

        name.CopyTo(_names.AsSpan(_nameEnd));
        _parent[id] = parentId;
        _nameOffset[id] = _nameEnd;
        _nameLength[id] = name.Length;
        _kind[id] = kind;
        _attributes[id] = attributes;
        _logical[id] = logicalSize;
        _allocated[id] = allocatedSize;
        _lastWriteTicks[id] = lastWriteUtcTicks;
        _fileReference[id] = fileReference;
        _status[id] = NodeStatus.None;

        _nameEnd += name.Length;
        _count++;
        return id;
    }

    private void EnsureNodeCapacity(int required)
    {
        if (required <= _parent.Length)
        {
            return;
        }

        int newLength = Math.Max(required, _parent.Length * 2);
        Array.Resize(ref _parent, newLength);
        Array.Resize(ref _nameOffset, newLength);
        Array.Resize(ref _nameLength, newLength);
        Array.Resize(ref _kind, newLength);
        Array.Resize(ref _attributes, newLength);
        Array.Resize(ref _logical, newLength);
        Array.Resize(ref _allocated, newLength);
        Array.Resize(ref _lastWriteTicks, newLength);
        Array.Resize(ref _fileReference, newLength);
        Array.Resize(ref _status, newLength);
    }

    private void EnsureNameCapacity(int required)
    {
        if (required <= _names.Length)
        {
            return;
        }

        Array.Resize(ref _names, Math.Max(required, _names.Length * 2));
    }

    private void CheckId(int id, string paramName)
    {
        if ((uint)id >= (uint)_count)
        {
            throw new ArgumentOutOfRangeException(paramName, id, "Node id does not refer to an existing node.");
        }
    }

    private void ThrowIfBuilt()
    {
        if (_built)
        {
            throw new InvalidOperationException("This builder has already been built and cannot be modified.");
        }
    }
}
