using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ManagedCode.ClaudeCodeSharpSDK.Configuration;
using ManagedCode.ClaudeCodeSharpSDK.Exceptions;
using ManagedCode.ClaudeCodeSharpSDK.Execution;
using ManagedCode.ClaudeCodeSharpSDK.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Models;

namespace ManagedCode.ClaudeCodeSharpSDK.Client;

public sealed class ClaudeThread : IDisposable
{
    private const string TypedRunRequiresOutputSchemaMessage = "Typed run requires TurnOptions.OutputSchema to be set.";
    private const string EmptyTypedRunResponseMessagePrefix = "Model returned empty structured output for type";
    private const string TypedRunDeserializeFailedMessagePrefix = "Failed to deserialize model response to type";
    private const string TypedRunRequiresTypeInfoMessage =
        "Reflection-based JSON serialization is disabled. Use RunAsync<TResponse>(..., JsonTypeInfo<TResponse>, ...) for typed output.";
    private const string ReflectionDisabledErrorToken = "Reflection-based serialization has been disabled";
    private const string AotUnsafeTypedRunMessage =
        "This overload relies on reflection-based JSON serialization and is not AOT/trimming-safe. Use the JsonTypeInfo<TResponse> overload.";
    private const string ImageInputUnsupportedMessage =
        "Claude Code print mode currently supports text-only input in this SDK.";
    private const string EventParseFailedMessagePrefix = "Failed to parse Claude Code event:";
    private const string UnsupportedInputTypeMessagePrefix = "Unsupported input type";
    private const string ParagraphSeparator = "\n\n";
    private const string Space = " ";
    private const string MessageQuote = "'";
    private const string MessageSuffix = ".";

    private readonly ClaudeExec _exec;
    private readonly ClaudeOptions _options;
    private readonly ThreadOptions _threadOptions;
    // One active turn per thread instance (ADR 002).
    private readonly SemaphoreSlim _turnGate = new(1, 1);
    private int _disposed;
    private string? _id;

    internal ClaudeThread(
        ClaudeExec exec,
        ClaudeOptions options,
        ThreadOptions threadOptions,
        string? id = null)
    {
        _exec = exec;
        _options = options;
        _threadOptions = threadOptions;
        _id = id;
    }

    public string? Id => Volatile.Read(ref _id);

    public Task<RunStreamedResult> RunStreamedAsync(string input, TurnOptions? turnOptions = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(input);
        var normalizedInput = new NormalizedInput(input);
        var resolvedTurnOptions = turnOptions ?? new TurnOptions();
        return Task.FromResult(new RunStreamedResult(RunStreamedWithTurnGateAsync(normalizedInput, resolvedTurnOptions)));
    }

    public Task<RunStreamedResult> RunStreamedAsync(IReadOnlyList<UserInput> input, TurnOptions? turnOptions = null)
    {
        ThrowIfDisposed();
        var normalizedInput = NormalizeInput(input);
        var resolvedTurnOptions = turnOptions ?? new TurnOptions();
        return Task.FromResult(new RunStreamedResult(RunStreamedWithTurnGateAsync(normalizedInput, resolvedTurnOptions)));
    }

    public Task<RunResult> RunAsync(string input, TurnOptions? turnOptions = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(input);
        var normalizedInput = new NormalizedInput(input);
        return RunInternalAsync(normalizedInput, turnOptions ?? new TurnOptions());
    }

    public Task<RunResult> RunAsync(IReadOnlyList<UserInput> input, TurnOptions? turnOptions = null)
    {
        ThrowIfDisposed();
        var normalizedInput = NormalizeInput(input);
        return RunInternalAsync(normalizedInput, turnOptions ?? new TurnOptions());
    }

    [RequiresDynamicCode(AotUnsafeTypedRunMessage)]
    [RequiresUnreferencedCode(AotUnsafeTypedRunMessage)]
    public Task<RunResult<TResponse>> RunAsync<TResponse>(string input, TurnOptions? turnOptions = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(input);
        var normalizedInput = new NormalizedInput(input);
        var runOptions = EnsureTypedRunOptions(turnOptions);
        return RunTypedInternalWithReflectionAsync<TResponse>(normalizedInput, runOptions);
    }

