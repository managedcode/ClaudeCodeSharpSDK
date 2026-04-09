using System.Diagnostics;
using ManagedCode.ClaudeCodeSharpSDK.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Models;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Unit;

public class ClaudeCliMetadataReaderTests
{
    private const string CommandFlagUnix = "-c";
    private const string CommandFlagWindows = "/c";
    private const string ConcurrentOutputCommandUnix =
        "i=0; while [ $i -lt 5000 ]; do printf 'stdout-line-%s\\n' \"$i\"; printf 'stderr-line-%s\\n' \"$i\" >&2; i=$((i+1)); done";
    private const string ConcurrentOutputCommandWindows =
        "for /L %i in (0,1,4999) do @echo stdout-line-%i & @echo stderr-line-%i 1>&2";
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
    private static readonly TimeSpan ProcessReadTimeout = TimeSpan.FromSeconds(10);

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

    private static void TryKillProcess(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }
}
