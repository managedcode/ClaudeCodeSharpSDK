using System.Text.Json;
using System.Text.Json.Serialization;
using ManagedCode.ClaudeCodeSharpSDK.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Models;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Unit;

public partial class ThreadEventParserTests
{
    private const string ApiErrorText = "API Error: 401";
    private const string ApiKeySourceNone = "none";
    private const string AssistantEventType = "assistant";
    private const string AssistantMessageId = "msg-1";
    private const string AssistantToolCallId = "toolu-1";
    private const string AssistantToolUseName = "Read";
    private const string AuthenticationFailedError = "authentication_failed";
    private const string ClaudeCodeVersion = "2.0.75";
    private const string DefaultPermissionMode = "default";
    private const string ErrorEventText = "fatal";
    private const string ErrorSubtype = "status";
    private const string FilePath = "README.md";
    private const string FileChangeAddKind = "add";
    private const string FileChangeItemId = "change-1";
    private const string FileChangeInProgressStatus = "in_progress";
    private const string FileChangeItemType = "file_change";
    private const string FirstEventId = "evt-1";
    private const string HelpSlashCommand = "/help";
    private const string InitSubtype = "init";
    private const string ItemStartedEventType = "item.started";
    private const string ItemPropertyName = "item";
    private const string MessageRoleAssistant = "assistant";
    private const string MessageRoleUser = "user";
    private const string MessageStopReason = "stop_sequence";
    private const string MessageType = "message";
    private const string OkPropertyName = "ok";
    private const string OutputStyleDefault = "default";
    private const string ReadToolName = "Read";
    private const string ResultEventType = "result";
    private const string ReviewerAgentName = "reviewer";
    private const string SecondEventId = "evt-2";
    private const string SessionId = "session-123";
    private const string SandboxApplePath = "C:/git/CodexSandbox/apple.txt";
    private const string StatusPropertyName = "status";
    private const string StatusSubtype = "status";
    private const string SuccessSubtype = "success";
    private const string SuccessText = "ok";
    private const string SystemEventType = "system";
    private const string TextContentType = "text";
    private const string ThirdEventId = "evt-3";
    private const string ToolUseContentType = "tool_use";
    private const string UnknownEventType = "mystery";
    private const string UserEventType = "user";
    private const string UserMessageId = "user-1";
    private const string UserPromptText = "Open README";
    private const string WorkspacePath = "/workspace";
    private const string WriteToolName = "Write";

    [Test]
    public async Task Parse_SystemInit_ReturnsThreadStartedEvent()
    {
        var line = CreateSystemInitPayload();

        var parsed = ThreadEventParser.Parse(line);

        await Assert.That(parsed).IsTypeOf<ThreadStartedEvent>();

        var started = (ThreadStartedEvent)parsed;
        await Assert.That(started.ThreadId).IsEqualTo(SessionId);
        await Assert.That(started.Session.WorkingDirectory).IsEqualTo(WorkspacePath);
        await Assert.That(started.Session.Model).IsEqualTo(ClaudeModels.ClaudeOpus45);
        await Assert.That(started.Session.PermissionMode).IsEqualTo(DefaultPermissionMode);
        await Assert.That(started.Session.Tools).IsEquivalentTo([ReadToolName, WriteToolName]);
        await Assert.That(started.Session.SlashCommands).IsEquivalentTo([HelpSlashCommand]);
    }

    [Test]
    public async Task Parse_AssistantMessage_ReturnsAssistantItem()
    {
        var line = CreateAssistantPayload();

        var parsed = ThreadEventParser.Parse(line);

        await Assert.That(parsed).IsTypeOf<ItemCompletedEvent>();

        var item = ((ItemCompletedEvent)parsed).Item;
        await Assert.That(item).IsTypeOf<AssistantMessageItem>();

        var assistant = (AssistantMessageItem)item;
        await Assert.That(assistant.Model).IsEqualTo(ClaudeModels.ClaudeSonnet45Alias);
        await Assert.That(assistant.Text).IsEqualTo(ApiErrorText);
        await Assert.That(assistant.Error).IsEqualTo(AuthenticationFailedError);
        await Assert.That(assistant.Usage!.InputTokens).IsEqualTo(10);
        await Assert.That(assistant.Usage.CachedInputTokens).IsEqualTo(5);
    }

