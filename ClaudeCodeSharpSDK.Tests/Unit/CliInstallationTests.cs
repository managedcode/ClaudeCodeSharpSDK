using System.Diagnostics;
using ManagedCode.ClaudeCodeSharpSDK.Configuration;
using ManagedCode.ClaudeCodeSharpSDK.Extensions.AI;
using ManagedCode.ClaudeCodeSharpSDK.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Unit;

public sealed class CliInstallationTests
{
    private const string SandboxDirectoryName = ".sandbox";
    private const string TestsDirectoryName = "tests";
    private const string NpmDirectoryName = "npm";
    private const string BinDirectoryName = "bin";
    private const string NpmBinDirectoryName = ".bin";
    private const string NpmCliFileName = "npm-cli.js";
    private const string PackageJsonFileName = "package.json";
    private const string NpmPackageName = "npm";
    private const string PackageName = "@anthropic-ai/claude-code";
    private const string PackageScopeDirectoryName = "@anthropic-ai";
    private const string PackageDirectoryName = "claude-code";
    private const string NodeModulesDirectoryName = "node_modules";
    private const string PackageEntryScript = "cli.js";
    private const string LocalApplicationDataDirectoryName = "local-app-data";
    private const string ManagedCodeDirectoryName = "ManagedCode";
    private const string SdkDirectoryName = "ManagedCode.ClaudeCodeSharpSDK";
    private const string InstallDirectoryName = "cli";
    private const string ChildPidFileName = "child.pid";
    private const string GuidFormat = "N";
    private const string NodeRequiredMessage = "Node.js is required for CLI installation process tests.";
    private const string CliName = "claude";
    private const string NodeName = "node";
    private const string NodeWindowsName = "node.exe";
    private const string PathName = "PATH";
    private const string SystemRootName = "SYSTEMROOT";
    private const string MismatchMarkerName = "mismatch";
    private const string HangMarkerName = "hang";
    private const string OverflowMarkerName = "overflow";
    private const string ScriptPlaceholderPackage = "PACKAGE_NAME";
    private const string ScriptPlaceholderCli = "CLI_NAME";
    private const string ScriptPlaceholderNpm = "NPM_PACKAGE_NAME";
    private const string ChatPrompt = "installed cli descriptor reaches a fresh chat";
    private const string AssistantSessionId = "installed-fixture";
    private const string ChatScriptSessionPlaceholder = "CLAUDE_SESSION";
    private const string ChatScriptModelPlaceholder = "CLAUDE_MODEL";
    private const string ChatScript = """
        const fs = require('node:fs');
        const input = fs.readFileSync(0, 'utf8');
        const sessionId = 'CLAUDE_SESSION';
        console.log(JSON.stringify({type:'system',subtype:'init',session_id:sessionId,cwd:process.cwd()}));
        console.log(JSON.stringify({type:'assistant',message:{id:'assistant-message',model:'CLAUDE_MODEL',role:'assistant',stop_reason:'end_turn',type:'message',usage:{input_tokens:1,cache_creation_input_tokens:0,cache_read_input_tokens:0,output_tokens:1},content:[{type:'text',text:input}]},session_id:sessionId,uuid:'assistant-event'}));
        console.log(JSON.stringify({type:'result',is_error:false,result:input}));
        """;
    private const string NpmPackageManifest = "{\"name\":\"NPM_PACKAGE_NAME\",\"version\":\"0.0.0\"}";
    private const string VersionMismatchMessage = "The installed Claude Code CLI version does not match the SDK compatibility target.";
    private const string OutputLimitMessage = "CLI installation exceeded its configured output limit.";
    private const string NpmScript = """
        const fs = require('node:fs');
        const path = require('node:path');
        const args = process.argv.slice(2);
        const prefixIndex = Math.max(args.indexOf('--prefix'), args.indexOf('--cwd'));
        const root = args[prefixIndex + 1];
        const packageSpec = args.at(-1);
        const version = packageSpec.slice(packageSpec.lastIndexOf('@') + 1);
        const packageRoot = path.join(root, 'node_modules', '@anthropic-ai', 'claude-code');
        fs.mkdirSync(packageRoot, { recursive: true });
        const installedVersion = fs.existsSync(path.join(__dirname, 'mismatch')) ? '0.0.0' : version;
        const entrypoint = 'cli.js';
        fs.mkdirSync(path.dirname(path.join(packageRoot, entrypoint)), { recursive: true });
        fs.writeFileSync(path.join(packageRoot, 'package.json'), JSON.stringify({name:'PACKAGE_NAME',version:installedVersion,bin:{claude:entrypoint}}));
        fs.writeFileSync(path.join(packageRoot, entrypoint), 'process.exit(0);');
        const shimDirectory = path.join(root, 'node_modules', '.bin');
        fs.mkdirSync(shimDirectory, { recursive: true });
        const suffix = process.platform === 'win32' ? '.cmd' : '';
        const shimPath = path.join(shimDirectory, 'CLI_NAME' + suffix);
        const shellQuote = value => "'" + value.replaceAll("'", "'\\''") + "'";
        const shim = process.platform === 'win32' ? '@echo off\r\n' : `#!/bin/sh\nexec ${shellQuote(process.execPath)} ${shellQuote(path.join(packageRoot, entrypoint))} "$@"\n`;
        fs.writeFileSync(shimPath, shim);
        if (process.platform !== 'win32') fs.chmodSync(shimPath, 0o755);
        if (fs.existsSync(path.join(__dirname, 'hang'))) {
            fs.writeFileSync(path.join(__dirname, 'child.pid'), String(process.pid));
            setInterval(() => {}, 1000);
        }
        if (fs.existsSync(path.join(__dirname, 'overflow'))) {
            process.stdout.write('x'.repeat(8192));
            setInterval(() => {}, 1000);
        }
        console.log('installation output');
        console.error('installation diagnostics');
        """;

