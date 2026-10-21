using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using DiskScope.Storage;

namespace DiskScope.Scanner.Standard;

/// <summary>
/// Single-threaded on purpose: parallel I/O is not measured yet.
/// One directory handle per directory, no per-file handle; sizes come from the directory entry.
/// A directory deleted between listing and opening is reported Vanished.
/// A failed directory is never treated as empty.
/// </summary>
public sealed class StandardScanEngine : IScanEngine
{
    private const int BufferSize = 64 * 1024;

    // Test seam: runs on the scan thread just before a directory handle is opened,
    // so a test can delete or lock the directory between listing and enumeration.
    private readonly Action<string>? _beforeDirectoryOpen;

    public StandardScanEngine()
    {
    }

    internal StandardScanEngine(Action<string> beforeDirectoryOpen)
    {
        _beforeDirectoryOpen = beforeDirectoryOpen;
    }

    public string Name => "Standard";

    public EngineAvailability CheckAvailability(ScanRequest request) =>
        Directory.Exists(request.RootPath)
            ? new EngineAvailability(true, "Directory enumeration via GetFileInformationByHandleEx")
            : new EngineAvailability(false, "Root path not found");

    public Task<ScanResult> ScanAsync(ScanRequest request, IScanObserver? observer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RootPath);
        string root = Path.GetFullPath(request.RootPath);
        return Task.Factory.StartNew(
            () => Scan(root, request.Options, observer, cancellationToken),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private unsafe ScanResult Scan(string root, ScanOptions options, IScanObserver? observer, CancellationToken ct)
    {
        var builder = new NodeIndexBuilder();
        int rootId = builder.AddRoot(root);
        var stack = new Stack<(int Id, string Path)>();
        stack.Push((rootId, root));
        var counters = new Counters();
        var sw = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        var partials = new PartialResultSchedule(options);
        var outcome = ScanOutcome.Completed;
        string? failure = null;

        byte* buffer = (byte*)NativeMemory.Alloc(BufferSize);
        try
        {
            try
            {
                while (stack.Count > 0)
                {
                    if (ct.IsCancellationRequested)
                    {
                        outcome = ScanOutcome.Cancelled;
                        foreach (var (id, _) in stack)
                        {
                            builder.AddStatus(id, NodeStatus.NotScanned);
                        }
                        break;
                    }

                    var (dirId, dirPath) = stack.Pop();
                    _beforeDirectoryOpen?.Invoke(dirPath);
                    EnumerateDirectory(builder, stack, dirId, dirPath, buffer, counters);
                    if (sw.Elapsed - lastReport >= options.ProgressInterval)
                    {
                        lastReport = sw.Elapsed;
                        Report(observer, counters, dirPath, lastReport);
                    }

                    // An empty stack means the final result is moments away; a snapshot then would carry no NotScanned flag.
                    if (observer is not null && stack.Count > 0 && partials.IsDue(sw.Elapsed))
                    {
                        TimeSpan start = sw.Elapsed;
                        NodeIndex partial = builder.Snapshot(stack.Select(s => s.Id));
                        partials.Published(sw.Elapsed, sw.Elapsed - start);
                        observer.OnPartialResult(partial);
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                outcome = ScanOutcome.Failed;
                failure = ex.Message;

                // The result stays usable but must say which directories were never visited.
                foreach (var (id, _) in stack)
                {
                    builder.AddStatus(id, NodeStatus.NotScanned);
                }
            }
        }
        finally
        {
            NativeMemory.Free(buffer);
        }

        Report(observer, counters, null, sw.Elapsed);
        return new ScanResult(builder.Build(), outcome, sw.Elapsed, Name, failure);
    }

    private static unsafe void EnumerateDirectory(
        NodeIndexBuilder builder,
        Stack<(int Id, string Path)> stack,
        int dirId,
        string dirPath,
        byte* buffer,
        Counters c)
    {
        using SafeFileHandle h = NativeMethods.CreateFileW(
            ToExtendedPath(dirPath),
            NativeMethods.FILE_LIST_DIRECTORY,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE | NativeMethods.FILE_SHARE_DELETE,
            0,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_BACKUP_SEMANTICS,
            0);
        if (h.IsInvalid)
        {
            int openCode = Marshal.GetLastPInvokeError();
            ScanErrorKind openKind = openCode switch
            {
                NativeMethods.ERROR_ACCESS_DENIED => ScanErrorKind.AccessDenied,
                NativeMethods.ERROR_FILE_NOT_FOUND or NativeMethods.ERROR_PATH_NOT_FOUND or NativeMethods.ERROR_DIRECTORY => ScanErrorKind.NotFound,
                _ => ScanErrorKind.IoError,
            };
            AddError(builder, c, dirId, openKind, openCode);
            return;
        }

        while (true)
        {
            if (!NativeMethods.GetFileInformationByHandleEx(h, NativeMethods.FileIdBothDirectoryInfo, buffer, BufferSize))
            {
                int code = Marshal.GetLastPInvokeError();
                if (code != NativeMethods.ERROR_NO_MORE_FILES)
                {
                    AddError(builder, c, dirId, ScanErrorKind.IoError, code);
                }
                return;
            }

            byte* e = buffer;
            while (true)
            {
                uint next = Unsafe.ReadUnaligned<uint>(e + DirectoryEntryLayout.NextEntryOffset);
                int nameChars = (int)(Unsafe.ReadUnaligned<uint>(e + DirectoryEntryLayout.FileNameLength) / 2);
                var name = new ReadOnlySpan<char>(e + DirectoryEntryLayout.FileName, nameChars);
                if (!name.SequenceEqual(".") && !name.SequenceEqual(".."))
                {
                    var attrs = (FileAttributes)Unsafe.ReadUnaligned<uint>(e + DirectoryEntryLayout.FileAttributes);
                    bool isDir = (attrs & FileAttributes.Directory) != 0;
                    bool isReparse = (attrs & FileAttributes.ReparsePoint) != 0;
                    uint tag = isReparse ? Unsafe.ReadUnaligned<uint>(e + DirectoryEntryLayout.EaSize) : 0;

                    // Junctions and symlinks are name surrogates: never traversed, which prevents cycles by construction.
                    // Other reparse points (OneDrive cloud placeholders) are real content and are traversed.
                    NodeKind kind = isReparse && (tag & NativeMethods.IO_REPARSE_TAG_NAME_SURROGATE) != 0
                        ? NodeKind.Link
                        : isDir ? NodeKind.Directory : NodeKind.File;

                    long logical = 0;
                    long allocated = 0;
                    if (kind == NodeKind.File)
                    {
                        logical = Unsafe.ReadUnaligned<long>(e + DirectoryEntryLayout.EndOfFile);
                        allocated = Unsafe.ReadUnaligned<long>(e + DirectoryEntryLayout.AllocationSize);
                    }

                    long lastWrite = DirectoryEntryLayout.FileTimeToUtcTicks(Unsafe.ReadUnaligned<long>(e + DirectoryEntryLayout.LastWriteTime));
                    ulong fileRef = (ulong)Unsafe.ReadUnaligned<long>(e + DirectoryEntryLayout.FileId);
                    int id = builder.Add(dirId, name, kind, attrs, logical, allocated, lastWrite, fileRef);
                    c.Nodes++;

                    if (kind == NodeKind.File)
                    {
                        c.Files++;
                        c.Bytes += logical;
                    }
                    else if (kind == NodeKind.Directory)
                    {
                        c.Dirs++;
                        stack.Push((id, Path.Join(dirPath.AsSpan(), name)));
                    }
                }

                if (next == 0)
                {
                    break;
                }

                e += next;
            }
        }
    }

    internal static string ToExtendedPath(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return path;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return @"\\?\UNC\" + path[2..];
        }

        return @"\\?\" + path;
    }

    private static void AddError(NodeIndexBuilder builder, Counters c, int dirId, ScanErrorKind kind, int code)
    {
        builder.AddError(new ScanError(dirId, kind, code, new Win32Exception(code).Message));
        c.Errors++;
    }

    private static void Report(IScanObserver? observer, Counters c, string? dirPath, TimeSpan elapsed) =>
        observer?.OnProgress(new ScanProgress(c.Nodes, c.Files, c.Dirs, c.Bytes, c.Errors, dirPath, elapsed));

    private sealed class Counters
    {
        public long Nodes;
        public long Files;
        public long Dirs;
        public long Bytes;
        public int Errors;
    }
}
