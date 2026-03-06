using System.Diagnostics;
using System.Text.Json;
using ManagedCode.ClaudeCodeSharpSDK.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Tests.Shared;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Integration;

public class ClaudeCliSmokeTests
{
    private const string SandboxPrefix = "ClaudeCliSmokeTests-";
    private const string HomeEnvironmentVariable = "HOME";
    private const string UserProfileEnvironmentVariable = "USERPROFILE";
    private const string XdgConfigHomeEnvironmentVariable = "XDG_CONFIG_HOME";
    private const string AppDataEnvironmentVariable = "APPDATA";
    private const string LocalAppDataEnvironmentVariable = "LOCALAPPDATA";
    private const string ClaudeConfigDirEnvironmentVariable = "CLAUDE_CONFIG_DIR";
    private const string AnthropicApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY";
    private const string AnthropicBaseUrlEnvironmentVariable = "ANTHROPIC_BASE_URL";
    private const string VersionFlag = "--version";
    private const string HelpFlag = "--help";
    private const string PrintFlag = "-p";
    private const string OutputFormatFlag = "--output-format";
    private const string StreamJsonFormat = "stream-json";
    private const string VerboseFlag = "--verbose";
    private const string ReplyWithOkPrompt = "Reply with ok";
    private const string ClaudeCodeFragment = "Claude Code";
    private const string LoginGuidanceFragment = "Please run /login";
    private const string ErrorJsonFragment = "\"is_error\":true";
    private const string TypePropertyName = "type";
    private const string SystemType = "system";
    private const string ResultType = "result";
    private const string ClaudeConfigDirectoryName = ".claude";
    private const string ConfigDirectoryName = ".config";
    private const string NewLine = "\n";
    private const string CarriageReturn = "\r";
    private const string PathRootedButMissingMessagePrefix = "Claude Code CLI path is rooted but missing:";
    private const string FailedToResolveExecutablePathMessage = "Failed to resolve Claude Code CLI path.";
    private const string CouldNotLocateRepositoryRootMessage = "Could not locate repository root from test execution directory.";
    private const string StartProcessFailedMessagePrefix = "Failed to start Claude Code CLI at";
    private const string Space = " ";
    private const string MessageQuote = "'";
    private const string MessageSuffix = ".";
    private static readonly string[] StandardLineSeparators = [Environment.NewLine, NewLine, CarriageReturn];

    [Test]
    public async Task ClaudeCli_Smoke_FindExecutablePath_ResolvesExistingBinary()
    {
        var executablePath = ResolveExecutablePath();
        await Assert.That(File.Exists(executablePath)).IsTrue();
    }

    [Test]
    public async Task ClaudeCli_Smoke_VersionCommand_ReturnsClaudeCodeVersion()
    {
        var result = await RunClaudeAsync(ResolveExecutablePath(), null, VersionFlag);

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(string.Concat(result.StandardOutput, result.StandardError))
            .Contains(ClaudeCodeFragment);
    }

    [Test]
    public async Task ClaudeCli_Smoke_HelpCommand_DescribesStreamJsonOutput()
    {
        var result = await RunClaudeAsync(ResolveExecutablePath(), null, HelpFlag);

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(string.Concat(result.StandardOutput, result.StandardError))
            .Contains(OutputFormatFlag);
    }

