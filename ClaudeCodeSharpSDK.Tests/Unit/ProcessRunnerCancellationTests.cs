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

    [Test]
    public async Task PublicExec_PreCanceledTokenDoesNotStartCliProcess()
    {
        var executablePath = Path.Combine(GetTestSandboxDirectory(), $"missing-claude-cli-{Guid.NewGuid():N}");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var exec = new ClaudeExec(false, TimeSpan.FromSeconds(5), executablePath, CreateEnvironment());

        await using var enumerator = exec.RunAsync(new ClaudeExecArgs
        {
            Input = "test",
            CancellationToken = cancellation.Token,
        }).GetAsyncEnumerator(cancellation.Token);

        var action = async () => await enumerator.MoveNextAsync();
        var exception = await Assert.That(action).ThrowsException();

        await Assert.That(exception).IsTypeOf<OperationCanceledException>();
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
        var invocation = new ClaudeProcessInvocation(
            "/bin/sh",
            Environment.CurrentDirectory,
            ["-c", DescendantHoldingStderrScript],
            CreateEnvironment(),
            string.Empty,
            TimeSpan.FromMilliseconds(terminationTimeoutMilliseconds));
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
                Input = "test",
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
            ? new ClaudeProcessInvocation("powershell.exe", Environment.CurrentDirectory, ["-NoProfile", "-NonInteractive", "-Command", "Write-Output $PID; Start-Sleep -Seconds 30"], CreateEnvironment(), string.Empty, TimeSpan.FromSeconds(5))
            : new ClaudeProcessInvocation("/bin/sh", Environment.CurrentDirectory, ["-c", "echo $$; exec /bin/sleep 30"], CreateEnvironment(), string.Empty, TimeSpan.FromSeconds(5));
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

    private static string CreateLongRunningCliScript()
    {
        var scriptPath = Path.Combine(GetTestSandboxDirectory(), $"claude-cli-{Guid.NewGuid():N}.sh");
        File.WriteAllText(scriptPath, LongRunningScript);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return scriptPath;
    }

    private static string GetTestSandboxDirectory()
    {
        var path = Path.Combine(Environment.CurrentDirectory, "tests", ".sandbox", "ProcessRunnerCancellationTests");
        Directory.CreateDirectory(path);
        return path;
    }
}
