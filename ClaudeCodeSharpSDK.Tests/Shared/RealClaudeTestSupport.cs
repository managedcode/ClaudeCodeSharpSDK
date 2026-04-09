using System.Diagnostics;
using System.Text.Json;
using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Configuration;
using ManagedCode.ClaudeCodeSharpSDK.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Models;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Shared;

internal static class RealClaudeTestSupport
{
    private const string ClaudeDirectoryName = ".claude";
    private const string DangerouslySkipPermissionsFlag = "--dangerously-skip-permissions";
    private const string LoginGuidanceFragment = "Please run /login";
    private const string MaxBudgetFlag = "--max-budget-usd";
    private const string MaxBudgetValue = "0.05";
    private const string UnauthorizedStatusCodeFragment = "401";
    private const string AuthenticationRequiredMessage =
        "Authenticated Claude Code session is required for this test. Start Claude Code and complete '/login' first.";
    private const string ClaudeExecutableNotFoundMessage =
        "Claude Code executable could not be resolved for authenticated integration tests.";
    private const string CouldNotLocateRepositoryRootMessage = "Could not locate repository root from test execution directory.";
    private const string JsonLinesFileExtension = ".jsonl";
    private const string ModelEnvironmentVariable = "CLAUDE_TEST_MODEL";
    private const string NoSessionPersistenceFlag = "--no-session-persistence";
    private const string OutputFormatFlag = "--output-format";
    private const string ProjectsDirectoryName = "projects";
    private const string JsonOutputFormat = "json";
    private const string PrintFlag = "-p";
    private const string ProbePrompt = "Reply with ok only.";
    private const string ResultPropertyName = "result";
    private const string IsErrorPropertyName = "is_error";
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
        return string.IsNullOrWhiteSpace(configuredModel)
            ? ClaudeCliMetadataReader.Read(executablePath).DefaultModel ?? ClaudeModels.Sonnet
            : configuredModel!;
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
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(PrintFlag);
        startInfo.ArgumentList.Add(OutputFormatFlag);
        startInfo.ArgumentList.Add(JsonOutputFormat);
        startInfo.ArgumentList.Add(DangerouslySkipPermissionsFlag);
        startInfo.ArgumentList.Add(NoSessionPersistenceFlag);
        startInfo.ArgumentList.Add(MaxBudgetFlag);
        startInfo.ArgumentList.Add(MaxBudgetValue);
        startInfo.ArgumentList.Add(ProbePrompt);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return new AuthenticationProbeResult(executablePath, false);
        }

        process.StandardInput.Close();
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

        if (combinedOutput.Contains(LoginGuidanceFragment, StringComparison.OrdinalIgnoreCase)
            || combinedOutput.Contains(UnauthorizedStatusCodeFragment, StringComparison.OrdinalIgnoreCase))
        {
            return new AuthenticationProbeResult(executablePath, false);
        }

        try
        {
            using var document = JsonDocument.Parse(standardOutput);
            var root = document.RootElement;
            var isError = root.TryGetProperty(IsErrorPropertyName, out var isErrorElement)
                          && isErrorElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                          && isErrorElement.GetBoolean();
            var result = root.TryGetProperty(ResultPropertyName, out var resultElement)
                ? resultElement.GetString()
                : null;
            var isAuthenticated = !isError && !string.IsNullOrWhiteSpace(result);
            return new AuthenticationProbeResult(executablePath, isAuthenticated);
        }
        catch (JsonException)
        {
            return new AuthenticationProbeResult(executablePath, false);
        }
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
