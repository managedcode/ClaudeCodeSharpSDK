using ManagedCode.ClaudeCodeSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Internal;

internal static class ChatResponseMapper
{
    internal static ChatResponse ToChatResponse(RunResult result, string? threadId)
    {
        var assistantMessage = new ChatMessage(
            ChatRole.Assistant,
            [new TextContent(result.FinalResponse)]);
        var response = new ChatResponse(assistantMessage)
        {
            ConversationId = threadId,
        };

        if (result.Usage is { } usage)
        {
            response.Usage = new UsageDetails
            {
                InputTokenCount = usage.InputTokens,
                OutputTokenCount = usage.OutputTokens,
                TotalTokenCount = usage.InputTokens + usage.OutputTokens,
                CachedInputTokenCount = usage.CachedInputTokens,
            };
        }

        return response;
    }
}
