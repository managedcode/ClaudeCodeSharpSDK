using Microsoft.Extensions.AI;

namespace ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Internal;

internal static class ChatMessageMapper
{
    private const string SystemPrefix = "[System] ";
    private const string AssistantPrefix = "[Assistant] ";
    private const string ParagraphSeparator = "\n\n";
    private const string ImageUnsupportedMessage = "Claude Code chat adapter currently supports text-only prompts.";

    internal static string ToClaudeInput(IEnumerable<ChatMessage> messages)
    {
        var promptParts = new List<string>();

        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System && message.Text is { } systemText)
            {
                promptParts.Add(string.Concat(SystemPrefix, systemText));
                continue;
            }

            if (message.Role == ChatRole.User)
            {
                var userTextParts = new List<string>();
                foreach (var content in message.Contents)
                {
                    switch (content)
                    {
                        case TextContent textContent when textContent.Text is { } text:
                            userTextParts.Add(text);
                            break;
                        case DataContent:
                            throw new NotSupportedException(ImageUnsupportedMessage);
                    }
                }

                if (userTextParts.Count > 0)
                {
                    promptParts.Add(string.Join(ParagraphSeparator, userTextParts));
                }

                continue;
            }

            if (message.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(message.Text))
            {
                promptParts.Add(string.Concat(AssistantPrefix, message.Text));
            }
        }

        return string.Join(ParagraphSeparator, promptParts);
    }
}
