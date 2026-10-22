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
        var textStream = new AssistantTextStream();
        string? conversationId = null;
        string? failureMessage = null;

        await foreach (var evt in events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            // Keep consuming until the core process runner proves natural exit and stream EOF.
            if (failureMessage is not null)
            {
                continue;
            }

            switch (evt)
            {
                case ThreadStartedEvent started:
                    conversationId = started.ThreadId;
                    yield return new ChatResponseUpdate { ConversationId = conversationId };
                    break;

                case AssistantMessageStartedEvent started:
                    textStream.Start(started.MessageId);
                    break;

                case AssistantTextDeltaEvent delta:
                    var deltaText = textStream.Append(delta.ContentIndex, delta.Text);
                    if (deltaText.Length > 0)
                    {
                        yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(deltaText)])
                        {
                            ConversationId = conversationId,
                            MessageId = textStream.MessageId,
                        };
                    }
                    break;

                case ItemCompletedEvent { Item: AssistantMessageItem assistant }:
                {
                    var text = textStream.Complete(assistant.Id, assistant.Text);
                    var toolUse = assistant.Content.Any(static block => block.Type == ToolUseContentType);
                    if (!string.IsNullOrEmpty(text) || toolUse)
                    {
                        yield return new ChatResponseUpdate
                        {
                            ConversationId = conversationId,
                            Role = ChatRole.Assistant,
                            Contents = string.IsNullOrEmpty(text) ? [] : [new TextContent(text)],
                            MessageId = assistant.Id,
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

        }

        if (failureMessage is not null)
        {
            throw CliExecutionFailureException.FromProviderFailure(failureMessage);
        }
    }
}
