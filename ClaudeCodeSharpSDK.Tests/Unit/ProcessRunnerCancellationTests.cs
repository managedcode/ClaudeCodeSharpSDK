using System.Diagnostics;
using System.Globalization;
using ManagedCode.ClaudeCodeSharpSDK.Execution;
using Microsoft.Extensions.Logging.Abstractions;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Unit;

public class ProcessRunnerCancellationTests
{
    private const string LongRunningScript = "#!/bin/sh\necho $$\nexec /bin/sleep 30\n";
    private const string DescendantHoldingStderrScript = "/usr/bin/setsid /bin/sleep 30 >&2 & echo $!; exit 0";
    private const string PosixFixtureSkipReason = "The public CLI yield-boundary fixture currently uses a POSIX executable script.";
    private const string LinuxFixtureSkipReason = "The detached stderr-retention fixture requires Linux setsid.";
    private const string StderrClosureFailure = "stderr stream closed";
    private const string ProcessOutputLimitMessage = "Claude Code process exceeded the configured output limit.";
    private const string ExpectedFirstLine = "first";
    private const string ExpectedSecondLine = "second";
    private const string PosixSingleLineOverflowCommand = "printf '%100s\\n' x; exec /bin/sleep 30";
    private const string PosixMultiLineOverflowCommand = "printf '1234567890\\n1234567890\\n1234567890\\n'";
    private const string PosixStandardErrorPressureCommand = "printf '%100s' x >&2; exec /bin/sleep 30";
    private const string PosixNormalMultiLineCommand = "printf 'first\\nsecond\\n'";
    private const string WindowsLongRunningCommand = "Write-Output $PID; Start-Sleep -Seconds 30";
    private const string WindowsSingleLineOverflowCommand = "Write-Output ('x' * 100); Start-Sleep -Seconds 30";
    private const string WindowsMultiLineOverflowCommand = "Write-Output '1234567890'; Write-Output '1234567890'; Write-Output '1234567890'";
    private const string WindowsStandardErrorPressureCommand = "[Console]::Error.Write('x' * 100); Start-Sleep -Seconds 30";
    private const string WindowsNormalMultiLineCommand = "Write-Output 'first'; Write-Output 'second'";
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
        var invocation = CreateOutputInvocation(
            OperatingSystem.IsWindows() ? WindowsStandardErrorPressureCommand : PosixStandardErrorPressureCommand,
            TimeSpan.FromSeconds(2), SmallOutputLimitCharacters);
        var runner = new DefaultClaudeProcessRunner();
        var stopwatch = Stopwatch.StartNew();
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
        await Assert.That(stopwatch.Elapsed < TimeSpan.FromSeconds(5)).IsTrue();
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
        int maximumProcessOutputCharacters)
    {
        return new ClaudeProcessInvocation(
            OperatingSystem.IsWindows() ? WindowsPowerShellPath : PosixShellPath,
            Environment.CurrentDirectory,
            OperatingSystem.IsWindows()
                ? [PowerShellNoProfileFlag, PowerShellNonInteractiveFlag, PowerShellCommandFlag, command]
                : [PosixShellCommandFlag, command],
            OperatingSystem.IsWindows() ? CreateWindowsProcessEnvironment() : CreateEnvironment(),
            EmptyStandardInput,
            processTerminationTimeout)
        {
            MaximumProcessOutputCharacters = maximumProcessOutputCharacters,
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
