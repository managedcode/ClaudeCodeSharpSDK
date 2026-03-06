using System.Diagnostics;
using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Configuration;
using ManagedCode.ClaudeCodeSharpSDK.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Models;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Shared;

internal static class RealClaudeTestSupport
{
    private const string AuthCommand = "auth";
    private const string StatusCommand = "status";
    private const string LoginGuidanceFragment = "Please run /login";
    private const string UnauthorizedStatusCodeFragment = "401";
    private const string AuthenticationRequiredMessage =
        "Authenticated Claude Code session is required for this test. Run 'claude auth status' and complete '/login' first.";
    private const string ClaudeExecutableNotFoundMessage =
        "Claude Code executable could not be resolved for authenticated integration tests.";
    private const string ModelEnvironmentVariable = "CLAUDE_TEST_MODEL";

    public static bool CanRunAuthenticatedTests()
    {
        if (!TryResolveExecutablePath(out var executablePath))
        {
            return false;
        }

        return IsAuthenticated(executablePath);
    }

    public static (ClaudeClient Client, RealClaudeTestSettings Settings) CreateAuthenticatedClient()
    {
        if (!TryResolveExecutablePath(out var executablePath))
        {
            throw new InvalidOperationException(ClaudeExecutableNotFoundMessage);
        }

        if (!IsAuthenticated(executablePath))
        {
            throw new InvalidOperationException(AuthenticationRequiredMessage);
        }

        var settings = new RealClaudeTestSettings(executablePath, ResolveModel(executablePath));
        var client = new ClaudeClient(new ClaudeOptions
        {
            ClaudeExecutablePath = executablePath,
        });

        return (client, settings);
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

    private static bool IsAuthenticated(string executablePath)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(AuthCommand);
        startInfo.ArgumentList.Add(StatusCommand);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            return false;
        }

        var combinedOutput = string.Concat(standardOutput, standardError);
        return !combinedOutput.Contains(LoginGuidanceFragment, StringComparison.OrdinalIgnoreCase)
               && !combinedOutput.Contains(UnauthorizedStatusCodeFragment, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record RealClaudeTestSettings(string ExecutablePath, string Model);
