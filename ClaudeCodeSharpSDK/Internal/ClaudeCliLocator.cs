using System.Text.Json;
using ManagedCode.ClaudeCodeSharpSDK.Models;

namespace ManagedCode.ClaudeCodeSharpSDK.Internal;

internal static class ClaudeCliLocator
{
    private const string PathEnvironmentVariable = "PATH";
    private const string NodeModulesDirectory = "node_modules";
    private const string DotBinDirectory = ".bin";
    private const string NpmScopeDirectory = "@anthropic-ai";
    private const string PackageDirectory = "claude-code";
    private const string CliEntryFileName = "cli.js";
    private const string PackageManifestFileName = "package.json";
    private const string NameProperty = "name";
    private const string BinProperty = "bin";
    private const string CommandName = "claude";
    private const string PackageName = NpmScopeDirectory + "/" + PackageDirectory;
    private const string NodeExecutableName = "node";
    private const string NodeWindowsExecutableName = "node.exe";
    private const string GitExecutableName = "git";
    private const string GitWindowsExecutableName = "git.exe";
    private const string NativeWindowsExecutableExtension = ".exe";
    private const string NativeWindowsComExtension = ".com";
    private const string InvalidPackageManifestMessage = "The installed Claude Code package manifest is invalid or unsupported.";
    private const string UnsupportedShimMessage = "The configured Claude Code command shim is unsupported; use a native executable or the official npm package layout.";
    private const string NodeExecutableNotFoundMessage = "Node.js was not found on the effective process PATH for the installed Claude Code JavaScript entrypoint.";
    private const string ExecutableNotFoundMessage = "Claude Code was not found as a launchable executable on the effective process PATH.";
    private const string PackageEntryNotFoundMessage = "The installed Claude Code package entrypoint was not found.";
    private const string CmdScriptExtension = ".cmd";
    private const string BatScriptExtension = ".bat";
    private const string JavaScriptExtension = ".js";

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

