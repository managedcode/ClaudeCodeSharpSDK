using ManagedCode.ClaudeCodeSharpSDK.Configuration;
using ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Tests;

public class StreamingEventMapperTests
{
    private const string ThreadId = "thread-1";
    private const string MessageId = "msg-1";
    private const string AssistantText = "Hello";
    private const string StopReason = "end_turn";
    private const string AuthenticationFailedMessage = "authentication failed";
    private const string WorkspaceDirectory = "/workspace";
    private const string DefaultPermissionMode = "default";
    private const string CliVersion = "2.0.75";
    private const string DefaultOutputStyle = "default";
    private const string NoneCostMode = "none";
    private const string AllowedToolRead = "Read";
    private const string NativeActivityProperty = "managedcode:activity";
    private const string ClaudeToolUseActivity = "claude_tool_use";
    private const string ClaudeToolResultActivity = "claude_tool_result";
    private const string ToolUseContentType = "tool_use";
    private const string ToolResultContentType = "tool_result";
    private const string MetadataScriptVersion = "2.1.292";
    private const string CliVersionFlag = "--version";
    private const string ScriptFileName = "claude-provider-failure.js";
    private const string SandboxDirectoryName = ".sandbox";
    private const string TestsDirectoryName = "tests";
    private const string UserPrompt = "request";
    private const string ProviderFailureJson = "{\"type\":\"result\",\"is_error\":true,\"result\":\"authentication failed\"}";
    private const string NodeScript = "const args = process.argv.slice(2);\n"
        + "if (args.includes('" + CliVersionFlag + "')) { console.log('" + MetadataScriptVersion + "'); process.exit(0); }\n"
        + "process.stdin.resume();\n"
        + "process.stdin.on('end', () => process.stdout.write('" + ProviderFailureJson + "' + '\\n'));\n";

    [Test]
    public async Task ToUpdates_MapsThreadStartAssistantMessageAndUsage()
    {
        var updates = await CollectUpdates(
            ToAsyncEnumerable(
                CreateThreadStartedEvent(ThreadId),
                new ItemCompletedEvent(new AssistantMessageItem(MessageId, ClaudeModels.ClaudeSonnet45Alias, AssistantText, [], null, StopReason, null)),
                new TurnCompletedEvent(new Usage(10, 2, 3, 4), AssistantText, null, null, null, 1)));

        await Assert.That(updates.Count).IsEqualTo(3);
        await Assert.That(updates[0].ConversationId).IsEqualTo(ThreadId);
        await Assert.That(updates[1].ConversationId).IsEqualTo(ThreadId);
        await Assert.That(updates[1].Text).IsEqualTo(AssistantText);
        await Assert.That(updates[1].Role).IsEqualTo(ChatRole.Assistant);

        var usageContent = updates[2].Contents.OfType<UsageContent>().Single();
        await Assert.That(updates[2].ConversationId).IsEqualTo(ThreadId);
        await Assert.That(updates[2].FinishReason).IsEqualTo(ChatFinishReason.Stop);
        await Assert.That(usageContent.Details.InputTokenCount).IsEqualTo(10);
        await Assert.That(usageContent.Details.OutputTokenCount).IsEqualTo(4);
        await Assert.That(usageContent.Details.CachedInputTokenCount).IsEqualTo(5);
    }

    [Test]
    public async Task ToUpdates_PreservesReportedZeroCachedTokens()
    {
        var updates = await CollectUpdates(
            ToAsyncEnumerable(
                CreateThreadStartedEvent(ThreadId),
                new TurnCompletedEvent(new Usage(10, 0, 0, 4), AssistantText, null, null, null, 1)));

        var usageContent = updates[1].Contents.OfType<UsageContent>().Single();

        await Assert.That(usageContent.Details.CachedInputTokenCount).IsEqualTo(0);
    }

    [Test]
    public async Task ToUpdates_ClaudeToolUseWithEmptyText_EmitsOnlySafeNativeActivityMetadata()
    {
        var assistant = new AssistantMessageItem(
            MessageId,
            ClaudeModels.ClaudeSonnet45Alias,
            string.Empty,
            [new MessageContentBlock(ToolUseContentType, null, "tool-call-id", "sensitive-tool-name", null, null, null, null)],
            null,
            null,
            null);
        var updates = await CollectUpdates(ToAsyncEnumerable(new ItemCompletedEvent(assistant)));

        await Assert.That(updates.Count).IsEqualTo(1);
        await Assert.That(updates[0].Role).IsEqualTo(ChatRole.Assistant);
        await Assert.That(updates[0].Contents.Count).IsEqualTo(0);
        await Assert.That(updates[0].AdditionalProperties!.Count).IsEqualTo(1);
        await Assert.That(updates[0].AdditionalProperties![NativeActivityProperty]).IsEqualTo(ClaudeToolUseActivity);
    }

