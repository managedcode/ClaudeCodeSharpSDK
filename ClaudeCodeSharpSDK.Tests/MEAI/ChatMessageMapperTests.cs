using ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Internal;
using Microsoft.Extensions.AI;

namespace ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Tests;

public class ChatMessageMapperTests
{
    [Test]
    public async Task ToClaudeInput_MixedConversation_PreservesChronology()
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, "Be concise"),
            new ChatMessage(ChatRole.User, "First question"),
            new ChatMessage(ChatRole.Assistant, "First answer"),
            new ChatMessage(ChatRole.User, [new TextContent("Follow up"), new TextContent("With extra context")]),
        };

        var prompt = ChatMessageMapper.ToClaudeInput(messages);

        await Assert.That(prompt).IsEqualTo(
            "[System] Be concise\n\nFirst question\n\n[Assistant] First answer\n\nFollow up\n\nWith extra context");
    }

    [Test]
    public async Task ToClaudeInput_IgnoresEmptyMessages()
    {
        var prompt = ChatMessageMapper.ToClaudeInput(
        [
            new ChatMessage(ChatRole.User, []),
            new ChatMessage(ChatRole.Assistant, string.Empty),
        ]);

        await Assert.That(prompt).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ToClaudeInput_ImageContent_ThrowsNotSupportedException()
    {
        var messages = new[]
        {
            new ChatMessage(
                ChatRole.User,
                [
                    new TextContent("Describe this image"),
                    new DataContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png"),
                ]),
        };

        var exception = await Assert.That(() => ChatMessageMapper.ToClaudeInput(messages)).ThrowsException();
        await Assert.That(exception).IsTypeOf<NotSupportedException>();
        await Assert.That(exception!.Message).Contains("text-only prompts");
    }
}
