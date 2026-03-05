using System.Diagnostics;
using System.Text.Json;
using ManagedCode.ClaudeCodeSharpSDK.Internal;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Integration;

public class ClaudeCliSmokeTests
{
    private const string SolutionFileName = "ManagedCode.ClaudeCodeSharpSDK.slnx";
    private const string SandboxDirectoryName = ".sandbox";
    private const string SandboxPrefix = "ClaudeCliSmokeTests-";
    private const string HomeEnvironmentVariable = "HOME";
    private const string UserProfileEnvironmentVariable = "USERPROFILE";
    private const string XdgConfigHomeEnvironmentVariable = "XDG_CONFIG_HOME";
    private const string AppDataEnvironmentVariable = "APPDATA";
    private const string LocalAppDataEnvironmentVariable = "LOCALAPPDATA";
    private const string ClaudeConfigDirEnvironmentVariable = "CLAUDE_CONFIG_DIR";
    private const string AnthropicApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY";
    private const string AnthropicBaseUrlEnvironmentVariable = "ANTHROPIC_BASE_URL";

    [Test]
    public async Task ClaudeCli_Smoke_FindExecutablePath_ResolvesExistingBinary()
    {
        var executablePath = ResolveExecutablePath();
        await Assert.That(File.Exists(executablePath)).IsTrue();
    }

    [Test]
    public async Task ClaudeCli_Smoke_VersionCommand_ReturnsClaudeCodeVersion()
    {
        var result = await RunClaudeAsync(ResolveExecutablePath(), null, "--version");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(string.Concat(result.StandardOutput, result.StandardError))
            .Contains("Claude Code");
    }

    [Test]
    public async Task ClaudeCli_Smoke_HelpCommand_DescribesStreamJsonOutput()
    {
        var result = await RunClaudeAsync(ResolveExecutablePath(), null, "--help");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(string.Concat(result.StandardOutput, result.StandardError))
            .Contains("--output-format");
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
                "-p",
                "--output-format",
                "stream-json",
                "--verbose",
                "Reply with ok");

            var stdoutLines = result.StandardOutput
                .Split([Environment.NewLine, "\n", "\r"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            await Assert.That(stdoutLines.Length).IsGreaterThanOrEqualTo(2);
            await Assert.That(result.StandardOutput).Contains("Please run /login");
            await Assert.That(result.StandardOutput).Contains("\"is_error\":true");

            using var initDocument = JsonDocument.Parse(stdoutLines[0]);
            using var finalDocument = JsonDocument.Parse(stdoutLines[^1]);

            await Assert.That(initDocument.RootElement.GetProperty("type").GetString()).IsEqualTo("system");
            await Assert.That(finalDocument.RootElement.GetProperty("type").GetString()).IsEqualTo("result");
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

            throw new InvalidOperationException($"Claude Code CLI path is rooted but missing: '{resolvedPath}'.");
        }

        if (ClaudeCliLocator.TryResolvePathExecutable(
                Environment.GetEnvironmentVariable("PATH"),
                OperatingSystem.IsWindows(),
                out var pathExecutable))
        {
            return pathExecutable;
        }

        throw new InvalidOperationException("Failed to resolve Claude Code CLI path.");
    }

    private static string CreateSandboxDirectory()
    {
        var sandboxDirectory = Path.Combine(
            ResolveRepositoryRootPath(),
            "tests",
            SandboxDirectoryName,
            $"{SandboxPrefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(sandboxDirectory);
        return sandboxDirectory;
    }

    private static Dictionary<string, string> CreateUnauthenticatedEnvironmentOverrides(string sandboxDirectory)
    {
        var claudeConfigDirectory = Path.Combine(sandboxDirectory, ".claude");
        var configHome = Path.Combine(sandboxDirectory, ".config");
        var appData = Path.Combine(sandboxDirectory, "AppData", "Roaming");
        var localAppData = Path.Combine(sandboxDirectory, "AppData", "Local");

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
            [AnthropicApiKeyEnvironmentVariable] = string.Empty,
            [AnthropicBaseUrlEnvironmentVariable] = string.Empty,
        };
    }

    private static string ResolveRepositoryRootPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, SolutionFileName)))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test execution directory.");
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
            throw new InvalidOperationException($"Failed to start Claude Code CLI at '{executablePath}'.");
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
