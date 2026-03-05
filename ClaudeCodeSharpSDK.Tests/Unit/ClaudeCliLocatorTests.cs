using ManagedCode.ClaudeCodeSharpSDK.Internal;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Unit;

public class ClaudeCliLocatorTests
{
    [Test]
    public async Task GetPathExecutableCandidates_Windows_IncludeCommandWrappers()
    {
        var candidates = ClaudeCliLocator.GetPathExecutableCandidates(isWindows: true);

        await Assert.That(candidates).IsEquivalentTo(
        [
            ClaudeCliLocator.ClaudeWindowsExecutableName,
            ClaudeCliLocator.ClaudeWindowsCommandName,
            ClaudeCliLocator.ClaudeWindowsBatchName,
            ClaudeCliLocator.ClaudeExecutableName,
        ]);
    }

    [Test]
    public async Task TryResolvePathExecutable_Windows_ResolvesCmdWhenExeMissing()
    {
        var sandboxDirectory = CreateSandboxDirectory();

        try
        {
            var firstPathEntry = Path.Combine(sandboxDirectory, "first");
            var secondPathEntry = Path.Combine(sandboxDirectory, "second");
            Directory.CreateDirectory(firstPathEntry);
            Directory.CreateDirectory(secondPathEntry);

            var cmdPath = Path.Combine(secondPathEntry, ClaudeCliLocator.ClaudeWindowsCommandName);
            await File.WriteAllTextAsync(cmdPath, "@echo off");

            var pathVariable = string.Join(Path.PathSeparator, firstPathEntry, secondPathEntry);

            var resolved = ClaudeCliLocator.TryResolvePathExecutable(pathVariable, isWindows: true, out var executablePath);
            await Assert.That(resolved).IsTrue();
            await Assert.That(executablePath).IsEqualTo(cmdPath);
        }
        finally
        {
            Directory.Delete(sandboxDirectory, recursive: true);
        }
    }

    [Test]
    public async Task TryResolvePathExecutable_Unix_DoesNotTreatCmdAsExecutable()
    {
        var sandboxDirectory = CreateSandboxDirectory();

        try
        {
            var pathEntry = Path.Combine(sandboxDirectory, "unix");
            Directory.CreateDirectory(pathEntry);

            await File.WriteAllTextAsync(Path.Combine(pathEntry, ClaudeCliLocator.ClaudeWindowsCommandName), "#!/usr/bin/env bash");

            var resolved = ClaudeCliLocator.TryResolvePathExecutable(pathEntry, isWindows: false, out var executablePath);
            await Assert.That(resolved).IsFalse();
            await Assert.That(executablePath).IsEqualTo(string.Empty);
        }
        finally
        {
            Directory.Delete(sandboxDirectory, recursive: true);
        }
    }

    [Test]
    public async Task FindClaudePath_ReturnsOverrideWithoutFurtherResolution()
    {
        const string overridePath = "/tmp/custom-claude";

        var resolved = ClaudeCliLocator.FindClaudePath(overridePath);

        await Assert.That(resolved).IsEqualTo(overridePath);
    }

    [Test]
    public async Task TryResolveNodeModulesBinary_Windows_IgnoresPackageCliEntryWithoutCommandWrapper()
    {
        var sandboxDirectory = CreateSandboxDirectory();

        try
        {
            var packageDirectory = Path.Combine(
                sandboxDirectory,
                "node_modules",
                "@anthropic-ai",
                "claude-code");
            Directory.CreateDirectory(packageDirectory);
            await File.WriteAllTextAsync(Path.Combine(packageDirectory, "cli.js"), "#!/usr/bin/env node");

            var resolved = ClaudeCliLocator.TryResolveNodeModulesBinary([sandboxDirectory], isWindows: true, out var executablePath);

            await Assert.That(resolved).IsFalse();
            await Assert.That(executablePath).IsEqualTo(string.Empty);
        }
        finally
        {
            Directory.Delete(sandboxDirectory, recursive: true);
        }
    }

    [Test]
    public async Task TryResolveNodeModulesBinary_Unix_UsesPackageCliEntryWhenWrapperMissing()
    {
        var sandboxDirectory = CreateSandboxDirectory();

        try
        {
            var packageDirectory = Path.Combine(
                sandboxDirectory,
                "node_modules",
                "@anthropic-ai",
                "claude-code");
            Directory.CreateDirectory(packageDirectory);
            var cliPath = Path.Combine(packageDirectory, "cli.js");
            await File.WriteAllTextAsync(cliPath, "#!/usr/bin/env node");

            var resolved = ClaudeCliLocator.TryResolveNodeModulesBinary([sandboxDirectory], isWindows: false, out var executablePath);

            await Assert.That(resolved).IsTrue();
            await Assert.That(executablePath).IsEqualTo(cliPath);
        }
        finally
        {
            Directory.Delete(sandboxDirectory, recursive: true);
        }
    }

    private static string CreateSandboxDirectory()
    {
        var sandboxDirectory = Path.Combine(
            Environment.CurrentDirectory,
            "tests",
            ".sandbox",
            $"ClaudeCliLocatorTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sandboxDirectory);
        return sandboxDirectory;
    }
}