    [Test]
    public async Task Parse_AssistantToolUseBlock_PreservesStructuredInput()
    {
        var line = CreateAssistantToolUsePayload();

        var parsed = ThreadEventParser.Parse(line);

        await Assert.That(parsed).IsTypeOf<ItemCompletedEvent>();

        var item = ((ItemCompletedEvent)parsed).Item;
        await Assert.That(item).IsTypeOf<AssistantMessageItem>();

        var assistant = (AssistantMessageItem)item;
        await Assert.That(assistant.Content.Count).IsEqualTo(1);
        await Assert.That(assistant.Content[0].Type).IsEqualTo(ToolUseContentType);
        await Assert.That(assistant.Content[0].Id).IsEqualTo(AssistantToolCallId);
        await Assert.That(assistant.Content[0].Name).IsEqualTo(AssistantToolUseName);
        await Assert.That(assistant.Content[0].Input).IsNotNull();
    }

    [Test]
    public async Task Parse_ResultWithError_ReturnsTurnFailedEvent()
    {
        var line = CreateFailedResultPayload();

        var parsed = ThreadEventParser.Parse(line);

        await Assert.That(parsed).IsTypeOf<TurnFailedEvent>();

        var failed = (TurnFailedEvent)parsed;
        await Assert.That(failed.Error.Message).IsEqualTo(ApiErrorText);
        await Assert.That(failed.DurationMs).IsEqualTo(12);
        await Assert.That(failed.Usage!.OutputTokens).IsEqualTo(0);
    }

    [Test]
    public async Task Parse_ResultWithoutError_ReturnsTurnCompletedEvent()
    {
        var line = CreateSuccessfulResultPayload();

        var parsed = ThreadEventParser.Parse(line);

        await Assert.That(parsed).IsTypeOf<TurnCompletedEvent>();

        var completed = (TurnCompletedEvent)parsed;
        await Assert.That(completed.Result).IsEqualTo(SuccessText);
        await Assert.That(completed.DurationMs).IsEqualTo(12);
        await Assert.That(completed.Usage.OutputTokens).IsEqualTo(4);
    }

    [Test]
    public async Task Parse_ResultWithStructuredOutput_ReturnsTurnCompletedEventWithJsonPayload()
    {
        var line = CreateStructuredOutputResultPayload();

        var parsed = ThreadEventParser.Parse(line);

        await Assert.That(parsed).IsTypeOf<TurnCompletedEvent>();

        var completed = (TurnCompletedEvent)parsed;
        using var document = JsonDocument.Parse(completed.Result);
        await Assert.That(document.RootElement.GetProperty(OkPropertyName).GetString()).IsEqualTo(SuccessText);
    }

    [Test]
    public async Task Parse_UserMessage_ReturnsUserItem()
    {
        var line = CreateUserPayload();

        var parsed = ThreadEventParser.Parse(line);

        await Assert.That(parsed).IsTypeOf<ItemCompletedEvent>();

        var item = ((ItemCompletedEvent)parsed).Item;
        await Assert.That(item).IsTypeOf<UserMessageItem>();

        var user = (UserMessageItem)item;
        await Assert.That(user.Id).IsEqualTo(UserMessageId);
        await Assert.That(user.Text).IsEqualTo(UserPromptText);
        await Assert.That(user.Content.Count).IsEqualTo(1);
        await Assert.That(user.Content[0].Type).IsEqualTo(TextContentType);
    }

    [Test]
    public async Task Parse_ErrorEvent_ReturnsThreadErrorEvent()
    {
        var parsed = ThreadEventParser.Parse(CreateErrorPayload());

        await Assert.That(parsed).IsTypeOf<ThreadErrorEvent>();
        await Assert.That(((ThreadErrorEvent)parsed).Message).IsEqualTo(ErrorEventText);
    }

    [Test]
    public async Task Parse_SystemWithoutInitSubtype_ReturnsUnknownEvent()
    {
        var parsed = ThreadEventParser.Parse(CreateSystemStatusPayload());

        await Assert.That(parsed).IsTypeOf<UnknownEvent>();
        await Assert.That(((UnknownEvent)parsed).RawType).IsEqualTo(SystemEventType);
    }