    public Task<RunResult<TResponse>> RunAsync<TResponse>(
        string input,
        JsonTypeInfo<TResponse> jsonTypeInfo,
        TurnOptions? turnOptions = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        var normalizedInput = new NormalizedInput(input);
        var runOptions = EnsureTypedRunOptions(turnOptions);
        return RunTypedInternalAsync(normalizedInput, runOptions, jsonTypeInfo);
    }

    [RequiresDynamicCode(AotUnsafeTypedRunMessage)]
    [RequiresUnreferencedCode(AotUnsafeTypedRunMessage)]
    public Task<RunResult<TResponse>> RunAsync<TResponse>(IReadOnlyList<UserInput> input, TurnOptions? turnOptions = null)
    {
        ThrowIfDisposed();
        var normalizedInput = NormalizeInput(input);
        var runOptions = EnsureTypedRunOptions(turnOptions);
        return RunTypedInternalWithReflectionAsync<TResponse>(normalizedInput, runOptions);
    }

    public Task<RunResult<TResponse>> RunAsync<TResponse>(
        IReadOnlyList<UserInput> input,
        JsonTypeInfo<TResponse> jsonTypeInfo,
        TurnOptions? turnOptions = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        var normalizedInput = NormalizeInput(input);
        var runOptions = EnsureTypedRunOptions(turnOptions);
        return RunTypedInternalAsync(normalizedInput, runOptions, jsonTypeInfo);
    }

    [RequiresDynamicCode(AotUnsafeTypedRunMessage)]
    [RequiresUnreferencedCode(AotUnsafeTypedRunMessage)]
    public Task<RunResult<TResponse>> RunAsync<TResponse>(
        string input,
        StructuredOutputSchema outputSchema,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(input);
        var normalizedInput = new NormalizedInput(input);
        var runOptions = CreateTypedTurnOptions(outputSchema, cancellationToken);
        return RunTypedInternalWithReflectionAsync<TResponse>(normalizedInput, runOptions);
    }

    public Task<RunResult<TResponse>> RunAsync<TResponse>(
        string input,
        StructuredOutputSchema outputSchema,
        JsonTypeInfo<TResponse> jsonTypeInfo,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        var normalizedInput = new NormalizedInput(input);
        var runOptions = CreateTypedTurnOptions(outputSchema, cancellationToken);
        return RunTypedInternalAsync(normalizedInput, runOptions, jsonTypeInfo);
    }

    [RequiresDynamicCode(AotUnsafeTypedRunMessage)]
    [RequiresUnreferencedCode(AotUnsafeTypedRunMessage)]
    public Task<RunResult<TResponse>> RunAsync<TResponse>(
        IReadOnlyList<UserInput> input,
        StructuredOutputSchema outputSchema,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var normalizedInput = NormalizeInput(input);
        var runOptions = CreateTypedTurnOptions(outputSchema, cancellationToken);
        return RunTypedInternalWithReflectionAsync<TResponse>(normalizedInput, runOptions);
    }

    public Task<RunResult<TResponse>> RunAsync<TResponse>(
        IReadOnlyList<UserInput> input,
        StructuredOutputSchema outputSchema,
        JsonTypeInfo<TResponse> jsonTypeInfo,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        var normalizedInput = NormalizeInput(input);
        var runOptions = CreateTypedTurnOptions(outputSchema, cancellationToken);
        return RunTypedInternalAsync(normalizedInput, runOptions, jsonTypeInfo);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _turnGate.Dispose();
    }

    private async Task<RunResult> RunInternalAsync(NormalizedInput normalizedInput, TurnOptions turnOptions)
    {
        var items = new List<ThreadItem>();
        var finalResponse = string.Empty;
        Usage? usage = null;

        await foreach (var threadEvent in RunStreamedWithTurnGateAsync(normalizedInput, turnOptions).ConfigureAwait(false))
        {
            switch (threadEvent)
            {
                case ItemCompletedEvent completed:
                    items.Add(completed.Item);
                    if (completed.Item is AssistantMessageItem assistantMessage && !string.IsNullOrWhiteSpace(assistantMessage.Text))
                    {
                        finalResponse = assistantMessage.Text;
                    }

                    break;

                case TurnCompletedEvent turnCompleted:
                    usage = turnCompleted.Usage;
                    if (!string.IsNullOrWhiteSpace(turnCompleted.Result))
                    {
                        finalResponse = turnCompleted.Result;
                    }

                    break;

                case TurnFailedEvent turnFailed:
                    throw new ThreadRunException(turnFailed.Error.Message);

                case ThreadErrorEvent threadError:
                    throw new ThreadRunException(threadError.Message);
            }
        }

        return new RunResult(items, finalResponse, usage);
    }

