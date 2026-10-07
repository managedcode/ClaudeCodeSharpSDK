using System.Runtime.CompilerServices;
using ManagedCode.ClaudeCodeSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Internal;

internal static class StreamingEventMapper
{
    private const string NativeActivityProperty = "managedcode:activity";
    private const string ClaudeToolUseActivity = "claude_tool_use";
    private const string ClaudeToolResultActivity = "claude_tool_result";
    private const string ToolUseContentType = "tool_use";
    private const string ToolResultContentType = "tool_result";
    internal static async IAsyncEnumerable<ChatResponseUpdate> ToUpdates(
        IAsyncEnumerable<ThreadEvent> events,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? conversationId = null;
        string? failureMessage = null;

        await foreach (var evt in events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            switch (evt)
            {
                case ThreadStartedEvent started:
                    conversationId = started.ThreadId;
                    yield return new ChatResponseUpdate { ConversationId = conversationId };
                    break;

                case ItemCompletedEvent { Item: AssistantMessageItem assistant }:
                {
                    var toolUse = assistant.Content.Any(static block => block.Type == ToolUseContentType);
                    if (!string.IsNullOrEmpty(assistant.Text) || toolUse)
                    {
                        yield return new ChatResponseUpdate
                        {
                            ConversationId = conversationId,
                            Role = ChatRole.Assistant,
                            Contents = string.IsNullOrEmpty(assistant.Text) ? [] : [new TextContent(assistant.Text)],
                            AdditionalProperties = toolUse
                                ? new AdditionalPropertiesDictionary { [NativeActivityProperty] = ClaudeToolUseActivity }
                                : null,
                        };
                    }
                    break;
                }

                case ItemCompletedEvent { Item: UserMessageItem user }:
                {
                    var toolResult = user.Content.Any(static block => block.Type == ToolResultContentType);
                    if (toolResult)
                    {
                        yield return new ChatResponseUpdate
                        {
                            ConversationId = conversationId,
                            Role = ChatRole.Tool,
                            AdditionalProperties = new AdditionalPropertiesDictionary { [NativeActivityProperty] = ClaudeToolResultActivity },
                        };
                    }
                    break;
                }

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
                    failureMessage ??= failed.Error.Message;
                    break;

                case ThreadErrorEvent error:
                    failureMessage ??= error.Message;
                    break;
            }

            if (failureMessage is not null)
            {
                break;
            }
        }

        if (failureMessage is not null)
        {
            throw CliExecutionFailureException.FromProviderFailure(failureMessage);
        }
    }
}
