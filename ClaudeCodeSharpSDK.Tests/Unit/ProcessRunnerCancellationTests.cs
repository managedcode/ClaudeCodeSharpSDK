using System.Diagnostics;
using System.Globalization;
using System.Text;
using ManagedCode.ClaudeCodeSharpSDK.Execution;
using ManagedCode.ClaudeCodeSharpSDK.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Unit;

[NotInParallel("CliProcess")]
public class ProcessRunnerCancellationTests
{
    private const string LongRunningScript = "#!/bin/sh\necho $$\nexec /bin/sleep 30\n";
    private const string DescendantHoldingStderrScript = "/usr/bin/setsid /bin/sleep 30 >&2 & echo $!; exit 0";
    private const string DescendantHoldingStderrWhileParentRunsScript =
        "/usr/bin/setsid /bin/sh -c '/usr/bin/setsid /bin/sleep 30 >&2 & echo $!; exit 0' & exec /bin/sleep 30";
    private const string PosixFixtureSkipReason = "The public CLI yield-boundary fixture currently uses a POSIX executable script.";
    private const string LinuxFixtureSkipReason = "The detached stderr-retention fixture requires Linux setsid.";
    private const string StderrClosureFailure = "Claude Code process and output cleanup could not be confirmed.";
    private const string ProcessOutputLimitMessage = "Claude Code process exceeded the configured output limit.";
    private const string ExpectedFirstLine = "first";
    private const string ExpectedSecondLine = "second";
    private const string PosixSingleLineOverflowCommand = "printf '%100s\\n' x; exec /bin/sleep 30";
    private const string PosixMultiLineOverflowCommand = "printf '1234567890\\n1234567890\\n1234567890\\n'";
    private const string PosixStandardErrorPressureCommand = "printf '%100s' x >&2; exec /bin/sleep 30";
    private const string PosixNormalMultiLineCommand = "printf 'first\\nsecond\\n'";
    private const string PosixNonZeroExitCommand = "printf 'provider failed\\n' >&2; exit 23";
    private const string PosixNonZeroExitBeforeInputCommand = "printf 'provider failed\\n' >&2; /bin/sleep 0.1; exit 23";
    private const string PosixZeroExitAfterClosingInputCommand = "exec 0<&-; /bin/sleep 0.1; exit 0";
    private const string NodeExecutableName = "node";
    private const string PosixLargePromptPipePressureCommand =
        "head -c 262144 /dev/zero | tr '\\0' s; printf '\\n'; head -c 262144 /dev/zero | tr '\\0' e >&2; cat";
    private const string WindowsLargePromptPipePressureCommand =
        "[Console]::Out.WriteLine('s' * 262144); [Console]::Error.WriteLine('e' * 262144); $inputText = [Console]::In.ReadToEnd(); [Console]::Out.Write($inputText)";
    private const string PromptLineSeparator = "\n";
    private const string YoloFlag = "--yolo";
    private const string PromptCharacter = "p";
    private const string StandardOutputPressureCharacter = "s";
    private const int PipePressureCharacters = 262144;
    private const int LargePromptCharacters = 262144;
    private const int StdinClosePressureCharacters = 16777216;
    private const int LargeProcessOutputCharacters = 1048576;
    private const string WindowsLongRunningCommand = "Write-Output $PID; Start-Sleep -Seconds 30";
    private const string WindowsSingleLineOverflowCommand = "Write-Output ('x' * 100); Start-Sleep -Seconds 30";
    private const string WindowsMultiLineOverflowCommand = "Write-Output '1234567890'; Write-Output '1234567890'; Write-Output '1234567890'";
    private const string WindowsStandardErrorPressureCommand = "[Console]::Error.Write('x' * 100); Start-Sleep -Seconds 30";
    private const string WindowsNormalMultiLineCommand = "Write-Output 'first'; Write-Output 'second'";
    private const string WindowsNonZeroExitCommand = "[Console]::Error.WriteLine('provider failed'); exit 23";
    private const string WindowsNonZeroExitBeforeInputCommand = "[Console]::Error.WriteLine('provider failed'); Start-Sleep -Milliseconds 100; exit 23";
    private const string WindowsZeroExitAfterClosingInputCommand = "[Console]::OpenStandardInput().Dispose(); Start-Sleep -Milliseconds 100; exit 0";
    private const string WindowsClosingInputFixtureCommand = "Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class CliTestNativeInput { [DllImport(\"kernel32.dll\")] public static extern bool CloseHandle(IntPtr handle); [DllImport(\"kernel32.dll\")] public static extern IntPtr GetStdHandle(int handle); }'; $ready=$args[0]; $release=$args[1]; $closed=$args[2]; [IO.File]::WriteAllText($ready,[string]$PID); Write-Output 'ready'; while(-not (Test-Path -LiteralPath $release)){Start-Sleep -Milliseconds 10}; [CliTestNativeInput]::CloseHandle([CliTestNativeInput]::GetStdHandle(-10)); [IO.File]::WriteAllText($closed,[string]$PID); Write-Output 'closed'; Start-Sleep -Seconds 30";
    private const string WindowsNodeExecutableName = "node.exe";
    private const string NodeEvaluationFlag = "-e";
    private const string NodeReadyLine = "ready";
    private const string NodeHandleClosedLine = "closed";
    private const string NodeMissingMessage = "Node.js was not found on PATH.";
    private const string UnexpectedFixtureOutputMessage = "The Node.js child fixture emitted an unexpected protocol line.";
    private const string RootExitedBeforeStdinCloseWasObservedMessage =
        "CLI root PID {0} exited before the stdin-close callback was observed (HasExited={1}, process-termination-timeout={2}).";
    private static readonly CompositeFormat RootExitedBeforeStdinCloseWasObservedFormat =
        CompositeFormat.Parse(RootExitedBeforeStdinCloseWasObservedMessage);
    private const string FixtureGuidFormat = "N";
    private const string ReadyFileExtension = ".ready";
    private const string ReleaseFileExtension = ".release";
    private const string ClosedFileExtension = ".closed";
    private const string EmptyFileContent = "";
    private const string NodeClosingInputFixture = "const fs=require('node:fs');const ready=process.argv[1],release=process.argv[2],closed=process.argv[3];fs.writeFileSync(ready,String(process.pid));console.log('ready');const timer=setInterval(()=>{if(fs.existsSync(release)){clearInterval(timer);process.stdin._handle.close(error=>{if(error){process.exitCode=1;return;}fs.closeSync(0);fs.writeFileSync(closed,String(process.pid));console.log('closed');});}},10);setTimeout(()=>{},30000);";
    private const string SystemRootVariableName = "SystemRoot";
    private const string WindowsDirectoryVariableName = "WINDIR";
    private const string PathVariableName = "PATH";
    private const string PathExtensionsVariableName = "PATHEXT";
    private const string TestInput = "test";
    private const string MissingExecutableNamePrefix = "missing-claude-cli-";
    private const string ScriptFileNamePrefix = "claude-cli-";
    private const string ShellScriptExtension = ".sh";
    private const string PosixShellPath = "/bin/sh";
    private const string PosixShellCommandFlag = "-c";
    private const string PosixLongRunningCommand = "echo $$; exec /bin/sleep 30";
    private const string WindowsPowerShellPath = "powershell.exe";
    private const string PowerShellNoProfileFlag = "-NoProfile";
    private const string PowerShellNonInteractiveFlag = "-NonInteractive";
    private const string PowerShellCommandFlag = "-Command";
    private const string EmptyStandardInput = "";
    private const string TestsDirectoryName = "tests";
    private const string SandboxDirectoryName = ".sandbox";
    private const string FixtureDirectoryName = "ProcessRunnerCancellationTests";
    private const int SmallOutputLimitCharacters = 64;
    private const int AggregateOutputLimitCharacters = 24;
    private static readonly TimeSpan WindowsFixtureStartupTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan WindowsFixtureCompletionTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WindowsFixturePollInterval = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan WindowsCleanupAssertionBound = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ClosedInputProcessTerminationTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ProcessOutputCleanupAssertionBound = TimeSpan.FromSeconds(8);

