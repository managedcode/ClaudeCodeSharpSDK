using System.Runtime.CompilerServices;
using ManagedCode.ClaudeCodeSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Internal;

internal static class StreamingEventMapper
{
    internal static async IAsyncEnumerable<ChatResponseUpdate> ToUpdates(
        IAsyncEnumerable<ThreadEvent> events,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? conversationId = null;

        await foreach (var evt in events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            switch (evt)
            {
                case ThreadStartedEvent started:
                    conversationId = started.ThreadId;
                    yield return new ChatResponseUpdate { ConversationId = conversationId };
                    break;

                case ItemCompletedEvent { Item: AssistantMessageItem assistant }:
                    yield return new ChatResponseUpdate
                    {
                        ConversationId = conversationId,
                        Role = ChatRole.Assistant,
                        Contents = [new TextContent(assistant.Text)],
                    };
                    break;

                case TurnCompletedEvent completed:
                    yield return new ChatResponseUpdate
                    {
                        ConversationId = conversationId,
                        FinishReason = ChatFinishReason.Stop,
                        Contents =
                        [
                            new UsageContent(new UsageDetails
                            {
                                InputTokenCount = completed.Usage.InputTokens,
                                OutputTokenCount = completed.Usage.OutputTokens,
                                TotalTokenCount = completed.Usage.InputTokens + completed.Usage.OutputTokens,
                                CachedInputTokenCount = completed.Usage.CachedInputTokens,
                            }),
                        ],
                    };
                    break;

                case TurnFailedEvent failed:
                    throw new InvalidOperationException(failed.Error.Message);

                case ThreadErrorEvent error:
                    throw new InvalidOperationException(error.Message);
            }
        }
    }
}