    internal static CliLaunchCommand FindClaudeCommand(
        string? overridePath,
        IReadOnlyDictionary<string, string> environment,
        int maximumManifestCharacters)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumManifestCharacters);

        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var selectedPath = ResolveExplicitSelection(overridePath, environment);
            return ResolveConfiguredPath(selectedPath, environment, maximumManifestCharacters);
        }

        if (TryResolveNodeModulesBinary(EnumerateSearchRoots(), OperatingSystem.IsWindows(), out var nodeModulesBinary))
        {
            return ResolveConfiguredPath(nodeModulesBinary, environment, maximumManifestCharacters);
        }

        if (TryResolvePathExecutable(TryGetEnvironmentPath(environment), OperatingSystem.IsWindows(), out var pathExecutable))
        {
            return ResolveConfiguredPath(Path.GetFullPath(pathExecutable), environment, maximumManifestCharacters);
        }

        throw new InvalidOperationException(ExecutableNotFoundMessage);
    }

    private static string ResolveExplicitSelection(string path, IReadOnlyDictionary<string, string> environment)
    {
        if (HasPathComponents(path))
        {
            if (File.Exists(path))
            {
                return Path.GetFullPath(path);
            }

            throw new FileNotFoundException(ExecutableNotFoundMessage, path);
        }

        if (TryResolveNamedExecutable(path, TryGetEnvironmentPath(environment), OperatingSystem.IsWindows(), out var resolved))
        {
            return resolved;
        }

        throw new FileNotFoundException(ExecutableNotFoundMessage, path);
    }

    private static CliLaunchCommand ResolveConfiguredPath(
        string executablePath,
        IReadOnlyDictionary<string, string> environment,
        int maximumManifestCharacters)
    {
        if (!Path.IsPathRooted(executablePath) &&
            !executablePath.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !executablePath.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal) &&
            TryResolveNamedExecutable(executablePath, TryGetEnvironmentPath(environment), OperatingSystem.IsWindows(), out var pathExecutable))
        {
            executablePath = pathExecutable;
        }

        if (!OperatingSystem.IsWindows())
        {
            if (string.Equals(Path.GetExtension(executablePath), JavaScriptExtension, StringComparison.OrdinalIgnoreCase))
            {
                return ResolveJavaScriptEntrypoint(executablePath, environment);
            }

            return ResolveAbsoluteExecutable(executablePath, TryGetEnvironmentPath(environment), false);
        }

        var extension = Path.GetExtension(executablePath);
        if (string.Equals(extension, CmdScriptExtension, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, BatScriptExtension, StringComparison.OrdinalIgnoreCase))
        {
            return ResolveNpmShim(executablePath, environment, maximumManifestCharacters);
        }

        if (string.Equals(extension, JavaScriptExtension, StringComparison.OrdinalIgnoreCase))
        {
            return ResolveJavaScriptEntrypoint(executablePath, environment);
        }

        return ResolveAbsoluteExecutable(executablePath, TryGetEnvironmentPath(environment), true);
    }

    private static CliLaunchCommand ResolveNpmShim(
        string shimPath,
        IReadOnlyDictionary<string, string> environment,
        int maximumManifestCharacters)
    {
        foreach (var packageRoot in EnumeratePackageRoots(shimPath))
        {
            var manifestPath = Path.Combine(packageRoot, PackageManifestFileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            string manifestText;
            try
            {
                manifestText = BoundedMetadataFileReader.ReadAllText(manifestPath, maximumManifestCharacters);
            }
            catch (IOException)
            {
                throw new InvalidOperationException(InvalidPackageManifestMessage);
            }

            JsonDocument manifest;
            try
            {
                manifest = JsonDocument.Parse(manifestText);
            }
            catch (JsonException)
            {
                throw new InvalidOperationException(InvalidPackageManifestMessage);
            }

            using (manifest)
            {
                var root = manifest.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    root.EnumerateObject().Count(property => string.Equals(property.Name, NameProperty, StringComparison.Ordinal)) != 1 ||
                    !root.TryGetProperty(NameProperty, out var name) ||
                    name.ValueKind != JsonValueKind.String ||
                    !string.Equals(name.GetString(), PackageName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryGetPackageEntry(root, out var entrypoint))
                {
                    throw new InvalidOperationException(PackageEntryNotFoundMessage);
                }

                var packagePath = Path.GetFullPath(packageRoot);
                var fullEntrypoint = Path.GetFullPath(Path.Combine(packagePath, entrypoint));
                var relativeEntrypoint = Path.GetRelativePath(packagePath, fullEntrypoint);
                if (Path.IsPathRooted(relativeEntrypoint) ||
                    string.Equals(relativeEntrypoint, "..", StringComparison.Ordinal) ||
                    relativeEntrypoint.StartsWith(string.Concat("..", Path.DirectorySeparatorChar), StringComparison.Ordinal) ||
                    relativeEntrypoint.StartsWith(string.Concat("..", Path.AltDirectorySeparatorChar), StringComparison.Ordinal) ||
                    !File.Exists(fullEntrypoint))
                {
                    continue;
                }

                var resolved = ResolveJavaScriptEntrypointOrExecutable(fullEntrypoint, environment);
                if (OperatingSystem.IsWindows() && !IsSupportedWindowsNativeExecutable(resolved.ExecutablePath))
                {
                    throw new InvalidOperationException(UnsupportedShimMessage);
                }

                return resolved;
            }
        }

        throw new InvalidOperationException(UnsupportedShimMessage);
    }

    private static IEnumerable<string> EnumeratePackageRoots(string shimPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(shimPath));
        if (string.IsNullOrWhiteSpace(directory))
        {
            yield break;
        }

        var current = new DirectoryInfo(directory);
        if (string.Equals(current.Name, DotBinDirectory, StringComparison.OrdinalIgnoreCase) &&
            current.Parent is { Name: NodeModulesDirectory } localModules)
        {
            yield return Path.Combine(localModules.FullName, NpmScopeDirectory, PackageDirectory);
        }

        yield return Path.Combine(directory, NodeModulesDirectory, NpmScopeDirectory, PackageDirectory);
    }

    private static bool TryGetPackageEntry(JsonElement packageManifest, out string entrypoint)
    {
        entrypoint = string.Empty;
        if (packageManifest.ValueKind != JsonValueKind.Object ||
            packageManifest.EnumerateObject().Count(property => string.Equals(property.Name, BinProperty, StringComparison.Ordinal)) != 1 ||
            !packageManifest.TryGetProperty(BinProperty, out var bin))
        {
            return false;
        }

        if (bin.ValueKind == JsonValueKind.String)
        {
            entrypoint = bin.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(entrypoint);
        }

        if (bin.ValueKind == JsonValueKind.Object &&
            bin.EnumerateObject().Count(property => string.Equals(property.Name, CommandName, StringComparison.Ordinal)) == 1 &&
            bin.TryGetProperty(CommandName, out var command) &&
            command.ValueKind == JsonValueKind.String)
        {
            entrypoint = command.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(entrypoint);
        }

        return false;
    }

    private static bool TryResolveNamedExecutable(string name, string? pathVariable, bool isWindows, out string executablePath)
    {
        executablePath = string.Empty;
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return false;
        }

        var candidates = isWindows && string.IsNullOrEmpty(Path.GetExtension(name))
            ? new[] { name, string.Concat(name, NativeWindowsExecutableExtension), string.Concat(name, NativeWindowsComExtension),
                string.Concat(name, CmdScriptExtension), string.Concat(name, BatScriptExtension) }
            : [name];
        foreach (var pathEntry in SplitPathVariable(pathVariable))
        {
            foreach (var candidate in candidates)
            {
                var fullCandidate = Path.Combine(pathEntry, candidate);
                if (File.Exists(fullCandidate))
                {
                    executablePath = Path.GetFullPath(fullCandidate);
                    return true;
                }
            }
        }

        return false;
    }

    private static CliLaunchCommand ResolveJavaScriptEntrypointOrExecutable(
        string entrypoint,
        IReadOnlyDictionary<string, string> environment)
    {
        return string.Equals(Path.GetExtension(entrypoint), JavaScriptExtension, StringComparison.OrdinalIgnoreCase)
            ? ResolveJavaScriptEntrypoint(entrypoint, environment)
            : new CliLaunchCommand(entrypoint, []);
    }

    private static CliLaunchCommand ResolveJavaScriptEntrypoint(
        string entrypoint,
        IReadOnlyDictionary<string, string> environment)
    {
        var nodeExecutable = FindNodeExecutable(TryGetEnvironmentPath(environment));
        return new CliLaunchCommand(Path.GetFullPath(nodeExecutable), [Path.GetFullPath(entrypoint)]);
    }

    internal static CliLaunchCommand ResolveRequiredExecutable(
        string executableName,
        IReadOnlyDictionary<string, string> environment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);
        ArgumentNullException.ThrowIfNull(environment);
        return ResolveAbsoluteExecutable(executableName, TryGetEnvironmentPath(environment), OperatingSystem.IsWindows());
    }

    private static CliLaunchCommand ResolveAbsoluteExecutable(string path, string? pathVariable, bool isWindows)
    {
        if (File.Exists(path))
        {
            return new CliLaunchCommand(Path.GetFullPath(path), []);
        }

        var hasDirectory = HasPathComponents(path);
        if (!hasDirectory && !string.IsNullOrWhiteSpace(pathVariable))
        {
            foreach (var pathEntry in SplitPathVariable(pathVariable))
            {
                var candidate = Path.Combine(pathEntry, path);
                if (File.Exists(candidate))
                {
                    return new CliLaunchCommand(Path.GetFullPath(candidate), []);
                }
            }
        }

        if (hasDirectory)
        {
            throw new FileNotFoundException(ExecutableNotFoundMessage, path);
        }

        if (isWindows && !IsSupportedWindowsNativeExecutable(path))
        {
            throw new InvalidOperationException(UnsupportedShimMessage);
        }

        throw new FileNotFoundException(ExecutableNotFoundMessage, path);
    }

    private static bool HasPathComponents(string path)
    {
        return Path.IsPathRooted(path) ||
               path.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
               path.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool IsSupportedWindowsNativeExecutable(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, NativeWindowsExecutableExtension, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, NativeWindowsComExtension, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindNodeExecutable(string? pathVariable)
    {
        if (!string.IsNullOrWhiteSpace(pathVariable))
        {
            foreach (var pathEntry in SplitPathVariable(pathVariable))
            {
                var nodeName = OperatingSystem.IsWindows() ? NodeWindowsExecutableName : NodeExecutableName;
                var candidate = Path.Combine(pathEntry, nodeName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        throw new InvalidOperationException(NodeExecutableNotFoundMessage);
    }

    internal static CliLaunchCommand ResolveGitCommand(IReadOnlyDictionary<string, string> environment) =>
        ResolveAbsoluteExecutable(OperatingSystem.IsWindows() ? GitWindowsExecutableName : GitExecutableName,
            TryGetEnvironmentPath(environment), OperatingSystem.IsWindows());

    private static string? TryGetEnvironmentPath(IReadOnlyDictionary<string, string> environment)
    {
        foreach (var (key, value) in environment)
        {
            if (string.Equals(key, PathEnvironmentVariable, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
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
