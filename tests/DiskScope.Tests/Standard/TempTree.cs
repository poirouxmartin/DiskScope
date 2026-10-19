using System.Diagnostics;

namespace DiskScope.Tests.Standard;

/// <summary>
/// Temporary directory tree for scan tests. Junctions are removed first, without following them.
/// </summary>
public sealed class TempTree : IDisposable
{
    private readonly List<string> _junctions = new();

    public TempTree()
    {
        Root = Path.Combine(Path.GetTempPath(), "DiskScopeTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    /// <summary>Creates a file of exactly <paramref name="sizeBytes"/> zero bytes and returns its full path.</summary>
    public string File(string relPath, int sizeBytes)
    {
        string path = Path.Combine(Root, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, new byte[sizeBytes]);
        return path;
    }

    /// <summary>Creates a directory and returns its full path.</summary>
    public string Dir(string relPath)
    {
        string path = Path.Combine(Root, relPath);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Creates a junction at <paramref name="relLink"/> pointing to the absolute path <paramref name="target"/>.</summary>
    public string Junction(string relLink, string target)
    {
        string link = Path.Combine(Root, relLink);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        using (Process process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!)
        {
            process.WaitForExit();
        }

        Assert.True(Directory.Exists(link), $"mklink /J failed for {link}");
        _junctions.Add(link);
        return link;
    }

    public void Dispose()
    {
        foreach (string junction in _junctions)
        {
            try
            {
                Directory.Delete(junction);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        try
        {
            Directory.Delete(Root, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
