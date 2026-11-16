using System.Globalization;
using System.IO;
using System.Text;

namespace DiskScope.App.Services;

/// <summary>
/// Appends unexpected exceptions to a local text log, so a failure is never silent.
/// Never throws: a log that cannot be written must not turn one error into two.
/// </summary>
public sealed class ErrorLog(string directory)
{
    public const string FileName = "errors.log";

    /// <summary>%LOCALAPPDATA%\DiskScope. Local only: the log may contain paths and is never sent anywhere.</summary>
    public static ErrorLog Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskScope"));

    public string FilePath { get; } = Path.Combine(directory, FileName);

    /// <summary>Writes one entry. Returns the log path, or null when the log itself could not be written.</summary>
    public string? Write(string source, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var sb = new StringBuilder();
            sb.Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))
              .Append(' ').Append(source).AppendLine()
              .AppendLine(exception.ToString())
              .AppendLine();
            File.AppendAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            return FilePath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
