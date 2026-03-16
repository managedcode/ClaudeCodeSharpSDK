using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Execution;
using ManagedCode.ClaudeCodeSharpSDK.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Models;
using ManagedCode.ClaudeCodeSharpSDK.Tests.Shared;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Integration;

[Property(TestConstants.RequiresClaudeAuthPropertyName, TestConstants.TrueString)]
[RequiresAuthenticatedClaude]
public class ClaudeExecIntegrationTests
{
    private const string CliExitCodeMessageFragment = "exited with code";
    private const string FirstPrompt = "Reply with short plain text: first.";
    private const string InvalidModel = "__claudecodesharpsdk_invalid_model__";
    private const string SecondPrompt = "Reply with short plain text: second.";
    private static readonly TimeSpan ExecTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ThreadTimeout = TimeSpan.FromMinutes(3);

    [Test]
    public async Task RunAsync_UsesDefaultProcessRunner_EndToEnd()
    {
        var settings = RealClaudeTestSupport.GetRequiredSettings();

        var exec = new ClaudeExec(settings.ExecutablePath);
        using var cancellation = new CancellationTokenSource(ExecTimeout);

        var lines = await DrainToListAsync(exec.RunAsync(new ClaudeExecArgs
        {
            Input = FirstPrompt,
            Model = settings.Model,
            DangerouslySkipPermissions = true,
            NoSessionPersistence = true,
            CancellationToken = cancellation.Token,
        }));
        var parsedEvents = lines.Select(ThreadEventParser.Parse).ToList();

        await Assert.That(parsedEvents.Any(static threadEvent => threadEvent is ThreadStartedEvent)).IsTrue();
        await Assert.That(parsedEvents.Any(static threadEvent => threadEvent is TurnCompletedEvent)).IsTrue();
    }

    [Test]
    public async Task RunAsync_SecondCallPassesResumeArgument_EndToEnd()
    {
        var settings = RealClaudeTestSupport.GetRequiredSettings();

        using var client = RealClaudeTestSupport.CreateClient();
        using var cancellation = new CancellationTokenSource(ThreadTimeout);

        using var thread = client.StartThread(new ThreadOptions
        {
            Model = settings.Model,
            DangerouslySkipPermissions = true,
        });

        var firstResult = await thread.RunAsync(
            FirstPrompt,
            new TurnOptions { CancellationToken = cancellation.Token });

        var threadId = thread.Id;
        await Assert.That(threadId).IsNotNull();
        await Assert.That(firstResult.Usage).IsNotNull();

        var secondResult = await thread.RunAsync(
            SecondPrompt,
            new TurnOptions { CancellationToken = cancellation.Token });

        await Assert.That(secondResult.Usage).IsNotNull();
        await Assert.That(thread.Id).IsEqualTo(threadId);
    }

    [Test]
    public async Task RunAsync_PropagatesNonZeroExitCode_EndToEnd()
    {
        var settings = RealClaudeTestSupport.GetRequiredSettings();

        var exec = new ClaudeExec(settings.ExecutablePath);
        using var cancellation = new CancellationTokenSource(ExecTimeout);

        var action = async () => await DrainAsync(exec.RunAsync(new ClaudeExecArgs
        {
            Input = FirstPrompt,
            Model = InvalidModel,
            DangerouslySkipPermissions = true,
            NoSessionPersistence = true,
            CancellationToken = cancellation.Token,
        }));

        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains(CliExitCodeMessageFragment);
    }

    private static async Task DrainAsync(IAsyncEnumerable<string> lines)
    {
        await foreach (var _ in lines)
        {
            // Intentionally empty.
        }
    }

    private static async Task<List<string>> DrainToListAsync(IAsyncEnumerable<string> lines)
    {
        var result = new List<string>();

        await foreach (var line in lines)
        {
            result.Add(line);
        }

        return result;
    }
}
