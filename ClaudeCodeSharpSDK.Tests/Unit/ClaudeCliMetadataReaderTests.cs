using System.Diagnostics;
using System.Text.Json;
using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Configuration;
using ManagedCode.ClaudeCodeSharpSDK.Execution;
using ManagedCode.ClaudeCodeSharpSDK.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Models;
using ManagedCode.ClaudeCodeSharpSDK.Tests.Shared;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Unit;

public class ClaudeCliMetadataReaderTests
{
    private const string MetadataSandboxPrefix = "ClaudeCliMetadataReaderTests-";
    private const string NpmPackageDirectory = "claude-code";
    private const string NodeModulesDirectory = "node_modules";
    private const string DotBinDirectory = ".bin";
    private const string NpmPackageManifestName = "package.json";
    private const string NpmShimName = "claude.cmd";
    private const string NpmEntrypointRelativePath = "bundle/claude-fixture.js";
    private const string NpmPackageManifest = "{\"name\":\"@anthropic-ai/claude-code\",\"bin\":{\"claude\":\"bundle/claude-fixture.js\"}}";
    private const string NodeCliFixture = "if(process.argv.includes('--version')){console.log('2.0.75 (Claude Code)');}else{process.stdin.setEncoding('utf8');let input='';process.stdin.on('data',chunk=>input+=chunk);process.stdin.on('end',()=>console.log(JSON.stringify({args:process.argv.slice(2),input})));}";
    private const string PromptFixture = "--approval-mode=yolo ! & | ^ % ' space\nsecond line";
    private const string NodeWindowsExecutableName = "node.exe";
    private const string NodeExecutableName = "node";
    private const string NpmPackageScopeSegment = "@anthropic-ai";
    private const string WindowsShimTestSkipReason = "The npm command-shim launch contract is Windows-specific.";
    private const string UnsupportedShimErrorFragment = "configured Claude Code command shim is unsupported";
    private const string SystemRootRequiredMessage = "SystemRoot is required for Windows child processes.";
    private const string NodeRequiredMessage = "Node.js is required for the Windows npm shim test.";
    private const string QuoteCharacter = "\"";
    private const string JsonInputPropertyName = "input";
    private const string JsonArgumentsPropertyName = "args";
    private const string PrintFlag = "--print";
    private const string PathEnvironmentVariable = "PATH";
    private const string SystemRootEnvironmentVariable = "SystemRoot";
    private const string ClaudeConfigDirectoryEnvironmentVariable = "CLAUDE_CONFIG_DIR";
    private const string SettingsFileName = "settings.json";
    private const int SmallMetadataFileLimit = 256;
    private const string MetadataFileLimitMessage = "CLI metadata file exceeded the configured character limit.";
    private const string ClaudeSettingsFixture = "{ \"model\": \"" + ClaudeModels.Sonnet + "\" }";
    private const string CommandFlagUnix = "-c";
    private const string CommandFlagWindows = "/c";
    private const string ConcurrentOutputCommandUnix =
        "i=0; while [ $i -lt 5000 ]; do printf 'stdout-line-%s\\n' \"$i\"; printf 'stderr-line-%s\\n' \"$i\" >&2; i=$((i+1)); done";
    private const string ConcurrentOutputCommandWindows =
        "for /L %i in (0,1,4999) do @(echo stdout-line-%i & echo stderr-line-%i 1>&2)";
    private const string DefaultModelJsonTemplate = "{\"model\":\"__MODEL__\",\"statusLine\":{\"enabled\":true}}";
    private const string FirstStandardErrorLine = "stderr-line-0";
    private const string FirstStandardOutputLine = "stdout-line-0";
    private const string HighestStableGitOutput = "deadbeef\trefs/tags/v2.0.74\nfeedface\trefs/tags/v2.0.75-beta.1\ncafebabe\trefs/tags/v2.0.75";
    private const string InstalledVersionOutput = "2.0.75 (Claude Code)";
    private const string InvalidVersionText = "not-a-version";
    private const string LastStandardErrorLine = "stderr-line-4999";
    private const string LastStandardOutputLine = "stdout-line-4999";
    private const string ModelPlaceholder = "__MODEL__";
    private const string NumericPrereleaseGitOutput = "deadbeef\trefs/tags/v2.0.75-beta.2\nfeedface\trefs/tags/v2.0.75-beta.10";
    private const string ProcessReadTimedOutMessage = "Concurrent process stream read timed out.";
    private const string ShellExecutableUnix = "/bin/sh";
    private const string ShellExecutableWindows = "cmd.exe";
    private const string StartProcessFailedMessage = "Failed to start metadata reader test process.";
    private const string PrereleaseTenVersion = "2.0.75-beta.10";
    private const string PrereleaseTwoVersion = "2.0.75-beta.2";
    private const string StableVersion = "2.0.75";
    private const string StableVsPrereleaseVersion = "2.0.75-beta.1";
    private static readonly TimeSpan ProcessReadTimeout = TimeSpan.FromSeconds(90);

