using ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Internal;
using Microsoft.Extensions.AI;

namespace ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Tests;

public class ChatMessageMapperTests
{
    private const string SystemPrompt = "Be concise";
    private const string FirstQuestion = "First question";
    private const string FirstAnswer = "First answer";
    private const string FollowUp = "Follow up";
    private const string ExtraContext = "With extra context";
    private const string ExpectedPrompt = "[System] Be concise\n\nFirst question\n\n[Assistant] First answer\n\nFollow up\n\nWith extra context";
    private const string DescribeImagePrompt = "Describe this image";
    private const string PngMimeType = "image/png";
    private const string TextOnlyPromptsMessage = "text-only prompts";

    [Test]
    public async Task ToClaudeInput_MixedConversation_PreservesChronology()
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, SystemPrompt),
            new ChatMessage(ChatRole.User, FirstQuestion),
            new ChatMessage(ChatRole.Assistant, FirstAnswer),
            new ChatMessage(ChatRole.User, [new TextContent(FollowUp), new TextContent(ExtraContext)]),
        };

        var prompt = ChatMessageMapper.ToClaudeInput(messages);

        await Assert.That(prompt).IsEqualTo(ExpectedPrompt);
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
                    new TextContent(DescribeImagePrompt),
                    new DataContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, PngMimeType),
                ]),
        };

        var exception = await Assert.That(() => ChatMessageMapper.ToClaudeInput(messages)).ThrowsException();
        await Assert.That(exception).IsTypeOf<NotSupportedException>();
        await Assert.That(exception!.Message).Contains(TextOnlyPromptsMessage);
    }
}
