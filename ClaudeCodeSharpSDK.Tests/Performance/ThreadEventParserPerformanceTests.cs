using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ManagedCode.ClaudeCodeSharpSDK.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Models;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Performance;

public partial class ThreadEventParserPerformanceTests
{
    private const string ApiKeySourceNone = "none";
    private const string AssistantErrorCode = "authentication_failed";
    private const string AssistantMessageId = "assistant-message-1";
    private const string AssistantReplyText = "Please run /login";
    private const string AssistantToolCallId = "toolu_1";
    private const string AssistantToolName = "Read";
    private const string ClaudeCodeVersion = "2.0.75";
    private const string DefaultPermissionMode = "default";
    private const string ErrorEventMessage = "fatal";
    private const string HelpSlashCommand = "/help";
    private const string InitEventId = "event-1";
    private const string KnownEventPayloadType = "diagnostic";
    private const string MessageRoleAssistant = "assistant";
    private const string MessageRoleUser = "user";
    private const string MessageStopReason = "stop_sequence";
    private const string MessageType = "message";
    private const string OutputStyleDefault = "default";
    private const string ReadmePath = "README.md";
    private const string ReviewerAgentName = "reviewer";
    private const string StatusPropertyName = "status";
    private const string StatusSubtype = "status";
    private const string SuccessEventId = "event-2";
    private const string SuccessResult = "ok";
    private const string SystemEventId = "event-3";
    private const string ToolUseName = "Read";
    private const string ToolUseResultId = "message-tool-1";
    private const string UnknownEventId = "event-4";
    private const string UserMessageId = "user-message-1";
    private const string UserPromptText = "Open README";
    private const string WorkspacePath = "/workspace";
    private static readonly string[] SupportedEventStream =
    [
        SerializeSystemInitPayload(),
        SerializeSystemStatusPayload(),
        SerializeAssistantTextPayload(),
        SerializeAssistantToolUsePayload(),
        SerializeUserPayload(),
        SerializeSuccessfulResultPayload(),
        SerializeFailedResultPayload(),
        SerializeErrorPayload(),
        SerializeUnknownPayload(),
    ];

    [Test]
    public async Task Parse_MixedSupportedEventStream_CompletesWithinBudgetAndCoversBranches()
    {
        const int iterations = 2_500;
        var expectedTotal = iterations * SupportedEventStream.Length;

        var eventKinds = new HashSet<string>(StringComparer.Ordinal);
        var itemKinds = new HashSet<string>(StringComparer.Ordinal);
        var contentTypes = new HashSet<string>(StringComparer.Ordinal);
        var assistantErrors = new HashSet<string>(StringComparer.Ordinal);
        var messagesWithStructuredInput = 0;
        var hasStructuredOutputPayload = false;

        var stopwatch = Stopwatch.StartNew();
        var parsedCount = 0;

        for (var iteration = 0; iteration < iterations; iteration += 1)
        {
            foreach (var line in SupportedEventStream)
            {
                var parsed = ThreadEventParser.Parse(line);
                parsedCount += 1;
                eventKinds.Add(parsed.GetType().FullName ?? parsed.GetType().Name);

                if (parsed is TurnCompletedEvent turnCompleted && !string.IsNullOrWhiteSpace(turnCompleted.Result))
                {
                    using var resultDocument = JsonDocument.Parse(turnCompleted.Result);
                    hasStructuredOutputPayload |= resultDocument.RootElement.TryGetProperty(StatusPropertyName, out _);
                }

                if (parsed is not ItemCompletedEvent itemCompleted)
                {
                    continue;
                }

                itemKinds.Add(itemCompleted.Item.GetType().FullName ?? itemCompleted.Item.GetType().Name);
                switch (itemCompleted.Item)
                {
                    case AssistantMessageItem assistant:
                        foreach (var block in assistant.Content)
                        {
                            contentTypes.Add(block.Type);
                            if (block.Input is not null)
                            {
                                messagesWithStructuredInput += 1;
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(assistant.Error))
                        {
                            assistantErrors.Add(assistant.Error);
                        }

                        break;

                    case UserMessageItem user:
                        foreach (var block in user.Content)
                        {
                            contentTypes.Add(block.Type);
                        }

                        break;
                }
            }
        }

        stopwatch.Stop();

        await Assert.That(parsedCount).IsEqualTo(expectedTotal);
        await Assert.That(eventKinds).IsEquivalentTo(
        [
            typeof(ThreadStartedEvent).FullName!,
            typeof(ItemCompletedEvent).FullName!,
            typeof(TurnCompletedEvent).FullName!,
            typeof(TurnFailedEvent).FullName!,
            typeof(ThreadErrorEvent).FullName!,
            typeof(UnknownEvent).FullName!,
        ]);
        await Assert.That(itemKinds).IsEquivalentTo(
        [
            typeof(AssistantMessageItem).FullName!,
            typeof(UserMessageItem).FullName!,
        ]);
        await Assert.That(contentTypes).IsEquivalentTo(
        [
            ClaudeProtocolConstants.ContentTypes.Text,
            ClaudeProtocolConstants.ContentTypes.ToolUse,
        ]);
        await Assert.That(assistantErrors).IsEquivalentTo([AssistantErrorCode]);
        await Assert.That(messagesWithStructuredInput).IsGreaterThan(0);
        await Assert.That(hasStructuredOutputPayload).IsTrue();
        await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromSeconds(20));
    }

