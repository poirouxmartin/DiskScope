namespace DiskScope.Core;

/// <summary>
/// Status flags of a node.
/// AccessDenied, IoError, Vanished and NotScanned describe the node itself: its own contents are unknown or incomplete.
/// SubtreeIncomplete is computed by NodeIndex build: it is set on every node whose own status or any descendant status is non-None.
/// </summary>
[Flags]
public enum NodeStatus : byte
{
    None = 0,
    AccessDenied = 1,
    IoError = 2,
    Vanished = 4,
    NotScanned = 8,
    SubtreeIncomplete = 16,
}

public static class NodeStatusExtensions
{
    public const NodeStatus OwnErrorMask = NodeStatus.AccessDenied | NodeStatus.IoError | NodeStatus.Vanished | NodeStatus.NotScanned;

    public static bool IsIncomplete(this NodeStatus status) => status != NodeStatus.None;
}
