using System.Diagnostics;
using System.Text.Json;
using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Configuration;
using ManagedCode.ClaudeCodeSharpSDK.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Models;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Shared;

internal static class RealClaudeTestSupport
{
    private const string AuthCommandName = "auth";
    private const string HaikuToken = "haiku";
    private const string StatusCommandName = "status";
    private const string ClaudeDirectoryName = ".claude";
    private const string AuthenticationRequiredMessage =
        "Authenticated Claude Code session is required for this test. Start Claude Code and complete '/login' first.";
    private const string ClaudeExecutableNotFoundMessage =
        "Claude Code executable could not be resolved for authenticated integration tests.";
    private const string CouldNotLocateRepositoryRootMessage = "Could not locate repository root from test execution directory.";
    private const string JsonLinesFileExtension = ".jsonl";
    private const string LoggedInPropertyName = "loggedIn";
    private const string ModelEnvironmentVariable = "CLAUDE_TEST_MODEL";
    private const string SonnetToken = "sonnet";
    private const string ProjectsDirectoryName = "projects";
    private const int AuthenticationProbeTimeoutMilliseconds = 60000;
    private const string TerminateFailureMessagePrefix = "Failed to terminate timed-out Claude auth probe process: ";
    private static readonly Lazy<AuthenticationProbeResult> CachedAuthenticationProbe = new(ProbeAuthentication);

    public static bool CanRunAuthenticatedTests()
    {
        if (!TryResolveExecutablePath(out var executablePath))
        {
            return false;
        }

        var probeResult = CachedAuthenticationProbe.Value;
        return string.Equals(probeResult.ExecutablePath, executablePath, StringComparison.Ordinal)
               && probeResult.IsAuthenticated;
    }

    public static RealClaudeTestSettings GetRequiredSettings()
    {
        if (!TryResolveExecutablePath(out var executablePath))
        {
            throw new InvalidOperationException(ClaudeExecutableNotFoundMessage);
        }

        var probeResult = CachedAuthenticationProbe.Value;
        if (!string.Equals(probeResult.ExecutablePath, executablePath, StringComparison.Ordinal)
            || !probeResult.IsAuthenticated)
        {
            throw new InvalidOperationException(AuthenticationRequiredMessage);
        }

        return new RealClaudeTestSettings(executablePath, ResolveModel(executablePath));
    }

    public static ClaudeClient CreateClient()
    {
        if (!TryResolveExecutablePath(out var executablePath))
        {
            throw new InvalidOperationException(ClaudeExecutableNotFoundMessage);
        }

        return new ClaudeClient(new ClaudeOptions
        {
            ClaudeExecutablePath = executablePath,
        });
    }

    public static async Task<string?> FindPersistedSessionPathAsync(string sessionId, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var projectsPath = GetClaudeProjectsPath();
        if (projectsPath is null || !Directory.Exists(projectsPath))
        {
            return null;
        }

        var searchPattern = string.Concat(sessionId, JsonLinesFileExtension);
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            var persistedPath = Directory
                .EnumerateFiles(projectsPath, searchPattern, SearchOption.AllDirectories)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(persistedPath))
            {
                return persistedPath;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
        }

        return null;
    }

    public static string ResolveRepositoryRootPath()
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

    private static string ResolveModel(string executablePath)
    {
        var configuredModel = Environment.GetEnvironmentVariable(ModelEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredModel))
        {
            return configuredModel!;
        }

        var metadata = ClaudeCliMetadataReader.Read(executablePath);
        return SelectPreferredTestModel(metadata);
    }

    private static bool TryResolveExecutablePath(out string executablePath)
    {
        executablePath = ClaudeCliLocator.FindClaudePath(null);
        if (Path.IsPathRooted(executablePath))
        {
            return File.Exists(executablePath);
        }

        return ClaudeCliLocator.TryResolvePathExecutable(
            Environment.GetEnvironmentVariable(TestConstants.PathEnvironmentVariable),
            OperatingSystem.IsWindows(),
            out executablePath);
    }

    private static string? GetClaudeProjectsPath()
    {
        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(homeDirectory))
        {
            return null;
        }

        return Path.Combine(homeDirectory, ClaudeDirectoryName, ProjectsDirectoryName);
    }

    private static AuthenticationProbeResult ProbeAuthentication()
    {
        if (!TryResolveExecutablePath(out var executablePath))
        {
            return new AuthenticationProbeResult(null, false);
        }

        var startInfo = new ProcessStartInfo(executablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(AuthCommandName);
        startInfo.ArgumentList.Add(StatusCommandName);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return new AuthenticationProbeResult(executablePath, false);
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(AuthenticationProbeTimeoutMilliseconds))
        {
            TryTerminate(process);
            return new AuthenticationProbeResult(executablePath, false);
        }

        Task.WaitAll(standardOutputTask, standardErrorTask);

        var standardOutput = standardOutputTask.GetAwaiter().GetResult();
        var standardError = standardErrorTask.GetAwaiter().GetResult();
        var combinedOutput = string.Concat(standardOutput, standardError);

        if (process.ExitCode != 0)
        {
            return new AuthenticationProbeResult(executablePath, false);
        }

        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(standardOutput) ? combinedOutput : standardOutput);
            var root = document.RootElement;
            var isAuthenticated = root.TryGetProperty(LoggedInPropertyName, out var loggedInElement)
                                  && loggedInElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                                  && loggedInElement.GetBoolean();
            return new AuthenticationProbeResult(executablePath, isAuthenticated);
        }
        catch (JsonException)
        {
            return new AuthenticationProbeResult(executablePath, false);
        }
    }

    private static string SelectPreferredTestModel(ClaudeCliMetadata metadata)
    {
        var defaultModel = metadata.DefaultModel;
        if (!string.IsNullOrWhiteSpace(defaultModel)
            && defaultModel.Contains(HaikuToken, StringComparison.OrdinalIgnoreCase))
        {
            return defaultModel;
        }

        if (metadata.Models.Any(
                static model => string.Equals(model.Slug, ClaudeModels.Haiku, StringComparison.OrdinalIgnoreCase)))
        {
            return ClaudeModels.Haiku;
        }

        if (!string.IsNullOrWhiteSpace(defaultModel)
            && defaultModel.Contains(SonnetToken, StringComparison.OrdinalIgnoreCase))
        {
            return defaultModel;
        }

        if (metadata.Models.Any(
                static model => string.Equals(model.Slug, ClaudeModels.Sonnet, StringComparison.OrdinalIgnoreCase)))
        {
            return ClaudeModels.Sonnet;
        }

        if (!string.IsNullOrWhiteSpace(defaultModel))
        {
            return defaultModel;
        }

        return ClaudeModels.Sonnet;
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception)
        {
            Trace.WriteLine(string.Concat(TerminateFailureMessagePrefix, exception.Message));
        }
    }
}

internal sealed record RealClaudeTestSettings(string ExecutablePath, string Model);

internal sealed record AuthenticationProbeResult(string? ExecutablePath, bool IsAuthenticated);