    [Test]
    public async Task Parse_UnknownType_ReturnsUnknownEvent()
    {
        var parsed = ThreadEventParser.Parse(CreateUnknownPayload());

        await Assert.That(parsed).IsTypeOf<UnknownEvent>();
        await Assert.That(((UnknownEvent)parsed).RawType).IsEqualTo(UnknownEventType);
    }

    [Test]
    public async Task Parse_ItemStartedFileChangeInProgress_ReturnsUnknownEvent()
    {
        var parsed = ThreadEventParser.Parse(CreateFileChangeStartedPayload());

        await Assert.That(parsed).IsTypeOf<UnknownEvent>();

        var unknown = (UnknownEvent)parsed;
        await Assert.That(unknown.RawType).IsEqualTo(ItemStartedEventType);
        await Assert.That(unknown.Payload[ItemPropertyName]?[ClaudeProtocolConstants.Properties.Type]?.GetValue<string>())
            .IsEqualTo(FileChangeItemType);
        await Assert.That(unknown.Payload[ItemPropertyName]?[ClaudeProtocolConstants.Properties.Id]?.GetValue<string>())
            .IsEqualTo(FileChangeItemId);
        await Assert.That(unknown.Payload[ItemPropertyName]?[StatusPropertyName]?.GetValue<string>())
            .IsEqualTo(FileChangeInProgressStatus);
        await Assert.That(unknown.Payload[ItemPropertyName]?["changes"]?[0]?["path"]?.GetValue<string>())
            .IsEqualTo(SandboxApplePath);
    }

    private static string CreateAssistantPayload()
    {
        return JsonSerializer.Serialize(
            new AssistantEventPayload(
                AssistantEventType,
                new AssistantMessagePayload(
                    AssistantMessageId,
                    ClaudeModels.ClaudeSonnet45Alias,
                    MessageRoleAssistant,
                    MessageStopReason,
                    MessageType,
                    new UsagePayload(10, 2, 3, 4),
                    [new ContentPayload(TextContentType, ApiErrorText)]),
                null,
                SessionId,
                SecondEventId,
                AuthenticationFailedError),
            ThreadEventParserJsonContext.Default.AssistantEventPayload);
    }

    private static string CreateAssistantToolUsePayload()
    {
        return JsonSerializer.Serialize(
            new AssistantEventPayload(
                AssistantEventType,
                new AssistantMessagePayload(
                    AssistantMessageId,
                    ClaudeModels.ClaudeSonnet45Alias,
                    MessageRoleAssistant,
                    MessageStopReason,
                    MessageType,
                    new UsagePayload(10, 2, 3, 4),
                    [new ContentPayload(
                        ToolUseContentType,
                        null,
                        AssistantToolCallId,
                        AssistantToolUseName,
                        null,
                        false,
                        new ToolInputPayload(FilePath))]),
                null,
                SessionId,
                SecondEventId,
                null),
            ThreadEventParserJsonContext.Default.AssistantEventPayload);
    }

    private static string CreateErrorPayload()
    {
        return JsonSerializer.Serialize(
            new ErrorEventPayload(ClaudeProtocolConstants.EventTypes.Error, ErrorEventText, null),
            ThreadEventParserJsonContext.Default.ErrorEventPayload);
    }

    private static string CreateFailedResultPayload()
    {
        return JsonSerializer.Serialize(
            new ResultEventPayload(
                ResultEventType,
                SuccessSubtype,
                true,
                12,
                0,
                1,
                ApiErrorText,
                SessionId,
                0,
                new UsagePayload(0, 0, 0, 0),
                ThirdEventId),
            ThreadEventParserJsonContext.Default.ResultEventPayload);
    }

    private static string CreateSuccessfulResultPayload()
    {
        return JsonSerializer.Serialize(
            new ResultEventPayload(
                ResultEventType,
                StatusSubtype,
                false,
                12,
                0,
                1,
                SuccessText,
                SessionId,
                0,
                new UsagePayload(1, 0, 0, 4),
                ThirdEventId),
            ThreadEventParserJsonContext.Default.ResultEventPayload);
    }

