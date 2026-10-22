using System.Runtime.CompilerServices;
using ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Tests;

public class PartialMessageStreamingTests
{
    [Test]
    public async Task NativePartialMessages_StreamBeforeCompletion_WithoutRepeatingCompletedText()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var stream = StreamingEventMapper.ToUpdates(Events(release.Task, timeout.Token), timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        await Assert.That(await stream.MoveNextAsync()).IsTrue();
        await Assert.That(stream.Current.Text).IsEqualTo("Hello");
        await Assert.That(stream.Current.MessageId).IsEqualTo("message-1");
        await Assert.That(stream.Current.FinishReason).IsNull();
        release.SetResult();
        var text = stream.Current.Text;
        var completed = false;
        while (await stream.MoveNextAsync())
        {
            text += stream.Current.Text;
            completed |= stream.Current.FinishReason == ChatFinishReason.Stop;
        }
        await Assert.That(text).IsEqualTo("Hello world");
        await Assert.That(completed).IsTrue();
    }

    [Test]
    public async Task NativeTextToolTextBlocks_WithOneMessageId_ContinueStreamingAfterFirstSnapshot()
    {
        var text = string.Empty;
        await foreach (var update in StreamingEventMapper.ToUpdates(BlockEvents()))
        {
            text += update.Text;
        }
        await Assert.That(text).IsEqualTo("Before\n\nAfter");
    }

    private static async IAsyncEnumerable<ThreadEvent> BlockEvents()
    {
        yield return new AssistantMessageStartedEvent("same-message");
        yield return new AssistantTextDeltaEvent(0, "Before");
        yield return ThreadEventParser.Parse("""{"type":"assistant","message":{"id":"same-message","content":[{"type":"text","text":"Before"}]}}""");
        yield return ThreadEventParser.Parse("""{"type":"assistant","message":{"id":"same-message","content":[{"type":"tool_use","id":"tool","name":"Write","input":{}}]}}""");
        await Task.Yield();
        yield return new AssistantTextDeltaEvent(2, "After");
        yield return ThreadEventParser.Parse("""{"type":"assistant","message":{"id":"same-message","content":[{"type":"text","text":"After"}]}}""");
    }

    private static async IAsyncEnumerable<ThreadEvent> Events(Task release,
        [EnumeratorCancellation] CancellationToken token)
    {
        yield return ThreadEventParser.Parse("""{"type":"stream_event","event":{"type":"message_start","message":{"id":"message-1"}}}""");
        yield return ThreadEventParser.Parse("""{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hello"}}}""");
        await release.WaitAsync(token);
        yield return ThreadEventParser.Parse("""{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":" "}}}""");
        yield return new AssistantTextDeltaEvent(0, "world");
        yield return ThreadEventParser.Parse("""{"type":"assistant","message":{"id":"message-1","content":[{"type":"text","text":"Hello world"}]}}""");
        yield return ThreadEventParser.Parse("""{"type":"result","result":"Hello world","is_error":false}""");
    }
}