    [Test]
    public async Task ToUpdates_ClaudeToolResult_EmitsOnlySafeNativeActivityMetadata()
    {
        var userMessage = new UserMessageItem(
            MessageId,
            string.Empty,
            [new MessageContentBlock(ToolResultContentType, "sensitive output", null, null, "tool-call-id", false, null, null)]);
        var updates = await CollectUpdates(ToAsyncEnumerable(new ItemCompletedEvent(userMessage)));

        await Assert.That(updates.Count).IsEqualTo(1);
        await Assert.That(updates[0].Role).IsEqualTo(ChatRole.Tool);
        await Assert.That(updates[0].Contents.Count).IsEqualTo(0);
        await Assert.That(updates[0].AdditionalProperties!.Count).IsEqualTo(1);
        await Assert.That(updates[0].AdditionalProperties![NativeActivityProperty]).IsEqualTo(ClaudeToolResultActivity);
    }

    [Test]
    public async Task ToUpdates_TurnFailed_DisposesSourceBeforeThrowingConfirmedFailure()
    {
        var sourceDisposed = false;
        var exception = await Assert.That(async () =>
                await CollectUpdates(ProviderFailureEvents(() => sourceDisposed = true)))
            .ThrowsException();

        await Assert.That(exception).IsTypeOf<CliExecutionFailureException>();
        await Assert.That(((CliExecutionFailureException)exception!).RootProcessExitConfirmed).IsTrue();
        await Assert.That(((CliExecutionFailureException)exception!).ExitCode).IsNull();
        await Assert.That(sourceDisposed).IsTrue();
        await Assert.That(exception!.Message).Contains(AuthenticationFailedMessage);
    }

    [Test]
    public async Task ClaudeChatClient_ProviderFailureConfirmsOnlyAfterRealCliDisposal()
    {
        var sandboxDirectory = Path.Combine(Environment.CurrentDirectory, TestsDirectoryName, SandboxDirectoryName,
            $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(sandboxDirectory);
        var scriptPath = Path.Combine(sandboxDirectory, ScriptFileName);
        await File.WriteAllTextAsync(scriptPath, NodeScript);

        try
        {
            using var client = new ClaudeChatClient(new ClaudeChatClientOptions
            {
                ClaudeOptions = new ClaudeOptions
                {
                    ClaudeExecutablePath = scriptPath,
                    ProcessTerminationTimeout = TimeSpan.FromSeconds(3),
                },
            });
            var action = async () =>
            {
                await foreach (var _ in client.GetStreamingResponseAsync(
                                   [new ChatMessage(ChatRole.User, UserPrompt)]))
                {
                }
            };

            var exception = await Assert.That(action).ThrowsException();
            await Assert.That(exception).IsTypeOf<CliExecutionFailureException>();
            await Assert.That(((CliExecutionFailureException)exception!).ExitCode).IsNull();
            await Assert.That(((CliExecutionFailureException)exception).RootProcessExitConfirmed).IsTrue();
        }
        finally
        {
            Directory.Delete(sandboxDirectory, recursive: true);
        }
    }

    private static async IAsyncEnumerable<ThreadEvent> ProviderFailureEvents(Action onDispose)
    {
        try
        {
            yield return new TurnFailedEvent(new ThreadError(AuthenticationFailedMessage));
            await Task.Yield();
        }
        finally
        {
            onDispose();
        }
    }

    private static ThreadStartedEvent CreateThreadStartedEvent(string sessionId)
    {
        return new ThreadStartedEvent(new SessionInfo(
            sessionId,
            WorkspaceDirectory,
            ClaudeModels.Sonnet,
            DefaultPermissionMode,
            CliVersion,
            DefaultOutputStyle,
            NoneCostMode,
            [AllowedToolRead],
            [],
            [],
            [],
            [],
            []));
    }

    private static async IAsyncEnumerable<ThreadEvent> ToAsyncEnumerable(
        params ThreadEvent[] events)
    {
        foreach (var evt in events)
        {
            yield return evt;
            await Task.Yield();
        }
    }

    private static async Task<List<ChatResponseUpdate>> CollectUpdates(IAsyncEnumerable<ThreadEvent> events)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in StreamingEventMapper.ToUpdates(events).ConfigureAwait(false))
        {
            updates.Add(update);
        }

        return updates;
    }
}