    private static string CreateSystemInitPayload()
    {
        return JsonSerializer.Serialize(
            new SystemInitEventPayload(
                SystemEventType,
                InitSubtype,
                SessionId,
                WorkspacePath,
                [ReadToolName, WriteToolName],
                [],
                ClaudeModels.ClaudeOpus45,
                DefaultPermissionMode,
                [HelpSlashCommand],
                ApiKeySourceNone,
                ClaudeCodeVersion,
                OutputStyleDefault,
                [new AgentPayload(ReviewerAgentName)],
                [],
                [],
                FirstEventId),
            ThreadEventParserJsonContext.Default.SystemInitEventPayload);
    }

    private static string CreateStructuredOutputResultPayload()
    {
        return JsonSerializer.Serialize(
            new StructuredOutputResultEventPayload(
                ResultEventType,
                StatusSubtype,
                false,
                12,
                0,
                1,
                string.Empty,
                SessionId,
                0,
                new UsagePayload(1, 0, 0, 4),
                new StructuredOutputPayload(SuccessText),
                ThirdEventId),
            ThreadEventParserJsonContext.Default.StructuredOutputResultEventPayload);
    }

    private static string CreateSystemStatusPayload()
    {
        return JsonSerializer.Serialize(
            new SystemInitEventPayload(
                SystemEventType,
                ErrorSubtype,
                SessionId,
                WorkspacePath,
                [ReadToolName, WriteToolName],
                [],
                ClaudeModels.ClaudeOpus45,
                DefaultPermissionMode,
                [HelpSlashCommand],
                ApiKeySourceNone,
                ClaudeCodeVersion,
                OutputStyleDefault,
                [new AgentPayload(ReviewerAgentName)],
                [],
                [],
                FirstEventId),
            ThreadEventParserJsonContext.Default.SystemInitEventPayload);
    }

    private static string CreateUnknownPayload()
    {
        return JsonSerializer.Serialize(
            new UnknownEventPayload(UnknownEventType, 1),
            ThreadEventParserJsonContext.Default.UnknownEventPayload);
    }

    private static string CreateFileChangeStartedPayload()
    {
        return JsonSerializer.Serialize(
            new FileChangeStartedEventPayload(
                ItemStartedEventType,
                new FileChangePayload(
                    FileChangeItemId,
                    FileChangeItemType,
                    [new FileChangeEntryPayload(SandboxApplePath, FileChangeAddKind)],
                    FileChangeInProgressStatus)),
            ThreadEventParserJsonContext.Default.FileChangeStartedEventPayload);
    }

    private static string CreateUserPayload()
    {
        return JsonSerializer.Serialize(
            new UserEventPayload(
                UserEventType,
                new UserMessagePayload(
                    UserMessageId,
                    MessageRoleUser,
                    MessageType,
                    [new ContentPayload(TextContentType, UserPromptText)]),
                SessionId,
                ThirdEventId),
            ThreadEventParserJsonContext.Default.UserEventPayload);
    }

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

    internal sealed record AgentPayload(string name);

    internal sealed record ContentPayload(
        string type,
        string? text,
        string? id = null,
        string? name = null,
        string? tool_use_id = null,
        bool? is_error = null,
        ToolInputPayload? input = null);

    internal sealed record ErrorEventPayload(string type, string? error, string? result);

    internal sealed record FileChangeEntryPayload(string path, string kind);

    internal sealed record FileChangePayload(
        string id,
        string type,
        FileChangeEntryPayload[] changes,
        string status);

    internal sealed record FileChangeStartedEventPayload(string type, FileChangePayload item);

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

    internal sealed record StructuredOutputPayload(string ok);

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

    internal sealed record ToolInputPayload(string path);

    internal sealed record UnknownEventPayload(string type, int payload);

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

    internal sealed record UsagePayload(
        int input_tokens,
        int cache_creation_input_tokens,
        int cache_read_input_tokens,
        int output_tokens);

    [JsonSerializable(typeof(AssistantEventPayload))]
    [JsonSerializable(typeof(ErrorEventPayload))]
    [JsonSerializable(typeof(FileChangeStartedEventPayload))]
    [JsonSerializable(typeof(ResultEventPayload))]
    [JsonSerializable(typeof(StructuredOutputResultEventPayload))]
    [JsonSerializable(typeof(SystemInitEventPayload))]
    [JsonSerializable(typeof(UnknownEventPayload))]
    [JsonSerializable(typeof(UserEventPayload))]
    internal sealed partial class ThreadEventParserJsonContext : JsonSerializerContext;
}
