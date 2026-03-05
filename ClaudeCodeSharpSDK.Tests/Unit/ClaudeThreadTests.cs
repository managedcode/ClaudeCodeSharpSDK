using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Configuration;
using ManagedCode.ClaudeCodeSharpSDK.Execution;
using ManagedCode.ClaudeCodeSharpSDK.Models;
using Microsoft.Extensions.Logging;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Unit;

public partial class ClaudeThreadTests
{
    [Test]
    public async Task RunAsync_CollectsItemsUsageAndThreadId()
    {
        var runner = new FakeClaudeProcessRunner(
            """{"type":"system","subtype":"init","session_id":"session-123","cwd":"/workspace","tools":["Read"],"mcp_servers":[],"model":"claude-sonnet-4-5","permissionMode":"default","slash_commands":[],"apiKeySource":"none","claude_code_version":"2.0.75","output_style":"default","agents":[],"skills":[],"plugins":[],"uuid":"evt-1"}""",
            """{"type":"assistant","message":{"id":"msg-1","model":"claude-sonnet-4-5","role":"assistant","stop_reason":"end_turn","type":"message","usage":{"input_tokens":11,"cache_creation_input_tokens":2,"cache_read_input_tokens":3,"output_tokens":7},"content":[{"type":"text","text":"Draft answer"}]},"session_id":"session-123","uuid":"evt-2"}""",
            """{"type":"result","subtype":"success","is_error":false,"duration_ms":21,"duration_api_ms":20,"num_turns":1,"result":"Final answer","session_id":"session-123","total_cost_usd":0.01,"usage":{"input_tokens":11,"cache_creation_input_tokens":2,"cache_read_input_tokens":3,"output_tokens":7},"uuid":"evt-3"}""");
        using var thread = CreateThread(runner, new ThreadOptions { Model = ClaudeModels.ClaudeSonnet45 });

        var result = await thread.RunAsync("Hello Claude");

        await Assert.That(thread.Id).IsEqualTo("session-123");
        await Assert.That(result.FinalResponse).IsEqualTo("Final answer");
        await Assert.That(result.Items.Count).IsEqualTo(1);
        await Assert.That(result.Items[0]).IsTypeOf<AssistantMessageItem>();
        await Assert.That(result.Usage).IsNotNull();
        await Assert.That(result.Usage!.OutputTokens).IsEqualTo(7);
        await Assert.That(runner.Invocations.Count).IsEqualTo(1);
        await Assert.That(runner.Invocations[0].Input).IsEqualTo("Hello Claude");
    }

    [Test]
    public async Task RunAsync_WithImageInput_ThrowsNotSupportedException()
    {
        var runner = new FakeClaudeProcessRunner();
        using var thread = CreateThread(runner);

        var exception = await Assert.That(async () => await thread.RunAsync([LocalImageInput.FromPath("/tmp/image.png")])).ThrowsException();
        await Assert.That(exception).IsTypeOf<NotSupportedException>();
        await Assert.That(runner.Invocations.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RunAsync_TypedWithoutOutputSchema_Throws()
    {
        var runner = new FakeClaudeProcessRunner();
        using var thread = CreateThread(runner);

        var exception = await Assert.That(async () => await thread.RunAsync<AnswerPayload>("Hello")).ThrowsException();
        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("OutputSchema");
    }

    [Test]
    public async Task RunAsync_TypedWithJsonTypeInfo_DeserializesResponse()
    {
        var runner = new FakeClaudeProcessRunner(
            """{"type":"system","subtype":"init","session_id":"session-typed","cwd":"/workspace","tools":[],"mcp_servers":[],"model":"claude-sonnet-4-5","permissionMode":"default","slash_commands":[],"apiKeySource":"none","claude_code_version":"2.0.75","output_style":"default","agents":[],"skills":[],"plugins":[],"uuid":"evt-1"}""",
            """{"type":"result","subtype":"success","is_error":false,"duration_ms":8,"duration_api_ms":7,"num_turns":1,"result":"{\"Answer\":\"ok\"}","session_id":"session-typed","total_cost_usd":0.0,"usage":{"input_tokens":3,"cache_creation_input_tokens":0,"cache_read_input_tokens":0,"output_tokens":2},"uuid":"evt-2"}""");
        using var thread = CreateThread(runner);
        var schema = StructuredOutputSchema.Map<AnswerPayload>(
            additionalProperties: false,
            (response => response.Answer, StructuredOutputSchema.PlainText()));

        var result = await thread.RunAsync(
            "Return JSON",
            schema,
            ClaudeThreadJsonContext.Default.AnswerPayload);

        await Assert.That(result.TypedResponse.Answer).IsEqualTo("ok");
        await Assert.That(runner.Invocations[0].Arguments.Contains("--json-schema")).IsTrue();
    }

    private static ClaudeThread CreateThread(FakeClaudeProcessRunner runner, ThreadOptions? threadOptions = null)
    {
        var exec = new ClaudeExec("claude", null, null, runner);
        return new ClaudeThread(exec, new ClaudeOptions(), threadOptions ?? new ThreadOptions());
    }

    private sealed class FakeClaudeProcessRunner(params string[] lines) : IClaudeProcessRunner
    {
        private readonly IReadOnlyList<string> _lines = lines;

        public List<ClaudeProcessInvocation> Invocations { get; } = [];

        public async IAsyncEnumerable<string> RunAsync(
            ClaudeProcessInvocation invocation,
            ILogger logger,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _ = logger;
            Invocations.Add(invocation);

            foreach (var line in _lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return line;
                await Task.Yield();
            }
        }
    }

    internal sealed record AnswerPayload(string Answer);

    [JsonSerializable(typeof(AnswerPayload))]
    internal sealed partial class ClaudeThreadJsonContext : JsonSerializerContext;
}
