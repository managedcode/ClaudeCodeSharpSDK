using ManagedCode.ClaudeCodeSharpSDK.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Tests.Shared;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Unit;

public class ClaudeCliLocatorTests
{
    private const string AnthropicScopeDirectory = "@anthropic-ai";
    private const string CustomOverridePath = "/tmp/custom-claude";
    private const string FirstPathEntryName = "first";
    private const string GuidFormat = "N";
    private const string NodeModulesDirectory = "node_modules";
    private const string PackageDirectory = "claude-code";
    private const string SandboxPrefix = "ClaudeCliLocatorTests-";
    private const string SecondPathEntryName = "second";
    private const string UnixPathEntryName = "unix";

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
            var firstPathEntry = Path.Combine(sandboxDirectory, FirstPathEntryName);
            var secondPathEntry = Path.Combine(sandboxDirectory, SecondPathEntryName);
            Directory.CreateDirectory(firstPathEntry);
            Directory.CreateDirectory(secondPathEntry);

            var cmdPath = Path.Combine(secondPathEntry, ClaudeCliLocator.ClaudeWindowsCommandName);
            await File.WriteAllTextAsync(cmdPath, TestConstants.EchoOffScript);

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
            var pathEntry = Path.Combine(sandboxDirectory, UnixPathEntryName);
            Directory.CreateDirectory(pathEntry);

            await File.WriteAllTextAsync(Path.Combine(pathEntry, ClaudeCliLocator.ClaudeWindowsCommandName), TestConstants.BashShebang);

            var resolved = ClaudeCliLocator.TryResolvePathExecutable(pathEntry, isWindows: false, out var executablePath);
            await Assert.That(resolved).IsFalse();
            await Assert.That(executablePath).IsEqualTo(TestConstants.EmptyString);
        }
        finally
        {
            Directory.Delete(sandboxDirectory, recursive: true);
        }
    }

    [Test]
    public async Task FindClaudePath_ReturnsOverrideWithoutFurtherResolution()
    {
        var resolved = ClaudeCliLocator.FindClaudePath(CustomOverridePath);

        await Assert.That(resolved).IsEqualTo(CustomOverridePath);
    }

    [Test]
    public async Task TryResolveNodeModulesBinary_Windows_IgnoresPackageCliEntryWithoutCommandWrapper()
    {
        var sandboxDirectory = CreateSandboxDirectory();

        try
        {
            var packageDirectory = Path.Combine(
                sandboxDirectory,
                NodeModulesDirectory,
                AnthropicScopeDirectory,
                PackageDirectory);
            Directory.CreateDirectory(packageDirectory);
            await File.WriteAllTextAsync(Path.Combine(packageDirectory, TestConstants.CliJsFileName), TestConstants.NodeShebang);

            var resolved = ClaudeCliLocator.TryResolveNodeModulesBinary([sandboxDirectory], isWindows: true, out var executablePath);

            await Assert.That(resolved).IsFalse();
            await Assert.That(executablePath).IsEqualTo(TestConstants.EmptyString);
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
                NodeModulesDirectory,
                AnthropicScopeDirectory,
                PackageDirectory);
            Directory.CreateDirectory(packageDirectory);
            var cliPath = Path.Combine(packageDirectory, TestConstants.CliJsFileName);
            await File.WriteAllTextAsync(cliPath, TestConstants.NodeShebang);

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
            TestConstants.TestsDirectoryName,
            TestConstants.SandboxDirectoryName,
            string.Concat(SandboxPrefix, Guid.NewGuid().ToString(GuidFormat)));
        Directory.CreateDirectory(sandboxDirectory);
        return sandboxDirectory;
    }
}
