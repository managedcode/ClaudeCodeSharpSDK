using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Configuration;
using ManagedCode.ClaudeCodeSharpSDK.Extensions.AI;
using ManagedCode.ClaudeCodeSharpSDK.Models;
using ManagedCode.ClaudeCodeSharpSDK.Tests.Shared;
using Microsoft.Extensions.AI;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Integration;

[Property(TestConstants.RequiresClaudeAuthPropertyName, TestConstants.TrueString)]
[RequiresAuthenticatedClaude]
public class RealClaudeIntegrationTests
{
    private const string AgainPlainTextOkPrompt = "Again: reply with short plain text: ok.";
    private const string PlainTextOkPrompt = "Reply with short plain text: ok.";
    private static readonly TimeSpan PersistenceDetectionTimeout = TimeSpan.FromSeconds(10);
    private const string StatusOnlyPrompt =
        "Return only a compact JSON object that matches the required schema with status exactly \"ok\". Do not include markdown, prose, or code fences.";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan TwoTurnTimeout = TimeSpan.FromMinutes(3);

    [Test]
    public async Task GetStreamingResponseAsync_NativeFileToolsCommitVisibleWorkspaceEffect()
    {
        var settings = RealClaudeTestSupport.GetRequiredSettings();
        var workspace = Path.Combine(RealClaudeTestSupport.ResolveRepositoryRootPath(), "tests", ".sandbox", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            using var client = new ClaudeChatClient(new ClaudeChatClientOptions
            {
                ClaudeOptions = new ClaudeOptions { ClaudeExecutablePath = settings.ExecutablePath },
                DefaultModel = settings.Model,
                DefaultThreadOptions = new ThreadOptions
                {
                    Model = settings.Model,
                    WorkingDirectory = workspace,
                    PermissionMode = PermissionMode.AcceptEdits,
                    NoSessionPersistence = true,
                },
            });
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var terminal = false;
            var activity = false;
            var textChunks = 0;
            await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User,
                "Use the Write tool to create native-tool-result.txt containing SDK_TOOL_OK followed by exactly one newline. Use Read to verify it, then explain in 20 short numbered lines how the change was verified. Do not change any other files.")], cancellationToken: timeout.Token))
            {
                if (update.Text.Length > 0)
                { textChunks++; }
                activity |= update.AdditionalProperties?.ContainsKey("managedcode:activity") is true;
                terminal |= update.FinishReason == ChatFinishReason.Stop;
            }
            await Assert.That(terminal).IsTrue();
            await Assert.That(activity).IsTrue();
            await Assert.That(textChunks).IsGreaterThan(1);
            await Assert.That(await File.ReadAllTextAsync(Path.Combine(workspace, "native-tool-result.txt"), timeout.Token))
                .IsEqualTo("SDK_TOOL_OK\n");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Test]
    public async Task RunAsync_WithRealClaudeCli_ReturnsStructuredOutput()
    {
        var settings = RealClaudeTestSupport.GetRequiredSettings();

        using var client = RealClaudeTestSupport.CreateClient();
        using var thread = StartRealIntegrationThread(client, settings.Model);
        using var cancellation = new CancellationTokenSource(TestTimeout);
        var schema = IntegrationOutputSchemas.StatusOnly();

        var result = await thread.RunAsync<StatusResponse>(
            StatusOnlyPrompt,
            schema,
            IntegrationOutputJsonContext.Default.StatusResponse,
            cancellation.Token);

        await Assert.That(result.TypedResponse.Status).IsEqualTo(TestConstants.OkStatusValue);
        await Assert.That(result.Usage).IsNotNull();
    }

    [Test]
    public async Task RunStreamedAsync_WithRealClaudeCli_YieldsCompletedTurnEvent()
    {
        var settings = RealClaudeTestSupport.GetRequiredSettings();

        using var client = RealClaudeTestSupport.CreateClient();
        using var thread = StartRealIntegrationThread(client, settings.Model);
        using var cancellation = new CancellationTokenSource(TestTimeout);

        var streamed = await thread.RunStreamedAsync(
            PlainTextOkPrompt,
            new TurnOptions { CancellationToken = cancellation.Token });

        var hasTurnCompleted = false;
        var hasTurnFailed = false;
        var hasCompletedItem = false;
        var hasTextDelta = false;

        await foreach (var threadEvent in streamed.Events.WithCancellation(cancellation.Token))
        {
            hasTurnCompleted |= threadEvent is TurnCompletedEvent;
            hasTurnFailed |= threadEvent is TurnFailedEvent;
            hasCompletedItem |= threadEvent is ItemCompletedEvent;
            hasTextDelta |= threadEvent is AssistantTextDeltaEvent;
        }

        await Assert.That(hasCompletedItem).IsTrue();
        await Assert.That(hasTextDelta).IsTrue();
        await Assert.That(hasTurnCompleted).IsTrue();
        await Assert.That(hasTurnFailed).IsFalse();
        await Assert.That(thread.Id).IsNotNull();
    }

    [Test]
    public async Task RunAsync_WithRealClaudeCli_SecondTurnKeepsThreadId()
    {
        var settings = RealClaudeTestSupport.GetRequiredSettings();

        using var client = RealClaudeTestSupport.CreateClient();
        using var thread = StartRealIntegrationThread(client, settings.Model);
        using var cancellation = new CancellationTokenSource(TwoTurnTimeout);

        var first = await thread.RunAsync(
            PlainTextOkPrompt,
            new TurnOptions { CancellationToken = cancellation.Token });

        var firstThreadId = thread.Id;
        await Assert.That(firstThreadId).IsNotNull();
        await Assert.That(first.Usage).IsNotNull();
        await Assert.That(first.FinalResponse).IsNotEmpty();

        var second = await thread.RunAsync(
            AgainPlainTextOkPrompt,
            new TurnOptions { CancellationToken = cancellation.Token });

        await Assert.That(second.FinalResponse).Contains(TestConstants.OkStatusValue);
        await Assert.That(second.Usage).IsNotNull();
        await Assert.That(thread.Id).IsEqualTo(firstThreadId);
    }

    [Test]
    public async Task RunAsync_WithSessionPersistenceEnabled_PersistsSessionAndAllowsFreshClientResume()
    {
        var settings = RealClaudeTestSupport.GetRequiredSettings();
        var workingDirectory = RealClaudeTestSupport.ResolveRepositoryRootPath();

        using var client = RealClaudeTestSupport.CreateClient();
        using var thread = client.StartThread(new ThreadOptions
        {
            Model = settings.Model,
            DangerouslySkipPermissions = true,
            WorkingDirectory = workingDirectory,
        });
        using var cancellation = new CancellationTokenSource(TwoTurnTimeout);

        var first = await thread.RunAsync(
            PlainTextOkPrompt,
            new TurnOptions { CancellationToken = cancellation.Token });

        var threadId = thread.Id;
        await Assert.That(first.Usage).IsNotNull();
        await Assert.That(threadId).IsNotNull();

        var persistedSessionPath = await RealClaudeTestSupport.FindPersistedSessionPathAsync(
            threadId!,
            PersistenceDetectionTimeout);

        await Assert.That(persistedSessionPath).IsNotNull();

        using var resumedClient = RealClaudeTestSupport.CreateClient();
        using var resumedThread = resumedClient.ResumeThread(threadId!, new ThreadOptions
        {
            Model = settings.Model,
            DangerouslySkipPermissions = true,
            WorkingDirectory = workingDirectory,
        });

        var resumed = await resumedThread.RunAsync(
            AgainPlainTextOkPrompt,
            new TurnOptions { CancellationToken = cancellation.Token });

        await Assert.That(resumed.FinalResponse).Contains(TestConstants.OkStatusValue);
        await Assert.That(resumed.Usage).IsNotNull();
        await Assert.That(resumedThread.Id).IsEqualTo(threadId);
    }

    private static ClaudeThread StartRealIntegrationThread(ClaudeClient client, string model)
    {
        return client.StartThread(new ThreadOptions
        {
            Model = model,
            DangerouslySkipPermissions = true,
        });
    }
}
