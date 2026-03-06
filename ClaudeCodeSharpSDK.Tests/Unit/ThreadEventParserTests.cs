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
    private const string AuthenticationFailedError = "authentication_failed";
    private const string ClaudeCodeVersion = "2.0.75";
    private const string DefaultPermissionMode = "default";
    private const string FirstEventId = "evt-1";
    private const string HelpSlashCommand = "/help";
    private const string InitSubtype = "init";
    private const string MessageRoleAssistant = "assistant";
    private const string MessageStopReason = "stop_sequence";
    private const string MessageType = "message";
    private const string OutputStyleDefault = "default";
    private const string ReadToolName = "Read";
    private const string ResultEventType = "result";
    private const string ReviewerAgentName = "reviewer";
    private const string SecondEventId = "evt-2";
    private const string SessionId = "session-123";
    private const string SuccessSubtype = "success";
    private const string SystemEventType = "system";
    private const string TextContentType = "text";
    private const string ThirdEventId = "evt-3";
    private const string UnknownEventType = "mystery";
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
    public async Task Parse_UnknownType_ReturnsUnknownEvent()
    {
        var parsed = ThreadEventParser.Parse(CreateUnknownPayload());

        await Assert.That(parsed).IsTypeOf<UnknownEvent>();
        await Assert.That(((UnknownEvent)parsed).RawType).IsEqualTo(UnknownEventType);
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
                    [new TextContentPayload(TextContentType, ApiErrorText)]),
                null,
                SessionId,
                SecondEventId,
                AuthenticationFailedError),
            ThreadEventParserJsonContext.Default.AssistantEventPayload);
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

    private static string CreateUnknownPayload()
    {
        return JsonSerializer.Serialize(
            new UnknownEventPayload(UnknownEventType, 1),
            ThreadEventParserJsonContext.Default.UnknownEventPayload);
    }

    internal sealed record AssistantEventPayload(
        string type,
        AssistantMessagePayload message,
        string? parent_tool_use_id,
        string session_id,
        string uuid,
        string error);

    internal sealed record AssistantMessagePayload(
        string id,
        string model,
        string role,
        string stop_reason,
        string type,
        UsagePayload usage,
        TextContentPayload[] content);

    internal sealed record AgentPayload(string name);

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

    internal sealed record TextContentPayload(string type, string text);

    internal sealed record UnknownEventPayload(string type, int payload);

    internal sealed record UsagePayload(
        int input_tokens,
        int cache_creation_input_tokens,
        int cache_read_input_tokens,
        int output_tokens);

    [JsonSerializable(typeof(AssistantEventPayload))]
    [JsonSerializable(typeof(ResultEventPayload))]
    [JsonSerializable(typeof(SystemInitEventPayload))]
    [JsonSerializable(typeof(UnknownEventPayload))]
    internal sealed partial class ThreadEventParserJsonContext : JsonSerializerContext;
}
