using ManagedCode.ClaudeCodeSharpSDK.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Models;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Unit;

public class ThreadEventParserTests
{
    [Test]
    public async Task Parse_SystemInit_ReturnsThreadStartedEvent()
    {
        const string line = """
                            {"type":"system","subtype":"init","session_id":"session-123","cwd":"/workspace","tools":["Read","Write"],"mcp_servers":[],"model":"claude-opus-4-5","permissionMode":"default","slash_commands":["/help"],"apiKeySource":"none","claude_code_version":"2.0.75","output_style":"default","agents":[{"name":"reviewer"}],"skills":[],"plugins":[],"uuid":"evt-1"}
                            """;

        var parsed = ThreadEventParser.Parse(line);

        await Assert.That(parsed).IsTypeOf<ThreadStartedEvent>();

        var started = (ThreadStartedEvent)parsed;
        await Assert.That(started.ThreadId).IsEqualTo("session-123");
        await Assert.That(started.Session.WorkingDirectory).IsEqualTo("/workspace");
        await Assert.That(started.Session.Model).IsEqualTo("claude-opus-4-5");
        await Assert.That(started.Session.PermissionMode).IsEqualTo("default");
        await Assert.That(started.Session.Tools).IsEquivalentTo(["Read", "Write"]);
        await Assert.That(started.Session.SlashCommands).IsEquivalentTo(["/help"]);
    }

    [Test]
    public async Task Parse_AssistantMessage_ReturnsAssistantItem()
    {
        const string line = """
                            {"type":"assistant","message":{"id":"msg-1","model":"claude-sonnet-4-5","role":"assistant","stop_reason":"stop_sequence","type":"message","usage":{"input_tokens":10,"cache_creation_input_tokens":2,"cache_read_input_tokens":3,"output_tokens":4},"content":[{"type":"text","text":"API Error: 401"}]},"parent_tool_use_id":null,"session_id":"session-123","uuid":"evt-2","error":"authentication_failed"}
                            """;

        var parsed = ThreadEventParser.Parse(line);

        await Assert.That(parsed).IsTypeOf<ItemCompletedEvent>();

        var item = ((ItemCompletedEvent)parsed).Item;
        await Assert.That(item).IsTypeOf<AssistantMessageItem>();

        var assistant = (AssistantMessageItem)item;
        await Assert.That(assistant.Model).IsEqualTo("claude-sonnet-4-5");
        await Assert.That(assistant.Text).IsEqualTo("API Error: 401");
        await Assert.That(assistant.Error).IsEqualTo("authentication_failed");
        await Assert.That(assistant.Usage!.InputTokens).IsEqualTo(10);
        await Assert.That(assistant.Usage.CachedInputTokens).IsEqualTo(5);
    }

    [Test]
    public async Task Parse_ResultWithError_ReturnsTurnFailedEvent()
    {
        const string line = """
                            {"type":"result","subtype":"success","is_error":true,"duration_ms":12,"duration_api_ms":0,"num_turns":1,"result":"API Error: 401","session_id":"session-123","total_cost_usd":0,"usage":{"input_tokens":0,"cache_creation_input_tokens":0,"cache_read_input_tokens":0,"output_tokens":0},"uuid":"evt-3"}
                            """;

        var parsed = ThreadEventParser.Parse(line);

        await Assert.That(parsed).IsTypeOf<TurnFailedEvent>();

        var failed = (TurnFailedEvent)parsed;
        await Assert.That(failed.Error.Message).IsEqualTo("API Error: 401");
        await Assert.That(failed.DurationMs).IsEqualTo(12);
        await Assert.That(failed.Usage!.OutputTokens).IsEqualTo(0);
    }

    [Test]
    public async Task Parse_UnknownType_ReturnsUnknownEvent()
    {
        var parsed = ThreadEventParser.Parse("""{"type":"mystery","payload":1}""");

        await Assert.That(parsed).IsTypeOf<UnknownEvent>();
        await Assert.That(((UnknownEvent)parsed).RawType).IsEqualTo("mystery");
    }
}
