using System.Globalization;

namespace DiskScope.Benchmarks;

internal static class Program
{
    private const int ExitFailed = 1;
    private const int ExitUsage = 2;

    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            return Usage(null);
        }

        try
        {
            return args[0] switch
            {
                "scan" => RunScan(args),
                "compare" => RunCompare(args),
                "synthetic" => RunSynthetic(args),
                "generate" => RunGenerate(args),
                "monitor" => args.Length == 1
                    ? MonitorBench.Run(60)
                    : args.Length == 3 && args[1] == "--seconds" && int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) && s >= 5
                        ? MonitorBench.Run(s)
                        : Usage("monitor takes [--seconds N>=5]."),
                "usn-probe" => args.Length == 2 && args[1].Length >= 1 && char.IsAsciiLetter(args[1][0])
                    ? UsnProbe.Run(char.ToUpperInvariant(args[1][0]))
                    : Usage("usn-probe takes exactly one drive letter."),
                _ => Usage($"Unknown command '{args[0]}'."),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"I/O error: {ex.Message}");
            return ExitFailed;
        }
    }

    private static int RunScan(string[] args)
    {
        if (args.Length < 2)
        {
            return Usage("scan needs <path>.");
        }

        string path = Path.GetFullPath(args[1]);
        if (!Directory.Exists(path))
        {
            return Usage($"Path not found: {path}");
        }

        int runs = 5;
        int warmup = 1;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--runs")
            {
                if (!TryReadInt(args, ref i, 1, out runs))
                {
                    return Usage("--runs needs an integer >= 1.");
                }
            }
            else if (args[i] == "--warmup")
            {
                if (!TryReadInt(args, ref i, 0, out warmup))
                {
                    return Usage("--warmup needs an integer >= 0.");
                }
            }
            else
            {
                return Usage($"Unknown argument '{args[i]}'.");
            }
        }

        return Commands.Scan(path, runs, warmup);
    }

    private static int RunCompare(string[] args)
    {
        if (args.Length != 2)
        {
            return Usage("compare takes exactly one <path>.");
        }

        string path = Path.GetFullPath(args[1]);
        if (!Directory.Exists(path))
        {
            return Usage($"Path not found: {path}");
        }

        return Commands.Compare(path);
    }

    private static int RunSynthetic(string[] args)
    {
        int nodes = 1_000_000;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--nodes")
            {
                if (!TryReadInt(args, ref i, 1, out nodes))
                {
                    return Usage("--nodes needs an integer >= 1.");
                }
            }
            else
            {
                return Usage($"Unknown argument '{args[i]}'.");
            }
        }

        return Commands.Synthetic(nodes);
    }

    private static int RunGenerate(string[] args)
    {
        if (args.Length < 2)
        {
            return Usage("generate needs <dir>.");
        }

        string root = Path.GetFullPath(args[1]);
        int files = 200_000;
        int seed = 1;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--files")
            {
                if (!TryReadInt(args, ref i, 1, out files))
                {
                    return Usage("--files needs an integer >= 1.");
                }
            }
            else if (args[i] == "--seed")
            {
                if (!TryReadInt(args, ref i, int.MinValue, out seed))
                {
                    return Usage("--seed needs an integer.");
                }
            }
            else
            {
                return Usage($"Unknown argument '{args[i]}'.");
            }
        }

        return Commands.Generate(root, files, seed);
    }

    private static bool TryReadInt(string[] args, ref int index, int minimum, out int value)
    {
        value = 0;
        if (index + 1 >= args.Length)
        {
            return false;
        }

        if (!int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < minimum)
        {
            return false;
        }

        index++;
        return true;
    }

    private static int Usage(string? error)
    {
        if (error is not null)
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
        }

        Console.Error.WriteLine("Usage: DiskScope.Benchmarks <command> [options]");
        Console.Error.WriteLine("  scan <path> [--runs N=5] [--warmup N=1]");
        Console.Error.WriteLine("      Timed runs of StandardScanEngine, release build recommended. Warmups are tagged.");
        Console.Error.WriteLine("  compare <path>");
        Console.Error.WriteLine("      Engine file count and logical bytes vs a manual directory walk. Correctness check.");
        Console.Error.WriteLine("  synthetic [--nodes N=1000000]");
        Console.Error.WriteLine("      NodeIndexBuilder Add, Build and allocation cost on a synthetic tree.");
        Console.Error.WriteLine("  generate <dir> [--files N=200000] [--seed S=1]");
        Console.Error.WriteLine("      Deterministic test tree (1110 directories, 8 GiB logical cap). Refuses a non-empty <dir>.");
        Console.Error.WriteLine("  monitor [--seconds N=60]");
        Console.Error.WriteLine("      Cost of every collector, the process refresh, history and alerts, without UI.");
        Console.Error.WriteLine("  usn-probe <drive letter>");
        Console.Error.WriteLine("      FSCTL_ENUM_USN_DATA enumeration and tree rebuild on a whole NTFS volume. Needs an elevated console.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Exit codes: 0 ok; 1 scan not completed, compare mismatch, or I/O failure;");
        Console.Error.WriteLine("            2 usage or invalid argument; 3 refused by precondition (non-empty target, low free space).");
        return ExitUsage;
    }
}
