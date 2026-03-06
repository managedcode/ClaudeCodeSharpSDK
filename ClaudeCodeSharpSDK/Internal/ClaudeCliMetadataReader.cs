using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ManagedCode.ClaudeCodeSharpSDK.Models;

namespace ManagedCode.ClaudeCodeSharpSDK.Internal;

internal static class ClaudeCliMetadataReader
{
    private static readonly string[] VersionSplitTokens = [Environment.NewLine, NewLine, CarriageReturn, Tab, Space];
    private static readonly string[] LineSplitTokens = [Environment.NewLine, NewLine, CarriageReturn];

    private const string Space = " ";
    private const string Tab = "\t";
    private const string NewLine = "\n";
    private const string CarriageReturn = "\r";
    private const string GitTagPrefix = "refs/tags/v";
    private const string ExecutableExtension = ".exe";
    private const string StartGitProcessFailedMessage = "Failed to start git process.";
    private const string VersionOutputEmptyMessage = "Claude Code version output is empty.";
    private const string VersionOutputParseFailedMessagePrefix = "Failed to parse Claude Code version output:";
    private const string StartExecutableFailedMessagePrefix = "Failed to start Claude Code executable";
    private const string ReadVersionFailedMessagePrefix = "Claude Code CLI exited with code";
    private const string ReadVersionFailedMessageMiddle = "while reading version.";
    private const string UpdateInstalledVersionSegment = "installed ";
    private const string UpdateLatestVersionSegment = ", latest ";
    private const string UpdateRunCommandSegment = ". Run ";
    private const string VersionSeparator = ".";
    private const string PreReleaseSeparator = "-";
    private const string MessageQuote = "'";
    private const string MessageSuffix = ".";

    private const string VersionFlag = "--version";
    private const string UpdateCommand = "claude update";
    private const string UpdateAvailableMessagePrefix = "Claude Code update is available:";
    private const string UpdateCheckFailedMessagePrefix = "Failed to check latest Claude Code version from GitHub:";

    private const string GitExecutableName = "git";
    private const string GitLsRemoteCommand = "ls-remote";
    private const string GitTagsArgument = "--tags";
    private const string GitRefsArgument = "--refs";
    private const string RepositoryUrl = "https://github.com/anthropics/claude-code.git";

    private const string ClaudeConfigDirEnvironmentVariable = "CLAUDE_CONFIG_DIR";
    private const string ClaudeConfigDirectoryName = ".claude";
    private const string SettingsFileName = "settings.json";
    private const string SettingsLocalFileName = "settings.local.json";
    private const string ModelPropertyName = "model";
    private const string FallbackDefaultModel = ClaudeModels.Sonnet;

    public static ClaudeCliMetadata Read(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        var installedVersion = ReadInstalledVersion(executablePath);
        return new ClaudeCliMetadata(installedVersion, ReadDefaultModel(), ClaudeModels.Known);
    }

    public static ClaudeCliUpdateStatus ReadUpdateStatus(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        var installedVersion = ReadInstalledVersion(executablePath);
        var probe = ProbeLatestPublishedVersion();

        if (!string.IsNullOrWhiteSpace(probe.ErrorMessage))
        {
            return new ClaudeCliUpdateStatus(
                installedVersion,
                null,
                false,
                string.Concat(UpdateCheckFailedMessagePrefix, Space, probe.ErrorMessage),
                null);
        }

        if (string.IsNullOrWhiteSpace(probe.LatestVersion))
        {
            return new ClaudeCliUpdateStatus(installedVersion, null, false, null, null);
        }

        if (!IsNewerVersion(probe.LatestVersion, installedVersion))
        {
            return new ClaudeCliUpdateStatus(installedVersion, probe.LatestVersion, false, null, null);
        }

        return new ClaudeCliUpdateStatus(
            installedVersion,
            probe.LatestVersion,
            true,
            string.Concat(
                UpdateAvailableMessagePrefix,
                Space,
                UpdateInstalledVersionSegment,
                installedVersion,
                UpdateLatestVersionSegment,
                probe.LatestVersion,
                UpdateRunCommandSegment,
                MessageQuote,
                UpdateCommand,
                MessageQuote,
                MessageSuffix),
            UpdateCommand);
    }

