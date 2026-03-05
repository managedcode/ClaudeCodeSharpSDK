using ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Models;

namespace ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Tests;

public class ChatResponseMapperTests
{
    [Test]
    public async Task ToChatResponse_MapsAssistantTextConversationIdAndUsage()
    {
        var result = new RunResult([], "Hello from Claude", new Usage(100, 20, 5, 40));

        var response = ChatResponseMapper.ToChatResponse(result, "thread-123");

        await Assert.That(response.Text).Contains("Hello from Claude");
        await Assert.That(response.ConversationId).IsEqualTo("thread-123");
        await Assert.That(response.Usage).IsNotNull();
        await Assert.That(response.Usage!.InputTokenCount).IsEqualTo(100);
        await Assert.That(response.Usage.OutputTokenCount).IsEqualTo(40);
        await Assert.That(response.Usage.TotalTokenCount).IsEqualTo(140);
        await Assert.That(response.Usage.CachedInputTokenCount).IsEqualTo(25);
    }

    [Test]
    public async Task ToChatResponse_LeavesUsageNullWhenMissing()
    {
        var response = ChatResponseMapper.ToChatResponse(new RunResult([], "Plain response", null), null);

        await Assert.That(response.Usage).IsNull();
        await Assert.That(response.ConversationId).IsNull();
    }
}
