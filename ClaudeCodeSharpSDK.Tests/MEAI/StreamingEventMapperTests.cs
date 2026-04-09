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
    public async Task ToUpdates_TurnFailed_ThrowsInvalidOperationException()
    {
        var exception = await Assert.That(async () =>
                await CollectUpdates(ToAsyncEnumerable(new TurnFailedEvent(new ThreadError(AuthenticationFailedMessage)))))
            .ThrowsException();

        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains(AuthenticationFailedMessage);
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