    [Test]
    public async Task ParseInstalledVersion_ReturnsFirstTokenForClaudeCodeOutput()
    {
        var parsed = ClaudeCliMetadataReader.ParseInstalledVersion(InstalledVersionOutput);

        await Assert.That(parsed).IsEqualTo(StableVersion);
    }

    [Test]
    public async Task ParseLatestPublishedVersion_PicksHighestGitTag()
    {
        var parsed = ClaudeCliMetadataReader.ParseLatestPublishedVersion(HighestStableGitOutput);

        await Assert.That(parsed).IsEqualTo(StableVersion);
    }

    [Test]
    public async Task IsNewerVersion_TreatsStableAsNotOlderThanMatchingPrerelease()
    {
        var isNewer = ClaudeCliMetadataReader.IsNewerVersion(StableVsPrereleaseVersion, StableVersion);

        await Assert.That(isNewer).IsFalse();
    }

    [Test]
    public async Task IsNewerVersion_UsesNumericSemVerComparisonForPrereleaseIdentifiers()
    {
        var isNewer = ClaudeCliMetadataReader.IsNewerVersion(PrereleaseTenVersion, PrereleaseTwoVersion);

        await Assert.That(isNewer).IsTrue();
    }

    [Test]
    public async Task ParseLatestPublishedVersion_UsesNumericSemVerComparisonForPrereleaseIdentifiers()
    {
        var parsed = ClaudeCliMetadataReader.ParseLatestPublishedVersion(NumericPrereleaseGitOutput);

        await Assert.That(parsed).IsEqualTo(PrereleaseTenVersion);
    }

    [Test]
    public async Task ParseDefaultModelFromJson_ReadsModelProperty()
    {
        var parsed = ClaudeCliMetadataReader.ParseDefaultModelFromJson(
            DefaultModelJsonTemplate.Replace(ModelPlaceholder, ClaudeModels.ClaudeOpus45, StringComparison.Ordinal));

        await Assert.That(parsed).IsEqualTo(ClaudeModels.ClaudeOpus45);
    }

    [Test]
    public async Task TryParseSemanticVersion_RejectsInvalidText()
    {
        var parsed = ClaudeCliMetadataReader.TryParseSemanticVersion(InvalidVersionText, out _);

        await Assert.That(parsed).IsFalse();
    }

    [Test]
    public async Task ReadStandardStreamsAndWaitForExit_CapturesLargeStandardOutputAndError()
    {
        using var process = Process.Start(CreateConcurrentOutputProcessStartInfo())
                            ?? throw new InvalidOperationException(StartProcessFailedMessage);
        process.StandardInput.Close();

        var readTask = Task.Run(() => ClaudeCliMetadataReader.ReadStandardStreamsAndWaitForExit(process));
        var completedTask = await Task.WhenAny(readTask, Task.Delay(ProcessReadTimeout));
        if (!ReferenceEquals(completedTask, readTask))
        {
            TryKillProcess(process);
            throw new TimeoutException(ProcessReadTimedOutMessage);
        }

        var (standardOutput, standardError) = await readTask;

        await Assert.That(process.ExitCode).IsEqualTo(0);
        await Assert.That(standardOutput).Contains(FirstStandardOutputLine);
        await Assert.That(standardOutput).Contains(LastStandardOutputLine);
        await Assert.That(standardError).Contains(FirstStandardErrorLine);
        await Assert.That(standardError).Contains(LastStandardErrorLine);
    }

