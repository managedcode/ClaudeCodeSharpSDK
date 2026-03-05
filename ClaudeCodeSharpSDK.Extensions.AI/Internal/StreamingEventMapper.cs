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
        await foreach (var evt in events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            switch (evt)
            {
                case ThreadStartedEvent started:
                    yield return new ChatResponseUpdate { ConversationId = started.ThreadId };
                    break;

                case ItemCompletedEvent { Item: AssistantMessageItem assistant }:
                    yield return new ChatResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        Contents = [new TextContent(assistant.Text)],
                    };
                    break;

                case TurnCompletedEvent completed:
                    yield return new ChatResponseUpdate
                    {
                        FinishReason = ChatFinishReason.Stop,
                        Contents =
                        [
                            new UsageContent(new UsageDetails
                            {
                                InputTokenCount = completed.Usage.InputTokens,
                                OutputTokenCount = completed.Usage.OutputTokens,
                                TotalTokenCount = completed.Usage.InputTokens + completed.Usage.OutputTokens,
                                CachedInputTokenCount = completed.Usage.CachedInputTokens > 0
                                    ? completed.Usage.CachedInputTokens
                                    : null,
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
