using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Configuration;
using ManagedCode.ClaudeCodeSharpSDK.Execution;
using ManagedCode.ClaudeCodeSharpSDK.Models;
using ManagedCode.ClaudeCodeSharpSDK.Tests.Shared;
using Microsoft.Extensions.Logging;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Unit;

public partial class ClaudeThreadTests
{
    private const string ApiKeySourceNone = "none";
    private const string AssistantEventType = "assistant";
    private const string AssistantMessageId = "msg-1";
    private const string ClaudeCodeVersion = "2.0.75";
    private const string DefaultPermissionMode = "default";
    private const string DraftAnswerText = "Draft answer";
    private const string FinalAnswerText = "Final answer";
    private const string FirstEventId = "evt-1";
    private const string HelloClaudeInput = "Hello";
    private const string ImagePath = "/tmp/image.png";
    private const string InitSubtype = "init";
    private const string JsonSchemaFlag = "--json-schema";
    private const string MessageRoleAssistant = "assistant";
    private const string MessageType = "message";
    private const string OkText = "ok";
    private const string OutputSchemaMessageFragment = "OutputSchema";
    private const string OutputStyleDefault = "default";
    private const string ReadToolName = "Read";
    private const string ResultEventType = "result";
    private const string ReturnJsonPrompt = "Return JSON";
    private const string ResumeFlag = "--resume";
    private const string SecondEventId = "evt-2";
    private const string SessionId = "session-123";
    private const string SuccessSubtype = "success";
    private const string SystemEventType = "system";
    private const string SystemInitTypedSessionId = "session-typed";
    private const string SystemStopReason = "end_turn";
    private const string TextContentType = "text";
    private const string ThirdEventId = "evt-3";
    private const string WorkspacePath = "/workspace";

    [Test]
    public async Task RunAsync_CollectsItemsUsageAndThreadId()
    {
        var runner = new FakeClaudeProcessRunner(
            CreateSystemInitLine(SessionId, [ReadToolName], FirstEventId),
            CreateAssistantMessageLine(SessionId, DraftAnswerText, SecondEventId),
            CreateResultLine(SessionId, FinalAnswerText, ThirdEventId, durationMs: 21, durationApiMs: 20, totalCostUsd: 0.01m, inputTokens: 11, cacheCreationInputTokens: 2, cacheReadInputTokens: 3, outputTokens: 7));
        using var thread = CreateThread(runner, new ThreadOptions { Model = ClaudeModels.ClaudeSonnet45Alias });

        var result = await thread.RunAsync(HelloClaudeInput);

        await Assert.That(thread.Id).IsEqualTo(SessionId);
        await Assert.That(result.FinalResponse).IsEqualTo(FinalAnswerText);
        await Assert.That(result.Items.Count).IsEqualTo(1);
        await Assert.That(result.Items[0]).IsTypeOf<AssistantMessageItem>();
        await Assert.That(result.Usage).IsNotNull();
        await Assert.That(result.Usage!.OutputTokens).IsEqualTo(7);
        await Assert.That(runner.Invocations.Count).IsEqualTo(1);
        await Assert.That(runner.Invocations[0].Input).IsEqualTo(HelloClaudeInput);
    }