    private static string SerializeAssistantTextPayload()
    {
        return JsonSerializer.Serialize(
            new AssistantEventPayload(
                ClaudeProtocolConstants.EventTypes.Assistant,
                new AssistantMessagePayload(
                    AssistantMessageId,
                    ClaudeModels.ClaudeSonnet45Alias,
                    MessageRoleAssistant,
                    MessageStopReason,
                    MessageType,
                    new UsagePayload(10, 2, 3, 4),
                    [new ContentPayload(ClaudeProtocolConstants.ContentTypes.Text, AssistantReplyText)]),
                null,
                InitEventId,
                SuccessEventId,
                AssistantErrorCode),
            ThreadEventParserPerformanceJsonContext.Default.AssistantEventPayload);
    }

    private static string SerializeAssistantToolUsePayload()
    {
        return JsonSerializer.Serialize(
            new AssistantEventPayload(
                ClaudeProtocolConstants.EventTypes.Assistant,
                new AssistantMessagePayload(
                    ToolUseResultId,
                    ClaudeModels.ClaudeSonnet45Alias,
                    MessageRoleAssistant,
                    MessageStopReason,
                    MessageType,
                    new UsagePayload(8, 0, 0, 3),
                    [new ContentPayload(
                        ClaudeProtocolConstants.ContentTypes.ToolUse,
                        null,
                        AssistantToolCallId,
                        ToolUseName,
                        null,
                        false,
                        new ToolInputPayload(ReadmePath))]),
                null,
                InitEventId,
                SystemEventId,
                null),
            ThreadEventParserPerformanceJsonContext.Default.AssistantEventPayload);
    }

    private static string SerializeErrorPayload()
    {
        return JsonSerializer.Serialize(
            new ErrorEventPayload(
                ClaudeProtocolConstants.EventTypes.Error,
                ErrorEventMessage,
                null),
            ThreadEventParserPerformanceJsonContext.Default.ErrorEventPayload);
    }

    private static string SerializeFailedResultPayload()
    {
        return JsonSerializer.Serialize(
            new ResultEventPayload(
                ClaudeProtocolConstants.EventTypes.Result,
                StatusSubtype,
                true,
                12,
                0,
                1,
                ErrorEventMessage,
                InitEventId,
                0,
                new UsagePayload(0, 0, 0, 0),
                UnknownEventId),
            ThreadEventParserPerformanceJsonContext.Default.ResultEventPayload);
    }

    private static string SerializeSuccessfulResultPayload()
    {
        return JsonSerializer.Serialize(
            new StructuredOutputResultEventPayload(
                ClaudeProtocolConstants.EventTypes.Result,
                StatusSubtype,
                false,
                12,
                0,
                1,
                string.Empty,
                InitEventId,
                0,
                new UsagePayload(4, 0, 0, 2),
                new StructuredOutputPayload(SuccessResult),
                SuccessEventId),
            ThreadEventParserPerformanceJsonContext.Default.StructuredOutputResultEventPayload);
    }

    private static string SerializeSystemInitPayload()
    {
        return JsonSerializer.Serialize(
            new SystemInitEventPayload(
                ClaudeProtocolConstants.EventTypes.System,
                ClaudeProtocolConstants.Subtypes.Init,
                InitEventId,
                WorkspacePath,
                [AssistantToolName],
                [],
                ClaudeModels.ClaudeSonnet45Alias,
                DefaultPermissionMode,
                [HelpSlashCommand],
                ApiKeySourceNone,
                ClaudeCodeVersion,
                OutputStyleDefault,
                [new AgentPayload(ReviewerAgentName)],
                [],
                [],
                InitEventId),
            ThreadEventParserPerformanceJsonContext.Default.SystemInitEventPayload);
    }