    [Test]
    public async Task ClaudeClient_GetCliMetadata_UsesConfiguredSettingsAndEnvironmentAllowlist()
    {
        var configDirectory = Path.Combine(Environment.CurrentDirectory, "tests", ".sandbox",
            $"{MetadataSandboxPrefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(configDirectory);
        try
        {
            File.WriteAllText(Path.Combine(configDirectory, SettingsFileName), ClaudeSettingsFixture);
            using var client = new ClaudeClient(new ClaudeOptions
            {
                ClaudeExecutablePath = ClaudeCliLocator.FindClaudePath(null),
                EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [PathEnvironmentVariable] = Environment.GetEnvironmentVariable(PathEnvironmentVariable) ?? string.Empty,
                    [SystemRootEnvironmentVariable] = Environment.GetEnvironmentVariable(SystemRootEnvironmentVariable) ?? string.Empty,
                    [ClaudeConfigDirectoryEnvironmentVariable] = configDirectory,
                },
                InheritEnvironmentVariables = false,
            });

            var metadata = client.GetCliMetadata();

            await Assert.That(metadata.DefaultModel).IsEqualTo(ClaudeModels.Sonnet);
            await Assert.That(string.IsNullOrWhiteSpace(metadata.InstalledVersion)).IsFalse();
        }
        finally
        {
            Directory.Delete(configDirectory, recursive: true);
        }
    }