    [Test]
    public async Task DefaultRunner_DrainsBothOutputPipesWhileWritingLargePromptToStandardInput()
    {
        var prompt = string.Concat(YoloFlag, PromptLineSeparator, new string(PromptCharacter[0], LargePromptCharacters));
        var invocation = CreateOutputInvocation(
            OperatingSystem.IsWindows() ? WindowsLargePromptPipePressureCommand : PosixLargePromptPipePressureCommand,
            TimeSpan.FromSeconds(10), LargeProcessOutputCharacters,
            input: prompt);
        var lines = new List<string>();

        await foreach (var line in new DefaultClaudeProcessRunner().RunAsync(invocation, NullLogger.Instance, CancellationToken.None))
        {
            lines.Add(line);
        }

        await Assert.That(lines).Count().IsEqualTo(3);
        await Assert.That(lines[0].Length).IsEqualTo(PipePressureCharacters);
        await Assert.That(lines[0].All(character => character == StandardOutputPressureCharacter[0])).IsTrue();
        await Assert.That(lines[1]).IsEqualTo(YoloFlag);
        await Assert.That(lines[2].Length).IsEqualTo(LargePromptCharacters);
        await Assert.That(lines[2].All(character => character == PromptCharacter[0])).IsTrue();
    }

    [Test]
    public async Task CancellationWithDescendantHoldingStderrSurfacesUnconfirmedCleanup()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test(LinuxFixtureSkipReason);
            return;
        }

        using var cancellation = new CancellationTokenSource();
        var standardErrorReaderCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var standardOutputReadCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocation = new ClaudeProcessInvocation(
            PosixShellPath,
            Environment.CurrentDirectory,
            [PosixShellCommandFlag, DescendantHoldingStderrWhileParentRunsScript],
            CreateEnvironment(),
            EmptyStandardInput,
            ClosedInputProcessTerminationTimeout)
        {
            StandardErrorReaderCompleted = () => standardErrorReaderCompleted.TrySetResult(),
            StandardOutputReadCompleted = () => standardOutputReadCompleted.TrySetResult(),
        };
        var runner = new DefaultClaudeProcessRunner();
        await using var enumerator = runner.RunAsync(invocation, NullLogger.Instance, cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);
        var childProcessId = 0;

        try
        {
            await Assert.That(await enumerator.MoveNextAsync()).IsTrue();
            childProcessId = int.Parse(enumerator.Current, CultureInfo.InvariantCulture);
            cancellation.Cancel();

            var action = async () => await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            var exception = await Assert.That(action).ThrowsException();

            await Assert.That(exception).IsTypeOf<InvalidOperationException>();
            await Assert.That(exception!.Message).Contains(StderrClosureFailure);
            await standardOutputReadCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            await standardErrorReaderCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            try
            {
                using var child = Process.GetProcessById(childProcessId);
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (ArgumentException)
            {
                // The detached fixture child already exited.
            }
        }
    }

    [Test]
    public async Task PublicExec_PreCanceledTokenDoesNotStartCliProcess()
    {
        var executablePath = Path.Combine(GetTestSandboxDirectory(), $"{MissingExecutableNamePrefix}{Guid.NewGuid():N}");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var exec = new ClaudeExec(false, TimeSpan.FromSeconds(5), executablePath, CreateEnvironment());

        await using var enumerator = exec.RunAsync(new ClaudeExecArgs
        {
            Input = TestInput,
            CancellationToken = cancellation.Token,
        }).GetAsyncEnumerator(cancellation.Token);

        var action = async () => await enumerator.MoveNextAsync();
        var exception = await Assert.That(action).ThrowsException();

        await Assert.That(exception).IsTypeOf<OperationCanceledException>();
    }

    [Test]
    public async Task DefaultRunner_PreservesMultilineOutputWithinConfiguredBudget()
    {
        var invocation = CreateOutputInvocation(
            OperatingSystem.IsWindows() ? WindowsNormalMultiLineCommand : PosixNormalMultiLineCommand,
            TimeSpan.FromSeconds(5), SmallOutputLimitCharacters);
        var runner = new DefaultClaudeProcessRunner();
        var lines = new List<string>();

        await foreach (var line in runner.RunAsync(invocation, NullLogger.Instance, CancellationToken.None))
        {
            lines.Add(line);
        }

        await Assert.That(lines).Count().IsEqualTo(2);
        await Assert.That(lines[0]).IsEqualTo(ExpectedFirstLine);
        await Assert.That(lines[1]).IsEqualTo(ExpectedSecondLine);
    }

    [Test]
    public async Task DefaultRunner_NonZeroExitConfirmsRootAndNaturalOutputCompletion()
    {
        var invocation = CreateOutputInvocation(
            OperatingSystem.IsWindows() ? WindowsNonZeroExitCommand : PosixNonZeroExitCommand,
            TimeSpan.FromSeconds(5), SmallOutputLimitCharacters);
        var action = async () =>
        {
            await foreach (var _ in new DefaultClaudeProcessRunner().RunAsync(invocation, NullLogger.Instance, CancellationToken.None))
            {
            }
        };

        var exception = await Assert.That(action).ThrowsException();

        await Assert.That(exception).IsTypeOf<CliExecutionFailureException>();
        await Assert.That(((CliExecutionFailureException)exception!).ExitCode).IsEqualTo(23);
        await Assert.That(((CliExecutionFailureException)exception).RootProcessExitConfirmed).IsTrue();
    }

    [Test]
    public async Task DefaultRunner_NonZeroExitWithCompletedBrokenPipeKeepsConfirmedFailure()
    {
        var prompt = new string(PromptCharacter[0], LargePromptCharacters);
        var invocation = CreateOutputInvocation(
            OperatingSystem.IsWindows() ? WindowsNonZeroExitBeforeInputCommand : PosixNonZeroExitBeforeInputCommand,
            TimeSpan.FromSeconds(5), LargeProcessOutputCharacters, input: prompt);
        var action = async () =>
        {
            await foreach (var _ in new DefaultClaudeProcessRunner().RunAsync(invocation, NullLogger.Instance, CancellationToken.None))
            {
            }
        };

        var exception = await Assert.That(action).ThrowsException();

        await Assert.That(exception).IsTypeOf<CliExecutionFailureException>();
        await Assert.That(((CliExecutionFailureException)exception!).ExitCode).IsEqualTo(23);
        await Assert.That(((CliExecutionFailureException)exception).RootProcessExitConfirmed).IsTrue();
    }

    [Test]
    public async Task DefaultRunner_ZeroExitAfterClosingInputRemainsAnInputFailure()
    {
        var prompt = new string(PromptCharacter[0], LargePromptCharacters);
        var invocation = CreateOutputInvocation(
            OperatingSystem.IsWindows() ? WindowsZeroExitAfterClosingInputCommand : PosixZeroExitAfterClosingInputCommand,
            TimeSpan.FromSeconds(5), LargeProcessOutputCharacters, input: prompt);
        var action = async () =>
        {
            await foreach (var _ in new DefaultClaudeProcessRunner().RunAsync(invocation, NullLogger.Instance, CancellationToken.None))
            {
            }
        };

        var exception = await Assert.That(action).ThrowsException();

        await Assert.That(exception).IsTypeOf<IOException>();
        await Assert.That(exception).IsNotTypeOf<CliExecutionFailureException>();
    }

    [Test]
    public async Task DefaultRunner_StillRunningAfterClosingInputIsBoundedAndUnconfirmed()
    {
        await Runner_StillRunningAfterClosingInputIsBoundedAndUnconfirmed();
    }

    private static async Task Runner_StillRunningAfterClosingInputIsBoundedAndUnconfirmed()
    {
        var sandbox = GetTestSandboxDirectory();
        var fixtureId = Guid.NewGuid().ToString(FixtureGuidFormat, CultureInfo.InvariantCulture);
        var readyPath = Path.Combine(sandbox, string.Concat(fixtureId, ReadyFileExtension));
        var releasePath = Path.Combine(sandbox, string.Concat(fixtureId, ReleaseFileExtension));
        var closedPath = Path.Combine(sandbox, string.Concat(fixtureId, ClosedFileExtension));
        var stdinFailureObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var executablePath = OperatingSystem.IsWindows() ? WindowsPowerShellPath : FindNodeExecutablePath();
        string[] arguments = OperatingSystem.IsWindows()
            ? [PowerShellNoProfileFlag, PowerShellNonInteractiveFlag, PowerShellCommandFlag, WindowsClosingInputFixtureCommand, readyPath, releasePath, closedPath]
            : [NodeEvaluationFlag, NodeClosingInputFixture, readyPath, releasePath, closedPath];
        var invocation = new ClaudeProcessInvocation(
            executablePath,
            sandbox,
            arguments,
            OperatingSystem.IsWindows() ? CreateWindowsProcessEnvironment() : CreateEnvironment(),
            new string(PromptCharacter[0], StdinClosePressureCharacters),
            TimeSpan.FromMilliseconds(250))
        {
            MaximumProcessOutputCharacters = LargeProcessOutputCharacters,
            StandardInputWriteFailed = rootExited => stdinFailureObserved.TrySetResult(rootExited),
        };
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = new DefaultClaudeProcessRunner()
            .RunAsync(invocation, NullLogger.Instance, cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);
        Task? consumeTask = null;

        try
        {
            await Assert.That(await enumerator.MoveNextAsync().AsTask().WaitAsync(WindowsFixtureStartupTimeout)).IsTrue();
            await Assert.That(enumerator.Current).IsEqualTo(NodeReadyLine);
            await Assert.That(File.Exists(readyPath)).IsTrue();
            File.WriteAllText(releasePath, EmptyFileContent);

            consumeTask = ConsumeAsync(enumerator);
            await WaitForFileAsync(closedPath, WindowsFixtureStartupTimeout);
            await stdinFailureObserved.Task.WaitAsync(WindowsFixtureStartupTimeout);
            await Assert.That(await stdinFailureObserved.Task).IsFalse();
            var processId = int.Parse(File.ReadAllText(closedPath), CultureInfo.InvariantCulture);
            using var child = Process.GetProcessById(processId);
            if (child.HasExited)
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture,
                    RootExitedBeforeStdinCloseWasObservedFormat,
                    processId,
                    child.HasExited,
                    ClosedInputProcessTerminationTimeout));
            }

            var stopwatch = Stopwatch.StartNew();
            var observedTask = CaptureExceptionAsync(consumeTask);
            using var assertionTimeout = new CancellationTokenSource(WindowsFixtureCompletionTimeout);
            var completedTask = await Task.WhenAny(observedTask, Task.Delay(Timeout.InfiniteTimeSpan, assertionTimeout.Token));
            stopwatch.Stop();

            await Assert.That(ReferenceEquals(completedTask, observedTask)).IsTrue();
            var exception = await observedTask;
            await Assert.That(exception).IsTypeOf<TimeoutException>();
            await Assert.That(exception).IsNotTypeOf<CliExecutionFailureException>();
            await Assert.That(stopwatch.Elapsed < WindowsCleanupAssertionBound).IsTrue();
        }
        finally
        {
            cancellation.Cancel();
            if (!File.Exists(releasePath))
            {
                File.WriteAllText(releasePath, EmptyFileContent);
            }

            if (consumeTask is not null)
            {
                var cleanupTask = CaptureExceptionAsync(consumeTask);
                using var cleanupTimeout = new CancellationTokenSource(WindowsFixtureCompletionTimeout);
                var completedTask = await Task.WhenAny(
                    cleanupTask,
                    Task.Delay(Timeout.InfiniteTimeSpan, cleanupTimeout.Token));
                await Assert.That(ReferenceEquals(completedTask, cleanupTask)).IsTrue();
            }

            File.Delete(readyPath);
            File.Delete(releasePath);
            File.Delete(closedPath);
        }
    }

    private static async Task ConsumeAsync(IAsyncEnumerator<string> enumerator)
    {
        while (await enumerator.MoveNextAsync().ConfigureAwait(false))
        {
            if (!string.Equals(enumerator.Current, NodeHandleClosedLine, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(UnexpectedFixtureOutputMessage);
            }
        }
    }

    private static async Task<Exception?> CaptureExceptionAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task WaitForFileAsync(string path, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!File.Exists(path))
        {
            await Task.Delay(WindowsFixturePollInterval, cancellation.Token).ConfigureAwait(false);
        }
    }

    private static string FindNodeExecutablePath()
    {
        var path = Environment.GetEnvironmentVariable(PathVariableName) ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, OperatingSystem.IsWindows() ? WindowsNodeExecutableName : NodeExecutableName);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new InvalidOperationException(NodeMissingMessage);
    }

    [Test]
    public async Task DefaultRunner_RejectsSingleLineAboveConfiguredBudget()
    {
        var invocation = CreateOutputInvocation(
            OperatingSystem.IsWindows() ? WindowsSingleLineOverflowCommand : PosixSingleLineOverflowCommand,
            TimeSpan.FromSeconds(2), SmallOutputLimitCharacters);
        var runner = new DefaultClaudeProcessRunner();
        var action = async () =>
        {
            await foreach (var _ in runner.RunAsync(invocation, NullLogger.Instance, CancellationToken.None))
            {
            }
        };

        var exception = await Assert.That(action).ThrowsException();

        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains(ProcessOutputLimitMessage);
    }

    [Test]
    public async Task DefaultRunner_RejectsAggregateMultilineOutputAboveConfiguredBudget()
    {
        var invocation = CreateOutputInvocation(
            OperatingSystem.IsWindows() ? WindowsMultiLineOverflowCommand : PosixMultiLineOverflowCommand,
            TimeSpan.FromSeconds(2), AggregateOutputLimitCharacters);
        var runner = new DefaultClaudeProcessRunner();
        var action = async () =>
        {
            await foreach (var _ in runner.RunAsync(invocation, NullLogger.Instance, CancellationToken.None))
            {
            }
        };

        var exception = await Assert.That(action).ThrowsException();

        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains(ProcessOutputLimitMessage);
    }

    [Test]
    public async Task DefaultRunner_StandardErrorPressureStopsQuietRootWithinBound()
    {
        var stopwatch = new Stopwatch();
        var invocation = CreateOutputInvocation(
            OperatingSystem.IsWindows() ? WindowsStandardErrorPressureCommand : PosixStandardErrorPressureCommand,
            TimeSpan.FromSeconds(2), SmallOutputLimitCharacters, stopwatch.Start);
        var runner = new DefaultClaudeProcessRunner();
        var action = async () =>
        {
            await foreach (var _ in runner.RunAsync(invocation, NullLogger.Instance, CancellationToken.None))
            {
            }
        };

        var exception = await Assert.That(action).ThrowsException();
        stopwatch.Stop();

        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains(ProcessOutputLimitMessage);
        await Assert.That(stopwatch.Elapsed < ProcessOutputCleanupAssertionBound).IsTrue();
    }

    [Test]
    public async Task RootExitWithDescendantHoldingStderr_FailsWithinConfiguredBound()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test(LinuxFixtureSkipReason);
            return;
        }

        const int terminationTimeoutMilliseconds = 250;
        var standardErrorReaderCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var standardOutputReadCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocation = new ClaudeProcessInvocation(
            PosixShellPath,
            Environment.CurrentDirectory,
            [PosixShellCommandFlag, DescendantHoldingStderrScript],
            CreateEnvironment(),
            string.Empty,
            TimeSpan.FromMilliseconds(terminationTimeoutMilliseconds))
        {
            StandardErrorReaderCompleted = () => standardErrorReaderCompleted.TrySetResult(),
            StandardOutputReadCompleted = () => standardOutputReadCompleted.TrySetResult(),
        };
        var runner = new DefaultClaudeProcessRunner();
        await using var enumerator = runner.RunAsync(invocation, NullLogger.Instance, CancellationToken.None)
            .GetAsyncEnumerator();
        var childProcessId = 0;

        try
        {
            await Assert.That(await enumerator.MoveNextAsync()).IsTrue();
            childProcessId = int.Parse(enumerator.Current, CultureInfo.InvariantCulture);
            var stopwatch = Stopwatch.StartNew();
            var action = async () => await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            var exception = await Assert.That(action).ThrowsException();
            stopwatch.Stop();

            await Assert.That(exception).IsTypeOf<InvalidOperationException>();
            await Assert.That(exception!.Message).Contains(StderrClosureFailure);
            await Assert.That(stopwatch.Elapsed < TimeSpan.FromSeconds(2)).IsTrue();
            await standardOutputReadCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            await standardErrorReaderCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            try
            {
                using var child = Process.GetProcessById(childProcessId);
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (ArgumentException)
            {
                // The detached fixture child already exited.
            }
        }
    }

    [Test]
    public async Task PublicExec_CancellationBetweenYieldedLinesIsNotReportedAsSuccess()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip.Test(PosixFixtureSkipReason);
            return;
        }

        var scriptPath = CreateLongRunningCliScript();
        using var cancellation = new CancellationTokenSource();
        var exec = new ClaudeExec(
            inheritEnvironmentVariables: false,
            processTerminationTimeout: TimeSpan.FromSeconds(5),
            executablePath: scriptPath,
            environmentOverride: CreateEnvironment());

        try
        {
            await using var enumerator = exec.RunAsync(new ClaudeExecArgs
            {
                Input = TestInput,
                CancellationToken = cancellation.Token,
            }).GetAsyncEnumerator(cancellation.Token);

            await Assert.That(await enumerator.MoveNextAsync()).IsTrue();
            var processId = int.Parse(enumerator.Current, CultureInfo.InvariantCulture);
            using var process = Process.GetProcessById(processId);
            cancellation.Cancel();

            var action = async () => await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            var exception = await Assert.That(action).ThrowsException();

            await Assert.That(exception).IsTypeOf<OperationCanceledException>();
            await Assert.That(process.HasExited).IsTrue();
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    [Test]
    public async Task Cancellation_WaitsForStartedProcessToExit()
    {
        using var cancellation = new CancellationTokenSource();
        var invocation = OperatingSystem.IsWindows()
            ? new ClaudeProcessInvocation(WindowsPowerShellPath, Environment.CurrentDirectory, [PowerShellNoProfileFlag, PowerShellNonInteractiveFlag, PowerShellCommandFlag, WindowsLongRunningCommand], CreateWindowsProcessEnvironment(), EmptyStandardInput, TimeSpan.FromSeconds(5))
            : new ClaudeProcessInvocation(PosixShellPath, Environment.CurrentDirectory, [PosixShellCommandFlag, PosixLongRunningCommand], CreateEnvironment(), EmptyStandardInput, TimeSpan.FromSeconds(5));
        var runner = new DefaultClaudeProcessRunner();

        await using var enumerator = runner.RunAsync(invocation, NullLogger.Instance, cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        await Assert.That(await enumerator.MoveNextAsync()).IsTrue();
        var processId = int.Parse(enumerator.Current, CultureInfo.InvariantCulture);
        using var process = Process.GetProcessById(processId);

        cancellation.Cancel();

        var action = async () => await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        var exception = await Assert.That(action).ThrowsException();

        await Assert.That(exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(process.HasExited).IsTrue();
    }

    private static Dictionary<string, string> CreateEnvironment()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static ClaudeProcessInvocation CreateOutputInvocation(
        string command,
        TimeSpan processTerminationTimeout,
        int maximumProcessOutputCharacters,
        Action? standardErrorOutputLimitExceeded = null,
        string? input = null)
    {
        return new ClaudeProcessInvocation(
            OperatingSystem.IsWindows() ? WindowsPowerShellPath : PosixShellPath,
            Environment.CurrentDirectory,
            OperatingSystem.IsWindows()
                ? [PowerShellNoProfileFlag, PowerShellNonInteractiveFlag, PowerShellCommandFlag, command]
                : [PosixShellCommandFlag, command],
            OperatingSystem.IsWindows() ? CreateWindowsProcessEnvironment() : CreateEnvironment(),
            input ?? EmptyStandardInput,
            processTerminationTimeout)
        {
            MaximumProcessOutputCharacters = maximumProcessOutputCharacters,
            StandardErrorOutputLimitExceeded = standardErrorOutputLimitExceeded,
        };
    }

    private static Dictionary<string, string> CreateWindowsProcessEnvironment()
    {
        var environment = CreateEnvironment();
        foreach (var variableName in new[]
        {
            SystemRootVariableName,
            WindowsDirectoryVariableName,
            PathVariableName,
            PathExtensionsVariableName,
        })
        {
            var value = Environment.GetEnvironmentVariable(variableName);
            if (!string.IsNullOrEmpty(value))
            {
                environment[variableName] = value;
            }
        }

        return environment;
    }

    private static string CreateLongRunningCliScript()
    {
        var scriptPath = Path.Combine(GetTestSandboxDirectory(), $"{ScriptFileNamePrefix}{Guid.NewGuid():N}{ShellScriptExtension}");
        File.WriteAllText(scriptPath, LongRunningScript);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return scriptPath;
    }

    private static string GetTestSandboxDirectory()
    {
        var path = Path.Combine(Environment.CurrentDirectory, TestsDirectoryName, SandboxDirectoryName, FixtureDirectoryName);
        Directory.CreateDirectory(path);
        return path;
    }
}