    private static string SerializeSystemStatusPayload()
    {
        return JsonSerializer.Serialize(
            new SystemStatusPayload(
                ClaudeProtocolConstants.EventTypes.System,
                StatusSubtype,
                InitEventId,
                WorkspacePath,
                [AssistantToolName],
                [],
                ClaudeModels.ClaudeSonnet45Alias,
                DefaultPermissionMode,
                [HelpSlashCommand],
                ApiKeySourceNone,
                ClaudeCodeVersion,
                OutputStyleDefault,
                [new AgentPayload(ReviewerAgentName)],
                [],
                [],
                SystemEventId),
            ThreadEventParserPerformanceJsonContext.Default.SystemStatusPayload);
    }

    private static string SerializeUnknownPayload()
    {
        return JsonSerializer.Serialize(
            new UnknownEventPayload(KnownEventPayloadType, 1),
            ThreadEventParserPerformanceJsonContext.Default.UnknownEventPayload);
    }

    private static string SerializeUserPayload()
    {
        return JsonSerializer.Serialize(
            new UserEventPayload(
                ClaudeProtocolConstants.EventTypes.User,
                new UserMessagePayload(
                    UserMessageId,
                    MessageRoleUser,
                    MessageType,
                    [new ContentPayload(ClaudeProtocolConstants.ContentTypes.Text, UserPromptText)]),
                InitEventId,
                UnknownEventId),
            ThreadEventParserPerformanceJsonContext.Default.UserEventPayload);
    }

    internal sealed record AgentPayload(string name);

    internal sealed record AssistantEventPayload(
        string type,
        AssistantMessagePayload message,
        string? parent_tool_use_id,
        string session_id,
        string uuid,
        string? error);

    internal sealed record AssistantMessagePayload(
        string id,
        string model,
        string role,
        string stop_reason,
        string type,
        UsagePayload usage,
        ContentPayload[] content);

    internal sealed record ContentPayload(
        string type,
        string? text,
        string? id = null,
        string? name = null,
        string? tool_use_id = null,
        bool? is_error = null,
        ToolInputPayload? input = null);

    internal sealed record ErrorEventPayload(string type, string? error, string? result);

    internal sealed record ResultEventPayload(
        string type,
        string subtype,
        bool is_error,
        int duration_ms,
        int duration_api_ms,
        int num_turns,
        string result,
        string session_id,
        decimal total_cost_usd,
        UsagePayload usage,
        string uuid);

    internal sealed record StructuredOutputPayload(string status);

    internal sealed record StructuredOutputResultEventPayload(
        string type,
        string subtype,
        bool is_error,
        int duration_ms,
        int duration_api_ms,
        int num_turns,
        string result,
        string session_id,
        decimal total_cost_usd,
        UsagePayload usage,
        StructuredOutputPayload structured_output,
        string uuid);

    internal sealed record SystemInitEventPayload(
        string type,
        string subtype,
        string session_id,
        string cwd,
        string[] tools,
        string[] mcp_servers,
        string model,
        string permissionMode,
        string[] slash_commands,
        string apiKeySource,
        string claude_code_version,
        string output_style,
        AgentPayload[] agents,
        string[] skills,
        string[] plugins,
        string uuid);

    internal sealed record SystemStatusPayload(
        string type,
        string subtype,
        string session_id,
        string cwd,
        string[] tools,
        string[] mcp_servers,
        string model,
        string permissionMode,
        string[] slash_commands,
        string apiKeySource,
        string claude_code_version,
        string output_style,
        AgentPayload[] agents,
        string[] skills,
        string[] plugins,
        string uuid);

    internal sealed record ToolInputPayload(string path);

    internal sealed record UnknownEventPayload(string type, int payload);

    internal sealed record UsagePayload(
        int input_tokens,
        int cache_creation_input_tokens,
        int cache_read_input_tokens,
        int output_tokens);

    internal sealed record UserEventPayload(
        string type,
        UserMessagePayload message,
        string session_id,
        string uuid);

    internal sealed record UserMessagePayload(
        string id,
        string role,
        string type,
        ContentPayload[] content);

    [JsonSerializable(typeof(AssistantEventPayload))]
    [JsonSerializable(typeof(ErrorEventPayload))]
    [JsonSerializable(typeof(ResultEventPayload))]
    [JsonSerializable(typeof(StructuredOutputResultEventPayload))]
    [JsonSerializable(typeof(SystemInitEventPayload))]
    [JsonSerializable(typeof(SystemStatusPayload))]
    [JsonSerializable(typeof(UnknownEventPayload))]
    [JsonSerializable(typeof(UserEventPayload))]
    internal sealed partial class ThreadEventParserPerformanceJsonContext : JsonSerializerContext;
}