    [Test]
    public async Task ClaudeClient_GetCliMetadata_ResolvesOfficialNpmShimThroughNodeOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test(WindowsShimTestSkipReason);
        }

        var sandboxDirectory = Path.Combine(Environment.CurrentDirectory, "tests", ".sandbox",
            $"{MetadataSandboxPrefix}{Guid.NewGuid():N}");
        var modulesDirectory = Path.Combine(sandboxDirectory, NodeModulesDirectory);
        var packageDirectory = Path.Combine(modulesDirectory, NpmPackageScopeSegment, NpmPackageDirectory);
        var shimDirectory = Path.Combine(modulesDirectory, DotBinDirectory);
        Directory.CreateDirectory(packageDirectory);
        Directory.CreateDirectory(shimDirectory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(packageDirectory, NpmPackageManifestName), NpmPackageManifest);
            var entrypointPath = Path.Combine(packageDirectory, NpmEntrypointRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(entrypointPath)!);
            await File.WriteAllTextAsync(entrypointPath, NodeCliFixture);
            var shimPath = Path.Combine(shimDirectory, NpmShimName);
            await File.WriteAllTextAsync(shimPath, TestConstants.EchoOffScript);

            var nodePath = FindNodeExecutablePath(Environment.GetEnvironmentVariable(PathEnvironmentVariable), isWindows: true);
            var effectivePath = string.Join(Path.PathSeparator,
                Path.GetDirectoryName(nodePath)!, Environment.GetEnvironmentVariable(PathEnvironmentVariable) ?? string.Empty);
            using var client = new ClaudeClient(new ClaudeOptions
            {
                ClaudeExecutablePath = shimPath,
                EnvironmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [PathEnvironmentVariable] = effectivePath,
                    [SystemRootEnvironmentVariable] = Environment.GetEnvironmentVariable(SystemRootEnvironmentVariable)
                        ?? throw new InvalidOperationException(SystemRootRequiredMessage),
                },
                InheritEnvironmentVariables = false,
            });

            var launch = client.GetCliLaunchCommand();
            var metadata = client.GetCliMetadata();

            await Assert.That(launch.ExecutablePath).IsEqualTo(nodePath);
            await Assert.That(launch.PrefixArguments).IsEquivalentTo([Path.GetFullPath(entrypointPath)]);
            await Assert.That(metadata.InstalledVersion).IsEqualTo("2.0.75");
        }
        finally
        {
            Directory.Delete(sandboxDirectory, recursive: true);
        }
    }

    [Test]
    public async Task ClaudeClient_GetCliLaunchCommand_DoesNotReplaceAnExplicitUnknownShimWithTheDefaultCli()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test(WindowsShimTestSkipReason);
        }

        var sandboxDirectory = Path.Combine(Environment.CurrentDirectory, "tests", ".sandbox",
            $"{MetadataSandboxPrefix}{Guid.NewGuid():N}");
        var explicitCommandDirectory = Path.Combine(sandboxDirectory, "custom-bin");
        var defaultShimDirectory = Path.Combine(sandboxDirectory, NodeModulesDirectory, DotBinDirectory);
        var decoyPackageDirectory = Path.Combine(sandboxDirectory, NodeModulesDirectory,
            NpmPackageScopeSegment, NpmPackageDirectory);
        Directory.CreateDirectory(explicitCommandDirectory);
        Directory.CreateDirectory(defaultShimDirectory);
        Directory.CreateDirectory(decoyPackageDirectory);
        try
        {
            var explicitShim = Path.Combine(explicitCommandDirectory, "custom-claude.cmd");
            await File.WriteAllTextAsync(explicitShim, TestConstants.EchoOffScript);
            await File.WriteAllTextAsync(Path.Combine(defaultShimDirectory, NpmShimName), TestConstants.EchoOffScript);
            await File.WriteAllTextAsync(Path.Combine(decoyPackageDirectory, NpmPackageManifestName), NpmPackageManifest);
            var decoyEntrypoint = Path.Combine(decoyPackageDirectory, NpmEntrypointRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(decoyEntrypoint)!);
            await File.WriteAllTextAsync(decoyEntrypoint, NodeCliFixture);
            var nodePath = FindNodeExecutablePath(Environment.GetEnvironmentVariable(PathEnvironmentVariable), isWindows: true);
            var effectivePath = string.Join(Path.PathSeparator, explicitCommandDirectory, defaultShimDirectory,
                Path.GetDirectoryName(nodePath)!);
            using var client = new ClaudeClient(new ClaudeOptions
            {
                ClaudeExecutablePath = explicitShim,
                EnvironmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [PathEnvironmentVariable] = effectivePath,
                    [SystemRootEnvironmentVariable] = Environment.GetEnvironmentVariable(SystemRootEnvironmentVariable)
                        ?? throw new InvalidOperationException(SystemRootRequiredMessage),
                },
                InheritEnvironmentVariables = false,
            });

            var action = () => client.GetCliLaunchCommand();
            var exception = await Assert.That(action).ThrowsException();

            await Assert.That(exception).IsTypeOf<InvalidOperationException>();
            await Assert.That(exception!.Message).Contains(UnsupportedShimErrorFragment);
        }
        finally
        {
            Directory.Delete(sandboxDirectory, recursive: true);
        }
    }

    [Test]
    public async Task ClaudeExec_ResolvesJavaScriptEntryAndPassesPromptThroughStdin()
    {
        var sandboxDirectory = Path.Combine(Environment.CurrentDirectory, "tests", ".sandbox",
            $"{MetadataSandboxPrefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(sandboxDirectory);
        try
        {
            var entrypointPath = Path.Combine(sandboxDirectory, NpmEntrypointRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(entrypointPath)!);
            await File.WriteAllTextAsync(entrypointPath, NodeCliFixture);
            var nodePath = FindNodeExecutablePath(Environment.GetEnvironmentVariable(PathEnvironmentVariable), OperatingSystem.IsWindows());
            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [PathEnvironmentVariable] = string.Join(Path.PathSeparator, Path.GetDirectoryName(nodePath)!,
                    Environment.GetEnvironmentVariable(PathEnvironmentVariable) ?? string.Empty),
            };
            if (OperatingSystem.IsWindows())
            {
                environment[SystemRootEnvironmentVariable] = Environment.GetEnvironmentVariable(SystemRootEnvironmentVariable)
                    ?? throw new InvalidOperationException(SystemRootRequiredMessage);
            }

            var input = string.Concat(PromptFixture, new string('x', 256 * 1024));
            var exec = new ClaudeExec(inheritEnvironmentVariables: false, executablePath: entrypointPath,
                environmentOverride: environment);
            var output = new List<string>();
            await foreach (var line in exec.RunAsync(new ClaudeExecArgs { Input = input }))
            {
                output.Add(line);
            }

            await Assert.That(output.Count).IsEqualTo(1);
            using var execution = JsonDocument.Parse(output[0]);
            await Assert.That(execution.RootElement.GetProperty(JsonInputPropertyName).GetString()).IsEqualTo(input);
            await Assert.That(execution.RootElement.GetProperty(JsonArgumentsPropertyName).EnumerateArray()
                .Any(argument => string.Equals(argument.GetString(), PrintFlag, StringComparison.Ordinal))).IsTrue();
        }
        finally
        {
            Directory.Delete(sandboxDirectory, recursive: true);
        }
    }

    [Test]
    public async Task ClaudeClient_GetCliMetadata_RejectsOversizedSettingsFile()
    {
        var configDirectory = Path.Combine(Environment.CurrentDirectory, "tests", ".sandbox",
            $"{MetadataSandboxPrefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(configDirectory);
        try
        {
            File.WriteAllText(Path.Combine(configDirectory, SettingsFileName),
                string.Concat(ClaudeSettingsFixture, new string('x', SmallMetadataFileLimit + 1)));
            using var client = new ClaudeClient(new ClaudeOptions
            {
                ClaudeExecutablePath = ClaudeCliLocator.FindClaudePath(null),
                EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [PathEnvironmentVariable] = Environment.GetEnvironmentVariable(PathEnvironmentVariable) ?? string.Empty,
                    [SystemRootEnvironmentVariable] = Environment.GetEnvironmentVariable(SystemRootEnvironmentVariable) ?? string.Empty,
                    [ClaudeConfigDirectoryEnvironmentVariable] = configDirectory,
                },
                InheritEnvironmentVariables = false,
                CliMetadataMaximumFileCharacters = SmallMetadataFileLimit,
            });

            var action = () => client.GetCliMetadata();
            var exception = await Assert.That(action).ThrowsException();

            await Assert.That(exception).IsTypeOf<InvalidOperationException>();
            await Assert.That(exception!.Message).Contains(MetadataFileLimitMessage);
        }
        finally
        {
            Directory.Delete(configDirectory, recursive: true);
        }
    }

    [Test]
    public async Task ClaudeClient_GetCliMetadata_RejectsNonPositiveProbeLeaseTimeout()
    {
        foreach (var timeout in new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(-1) })
        {
            using var client = new ClaudeClient(new ClaudeOptions { CliMetadataProbeLeaseTimeout = timeout });
            var action = () => client.GetCliMetadata();
            var exception = await Assert.That(action).ThrowsException();

            await Assert.That(exception).IsTypeOf<ArgumentOutOfRangeException>();
        }
    }

    private static ProcessStartInfo CreateConcurrentOutputProcessStartInfo()
    {
        var startInfo = new ProcessStartInfo(
            OperatingSystem.IsWindows() ? ShellExecutableWindows : ShellExecutableUnix)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add(OperatingSystem.IsWindows() ? CommandFlagWindows : CommandFlagUnix);
        startInfo.ArgumentList.Add(OperatingSystem.IsWindows() ? ConcurrentOutputCommandWindows : ConcurrentOutputCommandUnix);
        return startInfo;
    }

    private static string FindNodeExecutablePath(string? pathVariable, bool isWindows)
    {
        if (!string.IsNullOrWhiteSpace(pathVariable))
        {
            foreach (var pathEntry in pathVariable.Split(Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var candidate = Path.Combine(pathEntry.Trim(QuoteCharacter[0]),
                    isWindows ? NodeWindowsExecutableName : NodeExecutableName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new InvalidOperationException(NodeRequiredMessage);
    }

    private static void TryKillProcess(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }
}