    [Test]
    public async Task ClaudeCli_Smoke_PrintModeWithoutAuth_EmitsInitAndLoginGuidance()
    {
        var sandboxDirectory = CreateSandboxDirectory();

        try
        {
            var result = await RunClaudeAsync(
                ResolveExecutablePath(),
                CreateUnauthenticatedEnvironmentOverrides(sandboxDirectory),
                PrintFlag,
                OutputFormatFlag,
                StreamJsonFormat,
                VerboseFlag,
                ReplyWithOkPrompt);

            var stdoutLines = result.StandardOutput
                .Split(StandardLineSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            await Assert.That(stdoutLines.Length).IsGreaterThanOrEqualTo(2);
            await Assert.That(result.StandardOutput).Contains(LoginGuidanceFragment);
            await Assert.That(result.StandardOutput).Contains(ErrorJsonFragment);

            using var initDocument = JsonDocument.Parse(stdoutLines[0]);
            using var finalDocument = JsonDocument.Parse(stdoutLines[^1]);

            await Assert.That(initDocument.RootElement.GetProperty(TypePropertyName).GetString()).IsEqualTo(SystemType);
            await Assert.That(finalDocument.RootElement.GetProperty(TypePropertyName).GetString()).IsEqualTo(ResultType);
        }
        finally
        {
            Directory.Delete(sandboxDirectory, recursive: true);
        }
    }

    private static string ResolveExecutablePath()
    {
        var resolvedPath = ClaudeCliLocator.FindClaudePath(null);
        if (Path.IsPathRooted(resolvedPath))
        {
            if (File.Exists(resolvedPath))
            {
                return resolvedPath;
            }

            throw new InvalidOperationException(
                string.Concat(
                    PathRootedButMissingMessagePrefix,
                    Space,
                    MessageQuote,
                    resolvedPath,
                    MessageQuote,
                    MessageSuffix));
        }

        if (ClaudeCliLocator.TryResolvePathExecutable(
                Environment.GetEnvironmentVariable(TestConstants.PathEnvironmentVariable),
                OperatingSystem.IsWindows(),
                out var pathExecutable))
        {
            return pathExecutable;
        }

        throw new InvalidOperationException(FailedToResolveExecutablePathMessage);
    }

    private static string CreateSandboxDirectory()
    {
        var sandboxDirectory = Path.Combine(
            ResolveRepositoryRootPath(),
            TestConstants.TestsDirectoryName,
            TestConstants.SandboxDirectoryName,
            string.Concat(SandboxPrefix, Guid.NewGuid().ToString(TestConstants.NumericGuidFormat)));
        Directory.CreateDirectory(sandboxDirectory);
        return sandboxDirectory;
    }

    private static Dictionary<string, string> CreateUnauthenticatedEnvironmentOverrides(string sandboxDirectory)
    {
        var claudeConfigDirectory = Path.Combine(sandboxDirectory, ClaudeConfigDirectoryName);
        var configHome = Path.Combine(sandboxDirectory, ConfigDirectoryName);
        var appData = Path.Combine(sandboxDirectory, TestConstants.AppDataDirectoryName, TestConstants.RoamingDirectoryName);
        var localAppData = Path.Combine(sandboxDirectory, TestConstants.AppDataDirectoryName, TestConstants.LocalDirectoryName);

        Directory.CreateDirectory(claudeConfigDirectory);
        Directory.CreateDirectory(configHome);
        Directory.CreateDirectory(appData);
        Directory.CreateDirectory(localAppData);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [HomeEnvironmentVariable] = sandboxDirectory,
            [UserProfileEnvironmentVariable] = sandboxDirectory,
            [XdgConfigHomeEnvironmentVariable] = configHome,
            [AppDataEnvironmentVariable] = appData,
            [LocalAppDataEnvironmentVariable] = localAppData,
            [ClaudeConfigDirEnvironmentVariable] = claudeConfigDirectory,
            [AnthropicApiKeyEnvironmentVariable] = TestConstants.EmptyString,
            [AnthropicBaseUrlEnvironmentVariable] = TestConstants.EmptyString,
        };
    }

    private static string ResolveRepositoryRootPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, TestConstants.SolutionFileName)))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(CouldNotLocateRepositoryRootMessage);
    }

    private static async Task<ClaudeProcessResult> RunClaudeAsync(
        string executablePath,
        IReadOnlyDictionary<string, string>? environmentOverrides,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environmentOverrides is not null)
        {
            foreach (var (key, value) in environmentOverrides)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException(
                string.Concat(
                    StartProcessFailedMessagePrefix,
                    Space,
                    MessageQuote,
                    executablePath,
                    MessageQuote,
                    MessageSuffix));
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return new ClaudeProcessResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    private sealed record ClaudeProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
