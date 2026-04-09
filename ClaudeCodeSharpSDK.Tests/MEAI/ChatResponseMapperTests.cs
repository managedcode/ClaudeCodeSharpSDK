using ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Models;

namespace ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Tests;

public class ChatResponseMapperTests
{
    private const string AssistantResponseText = "Hello from Claude";
    private const string ThreadId = "thread-123";
    private const string PlainResponseText = "Plain response";

    [Test]
    public async Task ToChatResponse_MapsAssistantTextConversationIdAndUsage()
    {
        var result = new RunResult([], AssistantResponseText, new Usage(100, 20, 5, 40));

        var response = ChatResponseMapper.ToChatResponse(result, ThreadId);

        await Assert.That(response.Text).Contains(AssistantResponseText);
        await Assert.That(response.ConversationId).IsEqualTo(ThreadId);
        await Assert.That(response.Usage).IsNotNull();
        await Assert.That(response.Usage!.InputTokenCount).IsEqualTo(100);
        await Assert.That(response.Usage.OutputTokenCount).IsEqualTo(40);
        await Assert.That(response.Usage.TotalTokenCount).IsEqualTo(140);
        await Assert.That(response.Usage.CachedInputTokenCount).IsEqualTo(25);
    }

    [Test]
    public async Task ToChatResponse_LeavesUsageNullWhenMissing()
    {
        var response = ChatResponseMapper.ToChatResponse(new RunResult([], PlainResponseText, null), null);

        await Assert.That(response.Usage).IsNull();
        await Assert.That(response.ConversationId).IsNull();
    }

    [Test]
    public async Task ToChatResponse_PreservesReportedZeroCachedTokens()
    {
        var response = ChatResponseMapper.ToChatResponse(new RunResult([], AssistantResponseText, new Usage(100, 0, 0, 40)), ThreadId);

        await Assert.That(response.Usage).IsNotNull();
        await Assert.That(response.Usage!.CachedInputTokenCount).IsEqualTo(0);
    }
}
