using ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Tests;

public class StreamingEventMapperTests
{
    [Test]
    public async Task ToUpdates_MapsThreadStartAssistantMessageAndUsage()
    {
        var updates = await CollectUpdates(
            ToAsyncEnumerable(
                CreateThreadStartedEvent("thread-1"),
                new ItemCompletedEvent(new AssistantMessageItem("msg-1", "claude-sonnet-4-5", "Hello", [], null, "end_turn", null)),
                new TurnCompletedEvent(new Usage(10, 2, 3, 4), "Hello", null, null, null, 1)));

        await Assert.That(updates.Count).IsEqualTo(3);
        await Assert.That(updates[0].ConversationId).IsEqualTo("thread-1");
        await Assert.That(updates[1].Text).IsEqualTo("Hello");
        await Assert.That(updates[1].Role).IsEqualTo(ChatRole.Assistant);

        var usageContent = updates[2].Contents.OfType<UsageContent>().Single();
        await Assert.That(updates[2].FinishReason).IsEqualTo(ChatFinishReason.Stop);
        await Assert.That(usageContent.Details.InputTokenCount).IsEqualTo(10);
        await Assert.That(usageContent.Details.OutputTokenCount).IsEqualTo(4);
        await Assert.That(usageContent.Details.CachedInputTokenCount).IsEqualTo(5);
    }

    [Test]
    public async Task ToUpdates_TurnFailed_ThrowsInvalidOperationException()
    {
        var exception = await Assert.That(async () =>
                await CollectUpdates(ToAsyncEnumerable(new TurnFailedEvent(new ThreadError("authentication failed")))))
            .ThrowsException();

        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("authentication failed");
    }

    private static ThreadStartedEvent CreateThreadStartedEvent(string sessionId)
    {
        return new ThreadStartedEvent(new SessionInfo(
            sessionId,
            "/workspace",
            ClaudeModels.Sonnet,
            "default",
            "2.0.75",
            "default",
            "none",
            ["Read"],
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
