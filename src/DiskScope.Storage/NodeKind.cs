namespace DiskScope.Storage;

/// <summary>
/// Kind of a node in the index.
/// Link is a junction or symlink (name-surrogate reparse point): recorded, never traversed, never counted as a file.
/// </summary>
public enum NodeKind : byte
{
    File = 0,
    Directory = 1,
    Link = 2,
}