    [Test]
    public async Task InstallResult_ConfiguresPublicChatClientAndRunsControlledCli()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var updates = await RunInstallationAsync(fixture);
            var result = updates.Single(update => update.Stage == CliInstallationStage.Installed).Result!;
            var entryPoint = result.LaunchCommand.PrefixArguments.SingleOrDefault() ??
                             Path.Combine(result.InstallationRootPath, NodeModulesDirectoryName,
                                 PackageScopeDirectoryName, PackageDirectoryName, PackageEntryScript);
            var cliScript = ChatScript
                .Replace(ChatScriptSessionPlaceholder, AssistantSessionId, StringComparison.Ordinal)
                .Replace(ChatScriptModelPlaceholder, ClaudeModels.ClaudeSonnet45Alias, StringComparison.Ordinal);
            await File.WriteAllTextAsync(entryPoint, cliScript);

            var options = new ClaudeOptions
            {
                LaunchCommand = result.LaunchCommand,
                InheritEnvironmentVariables = false,
                EnvironmentVariables = fixture.Options.EnvironmentVariables,
            };
            using var client = new ClaudeChatClient(new ClaudeChatClientOptions { ClaudeOptions = options });
            var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, ChatPrompt)]);
            var text = response.Messages.SelectMany(message => message.Contents).OfType<TextContent>()
                .Select(content => content.Text).FirstOrDefault();

            await Assert.That(text).IsEqualTo(ChatPrompt);
        }
        finally
        {
            DeleteFixture(fixture.FixtureRoot);
        }
    }

    [Test]
    public async Task InstallOrUpdate_UsesIsolatedSdkRootAndReturnsVerifiedLaunch()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var updates = await RunInstallationAsync(fixture);
            var completed = updates.Single(update => update.Stage == CliInstallationStage.Installed);
            var result = completed.Result!;

            await Assert.That(result.InstalledVersion).IsEqualTo(ClaudeCliCompatibility.TargetVersion);
            await Assert.That(result.TargetVersion).IsEqualTo(ClaudeCliCompatibility.TargetVersion);
            await Assert.That(result.InstallationRootPath).IsEqualTo(fixture.ExpectedInstallRoot);
            if (OperatingSystem.IsWindows())
            {
                await Assert.That(result.LaunchCommand.ExecutablePath)
                    .IsEqualTo(fixture.Options.PackageManagerExecutablePath);
                await Assert.That(result.LaunchCommand.PrefixArguments.Single()).IsEqualTo(Path.Combine(
                    fixture.ExpectedInstallRoot, NodeModulesDirectoryName, PackageScopeDirectoryName,
                    PackageDirectoryName, PackageEntryScript));
            }
            else
            {
                await Assert.That(result.LaunchCommand.ExecutablePath).IsEqualTo(Path.Combine(
                    fixture.ExpectedInstallRoot, NodeModulesDirectoryName, NpmBinDirectoryName, CliName));
            }
            await Assert.That(updates.Any(update => update.Stage == CliInstallationStage.OutputObserved &&
                update.StandardOutputCharactersObserved > 0 && update.StandardErrorCharactersObserved > 0)).IsTrue();
            await Assert.That(updates.All(update => update.Result is null || update.Stage == CliInstallationStage.Installed)).IsTrue();
        }
        finally
        {
            DeleteFixture(fixture.FixtureRoot);
        }
    }

    [Test]
    public async Task InstallOrUpdate_RejectsPackageManagerVersionMismatch()
    {
        var fixture = await CreateFixtureAsync();
        await File.WriteAllTextAsync(Path.Combine(fixture.NpmRoot, BinDirectoryName, MismatchMarkerName), string.Empty);
        try
        {
            var exception = await Assert.That(async () => await RunInstallationAsync(fixture)).ThrowsException();
            await Assert.That(exception).IsTypeOf<InvalidOperationException>();
            await Assert.That(exception!.Message).IsEqualTo(VersionMismatchMessage);
        }
        finally
        {
            DeleteFixture(fixture.FixtureRoot);
        }
    }

    [Test]
    public async Task InstallOrUpdate_StopsWhenOutputExceedsConfiguredLimit()
    {
        var fixture = await CreateFixtureAsync();
        await File.WriteAllTextAsync(Path.Combine(fixture.NpmRoot, BinDirectoryName, OverflowMarkerName), string.Empty);
        try
        {
            var exception = await Assert.That(async () => await RunInstallationAsync(fixture)).ThrowsException();
            await Assert.That(exception!.Message).IsEqualTo(OutputLimitMessage);
        }
        finally
        {
            DeleteFixture(fixture.FixtureRoot);
        }
    }

    [Test]
    public async Task InstallOrUpdate_CancellationStopsAndJoinsPackageManager()
    {
        var fixture = await CreateFixtureAsync();
        await File.WriteAllTextAsync(Path.Combine(fixture.NpmRoot, BinDirectoryName, HangMarkerName), string.Empty);
        using var cancellation = new CancellationTokenSource();
        try
        {
            await using var updates = ClaudeCliInstallation.InstallOrUpdateAsync(fixture.Options,
                fixture.LocalApplicationDataRoot, cancellation.Token).GetAsyncEnumerator();
            await Assert.That(await updates.MoveNextAsync()).IsTrue();
            await Assert.That(updates.Current.Stage).IsEqualTo(CliInstallationStage.PackageManagerStarted);
            var childPidPath = Path.Combine(fixture.NpmRoot, BinDirectoryName, ChildPidFileName);
            using var markerTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (!File.Exists(childPidPath))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), markerTimeout.Token);
            }

            var childPid = int.Parse(await File.ReadAllTextAsync(childPidPath), System.Globalization.CultureInfo.InvariantCulture);
            var competingOptions = fixture.Options with { InstallationLockTimeout = TimeSpan.FromMilliseconds(100) };
            var lockException = await Assert.That(async () =>
                await RunInstallationAsync(fixture, competingOptions)).ThrowsException();
            await Assert.That(lockException).IsTypeOf<TimeoutException>();
            cancellation.Cancel();

            var exception = await Assert.That(async () =>
            {
                while (await updates.MoveNextAsync())
                {
                }
            }).ThrowsException();
            await Assert.That(exception).IsTypeOf<OperationCanceledException>();
            await Assert.That(IsProcessRunning(childPid)).IsFalse();
        }
        finally
        {
            DeleteFixture(fixture.FixtureRoot);
        }
    }

    private static async Task<List<CliInstallationUpdate>> RunInstallationAsync(
        InstallationFixture fixture,
        CliInstallationOptions? options = null)
    {
        var updates = new List<CliInstallationUpdate>();
        await foreach (var update in ClaudeCliInstallation.InstallOrUpdateAsync(options ?? fixture.Options,
                           fixture.LocalApplicationDataRoot, CancellationToken.None))
        {
            updates.Add(update);
        }

        return updates;
    }

    private static async Task<InstallationFixture> CreateFixtureAsync()
    {
        var root = Path.Combine(Environment.CurrentDirectory, TestsDirectoryName, SandboxDirectoryName,
            Guid.NewGuid().ToString(GuidFormat));
        var npmRoot = Path.Combine(root, NpmDirectoryName);
        var npmBin = Path.Combine(npmRoot, BinDirectoryName);
        Directory.CreateDirectory(npmBin);
        var npmManifest = Path.Combine(npmRoot, PackageJsonFileName);
        var npmScript = Path.Combine(npmBin, NpmCliFileName);
        await File.WriteAllTextAsync(npmManifest,
            NpmPackageManifest.Replace(ScriptPlaceholderNpm, NpmPackageName, StringComparison.Ordinal));
        await File.WriteAllTextAsync(npmScript, NpmScript.Replace(ScriptPlaceholderPackage, PackageName, StringComparison.Ordinal)
            .Replace(ScriptPlaceholderCli, CliName, StringComparison.Ordinal)
            .Replace(ScriptPlaceholderNpm, NpmPackageName, StringComparison.Ordinal));
        var node = FindNode();
        var path = Path.GetDirectoryName(node)!;
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [PathName] = path };
        var systemRoot = Environment.GetEnvironmentVariable(SystemRootName);
        if (OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(systemRoot))
        {
            environment[SystemRootName] = systemRoot;
        }

        var localApplicationDataRoot = Path.Combine(root, LocalApplicationDataDirectoryName);
        var expectedRoot = Path.Combine(localApplicationDataRoot, ManagedCodeDirectoryName, SdkDirectoryName,
            InstallDirectoryName);
        var options = new CliInstallationOptions
        {
            PackageManager = CliPackageManager.Npm,
            PackageManagerExecutablePath = node,
            NpmCliScriptPath = npmScript,
            EnvironmentVariables = environment,
            InstallTimeout = TimeSpan.FromSeconds(10),
            ProcessTerminationTimeout = TimeSpan.FromSeconds(2),
            InstallationLockTimeout = TimeSpan.FromSeconds(2),
            MaximumMetadataFileCharacters = 4096,
            MaximumOutputCharacters = 4096
        };

        return new InstallationFixture(root, npmRoot, localApplicationDataRoot, expectedRoot, options);
    }

    private static string FindNode()
    {
        var executableName = OperatingSystem.IsWindows() ? NodeWindowsName : NodeName;
        var path = Environment.GetEnvironmentVariable(PathName) ?? string.Empty;
        var node = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, executableName)).FirstOrDefault(File.Exists);
        return Path.GetFullPath(node ??
            throw new InvalidOperationException(NodeRequiredMessage));
    }

    private static void DeleteFixture(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private sealed record InstallationFixture(
        string FixtureRoot,
        string NpmRoot,
        string LocalApplicationDataRoot,
        string ExpectedInstallRoot,
        CliInstallationOptions Options);
}