    internal static string ParseInstalledVersion(string versionOutput)
    {
        if (string.IsNullOrWhiteSpace(versionOutput))
        {
            throw new InvalidOperationException(VersionOutputEmptyMessage);
        }

        var firstToken = versionOutput.Trim()
            .Split(VersionSplitTokens, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstToken))
        {
            throw new InvalidOperationException(
                string.Concat(VersionOutputParseFailedMessagePrefix, Space, MessageQuote, versionOutput, MessageQuote, MessageSuffix));
        }

        return firstToken;
    }

    internal static string? ParseLatestPublishedVersion(string gitOutput)
    {
        if (string.IsNullOrWhiteSpace(gitOutput))
        {
            return null;
        }

        SemanticVersion? best = null;
        foreach (var rawLine in gitOutput.Split(LineSplitTokens, StringSplitOptions.RemoveEmptyEntries))
        {
            var tagToken = rawLine.Split('\t', ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault();
            if (string.IsNullOrWhiteSpace(tagToken))
            {
                continue;
            }

            if (!tagToken.StartsWith(GitTagPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var versionText = tagToken[GitTagPrefix.Length..];
            if (!TryParseSemanticVersion(versionText, out var candidate))
            {
                continue;
            }

            if (best is null || CompareSemanticVersion(candidate, best.Value) > 0)
            {
                best = candidate;
            }
        }

        return best?.ToNormalizedString();
    }

    internal static bool IsNewerVersion(string latestVersion, string installedVersion)
    {
        if (!TryParseSemanticVersion(latestVersion, out var latest)
            || !TryParseSemanticVersion(installedVersion, out var installed))
        {
            return false;
        }

        return CompareSemanticVersion(latest, installed) > 0;
    }

    internal static string? ParseDefaultModelFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty(ModelPropertyName, out var modelElement)
            || modelElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return modelElement.GetString();
    }

    internal static bool TryParseSemanticVersion(string value, out SemanticVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var plusIndex = value.IndexOf('+');
        var withoutBuildMetadata = plusIndex >= 0
            ? value[..plusIndex]
            : value;

        var dashIndex = withoutBuildMetadata.IndexOf('-');
        var numericPortion = dashIndex >= 0
            ? withoutBuildMetadata[..dashIndex]
            : withoutBuildMetadata;
        var preReleasePortion = dashIndex >= 0
            ? withoutBuildMetadata[(dashIndex + 1)..]
            : null;

        var segments = numericPortion.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length is < 1 or > 3)
        {
            return false;
        }

        if (!int.TryParse(segments[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major))
        {
            return false;
        }

        var minor = 0;
        if (segments.Length >= 2 && !int.TryParse(segments[1], NumberStyles.None, CultureInfo.InvariantCulture, out minor))
        {
            return false;
        }

        var patch = 0;
        if (segments.Length == 3 && !int.TryParse(segments[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, preReleasePortion);
        return true;
    }

    internal static int CompareSemanticVersion(SemanticVersion left, SemanticVersion right)
    {
        var majorComparison = left.Major.CompareTo(right.Major);
        if (majorComparison != 0)
        {
            return majorComparison;
        }

        var minorComparison = left.Minor.CompareTo(right.Minor);
        if (minorComparison != 0)
        {
            return minorComparison;
        }

        var patchComparison = left.Patch.CompareTo(right.Patch);
        if (patchComparison != 0)
        {
            return patchComparison;
        }

        if (string.IsNullOrWhiteSpace(left.PreRelease) && string.IsNullOrWhiteSpace(right.PreRelease))
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(left.PreRelease))
        {
            return 1;
        }

        if (string.IsNullOrWhiteSpace(right.PreRelease))
        {
            return -1;
        }

        return ComparePreRelease(left.PreRelease, right.PreRelease);
    }

    private static int ComparePreRelease(string left, string right)
    {
        var leftIdentifiers = left.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rightIdentifiers = right.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var count = Math.Min(leftIdentifiers.Length, rightIdentifiers.Length);

        for (var index = 0; index < count; index += 1)
        {
            var comparison = ComparePreReleaseIdentifier(leftIdentifiers[index], rightIdentifiers[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return leftIdentifiers.Length.CompareTo(rightIdentifiers.Length);
    }

    private static int ComparePreReleaseIdentifier(string left, string right)
    {
        var leftIsNumeric = ulong.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
        var rightIsNumeric = ulong.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);

        if (leftIsNumeric && rightIsNumeric)
        {
            return leftNumber.CompareTo(rightNumber);
        }

        if (leftIsNumeric)
        {
            return -1;
        }

        if (rightIsNumeric)
        {
            return 1;
        }

        return string.CompareOrdinal(left, right);
    }

    private static string ReadInstalledVersion(string executablePath)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(VersionFlag);

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException(
                                string.Concat(StartExecutableFailedMessagePrefix, Space, MessageQuote, executablePath, MessageQuote, MessageSuffix));

        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var details = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
            throw new InvalidOperationException(
                string.Concat(
                    ReadVersionFailedMessagePrefix,
                    Space,
                    process.ExitCode.ToString(CultureInfo.InvariantCulture),
                    Space,
                    ReadVersionFailedMessageMiddle,
                    Space,
                    details).Trim());
        }

        return ParseInstalledVersion(standardOutput);
    }

    private static (string? LatestVersion, string? ErrorMessage) ProbeLatestPublishedVersion()
    {
        try
        {
            var gitExecutable = OperatingSystem.IsWindows() ? string.Concat(GitExecutableName, ExecutableExtension) : GitExecutableName;
            var startInfo = new ProcessStartInfo(gitExecutable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            startInfo.ArgumentList.Add(GitLsRemoteCommand);
            startInfo.ArgumentList.Add(GitTagsArgument);
            startInfo.ArgumentList.Add(GitRefsArgument);
            startInfo.ArgumentList.Add(RepositoryUrl);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return (null, StartGitProcessFailedMessage);
            }

            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                return (null, string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError);
            }

            return (ParseLatestPublishedVersion(standardOutput), null);
        }
        catch (Exception exception)
        {
            return (null, exception.Message);
        }
    }

    private static string ReadDefaultModel()
    {
        foreach (var settingsPath in EnumerateSettingsFiles())
        {
            if (!File.Exists(settingsPath))
            {
                continue;
            }

            try
            {
                var parsed = ParseDefaultModelFromJson(File.ReadAllText(settingsPath));
                if (!string.IsNullOrWhiteSpace(parsed))
                {
                    return parsed!;
                }
            }
            catch (JsonException)
            {
                // Ignore invalid settings files and continue.
            }
        }

        return FallbackDefaultModel;
    }

    private static IEnumerable<string> EnumerateSettingsFiles()
    {
        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current is not null)
        {
            yield return Path.Combine(current.FullName, ClaudeConfigDirectoryName, SettingsLocalFileName);
            yield return Path.Combine(current.FullName, ClaudeConfigDirectoryName, SettingsFileName);
            current = current.Parent;
        }

        var configRoot = Environment.GetEnvironmentVariable(ClaudeConfigDirEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configRoot))
        {
            var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(homeDirectory))
            {
                configRoot = Path.Combine(homeDirectory, ClaudeConfigDirectoryName);
            }
        }

        if (!string.IsNullOrWhiteSpace(configRoot))
        {
            yield return Path.Combine(configRoot, SettingsFileName);
        }
    }

    internal readonly record struct SemanticVersion(
        int Major,
        int Minor,
        int Patch,
        string? PreRelease)
    {
        public string ToNormalizedString()
        {
            var version = string.Concat(
                Major.ToString(CultureInfo.InvariantCulture),
                VersionSeparator,
                Minor.ToString(CultureInfo.InvariantCulture),
                VersionSeparator,
                Patch.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(PreRelease))
            {
                version += string.Concat(PreReleaseSeparator, PreRelease);
            }

            return version;
        }
    }
}
