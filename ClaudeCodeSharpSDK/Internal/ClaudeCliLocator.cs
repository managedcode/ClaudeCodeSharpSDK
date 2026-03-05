namespace ManagedCode.ClaudeCodeSharpSDK.Internal;

internal static class ClaudeCliLocator
{
    private const string PathEnvironmentVariable = "PATH";
    private const string NodeModulesDirectory = "node_modules";
    private const string DotBinDirectory = ".bin";
    private const string NpmScopeDirectory = "@anthropic-ai";
    private const string PackageDirectory = "claude-code";
    private const string CliEntryFileName = "cli.js";
    private const string CmdScriptExtension = ".cmd";
    private const string BatScriptExtension = ".bat";

    internal const string ClaudeExecutableName = "claude";
    internal const string ClaudeWindowsExecutableName = "claude.exe";
    internal const string ClaudeWindowsCommandName = ClaudeExecutableName + CmdScriptExtension;
    internal const string ClaudeWindowsBatchName = ClaudeExecutableName + BatScriptExtension;

    private static readonly string[] WindowsPathExecutableCandidates =
    [
        ClaudeWindowsExecutableName,
        ClaudeWindowsCommandName,
        ClaudeWindowsBatchName,
        ClaudeExecutableName,
    ];

    private static readonly string[] UnixPathExecutableCandidates =
    [
        ClaudeExecutableName,
    ];

    public static string FindClaudePath(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        if (TryResolveNodeModulesBinary(EnumerateSearchRoots(), OperatingSystem.IsWindows(), out var nodeModulesBinary))
        {
            return nodeModulesBinary;
        }

        if (TryResolvePathExecutable(Environment.GetEnvironmentVariable(PathEnvironmentVariable), OperatingSystem.IsWindows(), out var pathExecutable))
        {
            return pathExecutable;
        }

        return OperatingSystem.IsWindows()
            ? ClaudeWindowsExecutableName
            : ClaudeExecutableName;
    }

    internal static bool TryResolvePathExecutable(string? pathVariable, bool isWindows, out string executablePath)
    {
        executablePath = string.Empty;

        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return false;
        }

        foreach (var pathEntry in SplitPathVariable(pathVariable))
        {
            foreach (var candidateName in GetPathExecutableCandidates(isWindows))
            {
                var candidatePath = Path.Combine(pathEntry, candidateName);
                if (File.Exists(candidatePath))
                {
                    executablePath = candidatePath;
                    return true;
                }
            }
        }

        return false;
    }

    internal static IReadOnlyList<string> GetPathExecutableCandidates(bool isWindows)
    {
        return isWindows
            ? WindowsPathExecutableCandidates
            : UnixPathExecutableCandidates;
    }

    internal static bool TryResolveNodeModulesBinary(
        IEnumerable<string> searchRoots,
        bool isWindows,
        out string executablePath)
    {
        executablePath = string.Empty;

        foreach (var root in searchRoots)
        {
            var dotBinCandidate = Path.Combine(
                root,
                NodeModulesDirectory,
                DotBinDirectory,
                isWindows ? ClaudeWindowsCommandName : ClaudeExecutableName);

            if (File.Exists(dotBinCandidate))
            {
                executablePath = dotBinCandidate;
                return true;
            }

            if (isWindows)
            {
                continue;
            }

            var packageCliCandidate = Path.Combine(
                root,
                NodeModulesDirectory,
                NpmScopeDirectory,
                PackageDirectory,
                CliEntryFileName);

            if (File.Exists(packageCliCandidate))
            {
                executablePath = packageCliCandidate;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> SplitPathVariable(string pathVariable)
    {
        foreach (var rawPathEntry in pathVariable.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var trimmedPathEntry = rawPathEntry.Trim('"');
            if (!string.IsNullOrWhiteSpace(trimmedPathEntry))
            {
                yield return trimmedPathEntry;
            }
        }
    }

    private static IEnumerable<string> EnumerateSearchRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in EnumerateUpwards(Environment.CurrentDirectory))
        {
            if (seen.Add(root))
            {
                yield return root;
            }
        }

        foreach (var root in EnumerateUpwards(AppContext.BaseDirectory))
        {
            if (seen.Add(root))
            {
                yield return root;
            }
        }
    }

    private static IEnumerable<string> EnumerateUpwards(string startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            yield break;
        }

        var current = new DirectoryInfo(startPath);
        while (current is not null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }
}