    private async IAsyncEnumerable<ThreadEvent> RunStreamedWithTurnGateAsync(
        NormalizedInput normalizedInput,
        TurnOptions turnOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            turnOptions.CancellationToken,
            cancellationToken);
        var linkedCancellationToken = linkedCancellationTokenSource.Token;

        await _turnGate.WaitAsync(linkedCancellationToken).ConfigureAwait(false);
        try
        {
            await foreach (var threadEvent in RunStreamedInternalAsync(normalizedInput, turnOptions, linkedCancellationToken)
                               .WithCancellation(linkedCancellationToken)
                               .ConfigureAwait(false))
            {
                yield return threadEvent;
            }
        }
        finally
        {
            _turnGate.Release();
        }
    }

    private async IAsyncEnumerable<ThreadEvent> RunStreamedInternalAsync(
        NormalizedInput normalizedInput,
        TurnOptions turnOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new TurnStartedEvent();

        var execArgs = new ClaudeExecArgs
        {
            Input = normalizedInput.Prompt,
            BaseUrl = _options.BaseUrl,
            ApiKey = _options.ApiKey,
            Model = _threadOptions.Model,
            Agent = _threadOptions.Agent,
            FallbackModel = _threadOptions.FallbackModel,
            WorkingDirectory = _threadOptions.WorkingDirectory,
            PermissionMode = _threadOptions.PermissionMode,
            DangerouslySkipPermissions = _threadOptions.DangerouslySkipPermissions,
            AllowDangerouslySkipPermissions = _threadOptions.AllowDangerouslySkipPermissions,
            AllowedTools = _threadOptions.AllowedTools,
            DisallowedTools = _threadOptions.DisallowedTools,
            Tools = _threadOptions.Tools,
            AdditionalDirectories = _threadOptions.AdditionalDirectories,
            McpConfigs = _threadOptions.McpConfigs,
            StrictMcpConfig = _threadOptions.StrictMcpConfig,
            SystemPrompt = _threadOptions.SystemPrompt,
            AppendSystemPrompt = _threadOptions.AppendSystemPrompt,
            ContinueMostRecent = _threadOptions.ContinueMostRecent,
            ResumeSessionId = _threadOptions.ResumeSessionId ?? _id,
            SessionId = _threadOptions.SessionId,
            ForkSession = _threadOptions.ForkSession,
            NoSessionPersistence = _threadOptions.NoSessionPersistence,
            MaxBudgetUsd = turnOptions.MaxBudgetUsd ?? _threadOptions.MaxBudgetUsd,
            Settings = _threadOptions.Settings,
            SettingSources = _threadOptions.SettingSources,
            PluginDirectories = _threadOptions.PluginDirectories,
            DisableSlashCommands = _threadOptions.DisableSlashCommands,
            Ide = _threadOptions.Ide,
            Chrome = _threadOptions.Chrome,
            Betas = _threadOptions.Betas,
            InlineAgents = _threadOptions.InlineAgents,
            JsonSchema = turnOptions.OutputSchema?.ToJsonObject().ToJsonString(),
            IncludePartialMessages = turnOptions.IncludePartialMessages,
            ReplayUserMessages = turnOptions.ReplayUserMessages,
            AdditionalCliArguments = _threadOptions.AdditionalCliArguments,
            CancellationToken = cancellationToken,
        };

        await foreach (var line in _exec.RunAsync(execArgs)
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            ThreadEvent parsedEvent;
            try
            {
                parsedEvent = ThreadEventParser.Parse(line);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(string.Concat(EventParseFailedMessagePrefix, Space, line), exception);
            }

            if (parsedEvent is ThreadStartedEvent startedEvent)
            {
                Volatile.Write(ref _id, startedEvent.ThreadId);
            }

            yield return parsedEvent;
        }
    }

    private async Task<RunResult<TResponse>> RunTypedInternalAsync<TResponse>(
        NormalizedInput normalizedInput,
        TurnOptions turnOptions,
        JsonTypeInfo<TResponse> jsonTypeInfo)
    {
        var result = await RunInternalAsync(normalizedInput, turnOptions).ConfigureAwait(false);
        var typedResponse = DeserializeTypedResponse(result.FinalResponse, jsonTypeInfo);
        return new RunResult<TResponse>(result.Items, result.FinalResponse, result.Usage, typedResponse);
    }

    [RequiresDynamicCode(AotUnsafeTypedRunMessage)]
    [RequiresUnreferencedCode(AotUnsafeTypedRunMessage)]
    private async Task<RunResult<TResponse>> RunTypedInternalWithReflectionAsync<TResponse>(
        NormalizedInput normalizedInput,
        TurnOptions turnOptions)
    {
        var result = await RunInternalAsync(normalizedInput, turnOptions).ConfigureAwait(false);
        var typedResponse = DeserializeTypedResponseWithReflection<TResponse>(result.FinalResponse);
        return new RunResult<TResponse>(result.Items, result.FinalResponse, result.Usage, typedResponse);
    }

    private static TurnOptions EnsureTypedRunOptions(TurnOptions? turnOptions)
    {
        var resolved = turnOptions ?? new TurnOptions();
        if (resolved.OutputSchema is null)
        {
            throw new InvalidOperationException(TypedRunRequiresOutputSchemaMessage);
        }

        return resolved;
    }

    private static TurnOptions CreateTypedTurnOptions(StructuredOutputSchema outputSchema, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outputSchema);
        return new TurnOptions
        {
            OutputSchema = outputSchema,
            CancellationToken = cancellationToken,
        };
    }

    private static TResponse DeserializeTypedResponse<TResponse>(string json, JsonTypeInfo<TResponse> jsonTypeInfo)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException(
                string.Concat(EmptyTypedRunResponseMessagePrefix, Space, MessageQuote, typeof(TResponse).FullName, MessageQuote, MessageSuffix));
        }

        try
        {
            return JsonSerializer.Deserialize(json, jsonTypeInfo)
                   ?? throw new InvalidOperationException(
                       string.Concat(EmptyTypedRunResponseMessagePrefix, Space, MessageQuote, typeof(TResponse).FullName, MessageQuote, MessageSuffix));
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                string.Concat(TypedRunDeserializeFailedMessagePrefix, Space, MessageQuote, typeof(TResponse).FullName, MessageQuote, MessageSuffix),
                exception);
        }
    }

    [RequiresDynamicCode(AotUnsafeTypedRunMessage)]
    [RequiresUnreferencedCode(AotUnsafeTypedRunMessage)]
    private static TResponse DeserializeTypedResponseWithReflection<TResponse>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException(
                string.Concat(EmptyTypedRunResponseMessagePrefix, Space, MessageQuote, typeof(TResponse).FullName, MessageQuote, MessageSuffix));
        }

        try
        {
            return JsonSerializer.Deserialize<TResponse>(json)
                   ?? throw new InvalidOperationException(
                       string.Concat(EmptyTypedRunResponseMessagePrefix, Space, MessageQuote, typeof(TResponse).FullName, MessageQuote, MessageSuffix));
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains(ReflectionDisabledErrorToken, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(TypedRunRequiresTypeInfoMessage, exception);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                string.Concat(TypedRunDeserializeFailedMessagePrefix, Space, MessageQuote, typeof(TResponse).FullName, MessageQuote, MessageSuffix),
                exception);
        }
    }

    private static NormalizedInput NormalizeInput(IReadOnlyList<UserInput> input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var promptParts = new List<string>();
        foreach (var item in input)
        {
            switch (item)
            {
                case TextInput text:
                    if (!string.IsNullOrWhiteSpace(text.Text))
                    {
                        promptParts.Add(text.Text);
                    }

                    break;

                case LocalImageInput:
                    throw new NotSupportedException(ImageInputUnsupportedMessage);

                case null:
                    break;

                default:
                    throw new NotSupportedException(
                        string.Concat(UnsupportedInputTypeMessagePrefix, Space, MessageQuote, item.GetType().Name, MessageQuote, MessageSuffix));
            }
        }

        return new NormalizedInput(string.Join(ParagraphSeparator, promptParts));
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, nameof(ClaudeThread));

    private readonly record struct NormalizedInput(string Prompt);
}