    [Test]
    public async Task RunAsync_WithImageInput_ThrowsNotSupportedException()
    {
        var runner = new FakeClaudeProcessRunner();
        using var thread = CreateThread(runner);

        var exception = await Assert.That(async () => await thread.RunAsync([LocalImageInput.FromPath(ImagePath)])).ThrowsException();
        await Assert.That(exception).IsTypeOf<NotSupportedException>();
        await Assert.That(runner.Invocations.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RunAsync_TypedWithoutOutputSchema_Throws()
    {
        var runner = new FakeClaudeProcessRunner();
        using var thread = CreateThread(runner);

        var exception = await Assert.That(async () => await thread.RunAsync<AnswerPayload>(HelloClaudeInput)).ThrowsException();
        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains(OutputSchemaMessageFragment);
    }

    [Test]
    public async Task RunAsync_TypedWithJsonTypeInfo_DeserializesResponse()
    {
        var runner = new FakeClaudeProcessRunner(
            CreateSystemInitLine(SystemInitTypedSessionId, [], FirstEventId),
            CreateResultLine(SystemInitTypedSessionId, JsonSerializer.Serialize(new AnswerPayload(OkText), ClaudeThreadJsonContext.Default.AnswerPayload), SecondEventId, durationMs: 8, durationApiMs: 7, totalCostUsd: 0m, inputTokens: 3, cacheCreationInputTokens: 0, cacheReadInputTokens: 0, outputTokens: 2));
        using var thread = CreateThread(runner);
        var schema = StructuredOutputSchema.Map<AnswerPayload>(
            additionalProperties: false,
            (response => response.Answer, StructuredOutputSchema.PlainText()));

        var result = await thread.RunAsync(
            ReturnJsonPrompt,
            schema,
            ClaudeThreadJsonContext.Default.AnswerPayload);

        await Assert.That(result.TypedResponse.Answer).IsEqualTo(OkText);
        await Assert.That(runner.Invocations[0].Arguments.Contains(JsonSchemaFlag)).IsTrue();
    }

    [Test]
    public async Task RunAsync_OnResumedThread_PassesResumeFlagFromThreadId()
    {
        var runner = new FakeClaudeProcessRunner(
            CreateSystemInitLine(SessionId, [], FirstEventId),
            CreateResultLine(SessionId, FinalAnswerText, SecondEventId, durationMs: 8, durationApiMs: 7, totalCostUsd: 0m, inputTokens: 3, cacheCreationInputTokens: 0, cacheReadInputTokens: 0, outputTokens: 2));
        var exec = new ClaudeExec(TestConstants.ClaudeExecutablePath, null, null, runner);
        using var client = new ClaudeClient(
            new ClaudeClientOptions
            {
                AutoStart = true,
                ClaudeOptions = new ClaudeOptions { ClaudeExecutablePath = TestConstants.ClaudeExecutablePath },
            },
            exec);
        using var thread = client.ResumeThread(SessionId);

        _ = await thread.RunAsync(HelloClaudeInput);

        var resumeFlagIndex = runner.Invocations[0].Arguments.IndexOf(ResumeFlag);
        await Assert.That(resumeFlagIndex).IsGreaterThan(-1);
        await Assert.That(runner.Invocations[0].Arguments[resumeFlagIndex + 1]).IsEqualTo(SessionId);
    }

    private static ClaudeThread CreateThread(FakeClaudeProcessRunner runner, ThreadOptions? threadOptions = null)
    {
        var exec = new ClaudeExec(TestConstants.ClaudeExecutablePath, null, null, runner);
        return new ClaudeThread(exec, new ClaudeOptions(), threadOptions ?? new ThreadOptions());
    }

    private static string CreateAssistantMessageLine(string sessionId, string text, string eventId)
    {
        return JsonSerializer.Serialize(
            new AssistantEventPayload(
                AssistantEventType,
                new AssistantMessagePayload(
                    AssistantMessageId,
                    ClaudeModels.ClaudeSonnet45Alias,
                    MessageRoleAssistant,
                    SystemStopReason,
                    MessageType,
                    new UsagePayload(11, 2, 3, 7),
                    [new TextContentPayload(TextContentType, text)]),
                sessionId,
                eventId),
            ClaudeThreadJsonContext.Default.AssistantEventPayload);
    }

    private static string CreateResultLine(
        string sessionId,
        string result,
        string eventId,
        int durationMs,
        int durationApiMs,
        decimal totalCostUsd,
        int inputTokens,
        int cacheCreationInputTokens,
        int cacheReadInputTokens,
        int outputTokens)
    {
        return JsonSerializer.Serialize(
            new ResultEventPayload(
                ResultEventType,
                SuccessSubtype,
                false,
                durationMs,
                durationApiMs,
                1,
                result,
                sessionId,
                totalCostUsd,
                new UsagePayload(inputTokens, cacheCreationInputTokens, cacheReadInputTokens, outputTokens),
                eventId),
            ClaudeThreadJsonContext.Default.ResultEventPayload);
    }

    private static string CreateSystemInitLine(string sessionId, string[] tools, string eventId)
    {
        return JsonSerializer.Serialize(
            new SystemInitEventPayload(
                SystemEventType,
                InitSubtype,
                sessionId,
                WorkspacePath,
                tools,
                [],
                ClaudeModels.ClaudeSonnet45Alias,
                DefaultPermissionMode,
                [],
                ApiKeySourceNone,
                ClaudeCodeVersion,
                OutputStyleDefault,
                [],
                [],
                [],
                eventId),
            ClaudeThreadJsonContext.Default.SystemInitEventPayload);
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

    internal sealed record AssistantEventPayload(
        string type,
        AssistantMessagePayload message,
        string session_id,
        string uuid);

    internal sealed record AssistantMessagePayload(
        string id,
        string model,
        string role,
        string stop_reason,
        string type,
        UsagePayload usage,
        TextContentPayload[] content);

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
        string[] agents,
        string[] skills,
        string[] plugins,
        string uuid);

    internal sealed record TextContentPayload(string type, string text);

    internal sealed record UsagePayload(
        int input_tokens,
        int cache_creation_input_tokens,
        int cache_read_input_tokens,
        int output_tokens);

    [JsonSerializable(typeof(AnswerPayload))]
    [JsonSerializable(typeof(AssistantEventPayload))]
    [JsonSerializable(typeof(ResultEventPayload))]
    [JsonSerializable(typeof(SystemInitEventPayload))]
    internal sealed partial class ClaudeThreadJsonContext : JsonSerializerContext;
}
