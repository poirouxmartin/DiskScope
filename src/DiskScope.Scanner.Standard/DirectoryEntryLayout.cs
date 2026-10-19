namespace DiskScope.Scanner.Standard;

/// <summary>
/// FILE_ID_BOTH_DIR_INFO byte offsets, learn.microsoft.com/windows/win32/api/winbase/ns-winbase-file_id_both_dir_info
/// </summary>
internal static class DirectoryEntryLayout
{
    internal const int NextEntryOffset = 0;   // uint
    internal const int LastWriteTime = 24;    // long FILETIME
    internal const int EndOfFile = 40;        // long
    internal const int AllocationSize = 48;   // long
    internal const int FileAttributes = 56;   // uint
    internal const int FileNameLength = 60;   // uint, bytes
    internal const int EaSize = 64;           // uint, reparse tag when FILE_ATTRIBUTE_REPARSE_POINT is set
    internal const int FileId = 96;           // long
    internal const int FileName = 104;        // char[]

    internal static long FileTimeToUtcTicks(long fileTime) => fileTime <= 0 ? 0 : fileTime + 504911232000000000L;
}
