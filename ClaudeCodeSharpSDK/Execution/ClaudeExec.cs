using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Configuration;
using ManagedCode.ClaudeCodeSharpSDK.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Logging;
using ManagedCode.ClaudeCodeSharpSDK.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ManagedCode.ClaudeCodeSharpSDK.Execution;

public sealed class ClaudeExec
{
    private const string ShortPrintFlag = "-p";
    private const string PrintFlag = "--print";
    private const string OutputFormatFlag = "--output-format";
    private const string InputFormatFlag = "--input-format";
    private const string JsonSchemaFlag = "--json-schema";
    private const string IncludePartialMessagesFlag = "--include-partial-messages";
    private const string ReplayUserMessagesFlag = "--replay-user-messages";
    private const string ModelFlag = "--model";
    private const string AgentFlag = "--agent";
    private const string FallbackModelFlag = "--fallback-model";
    private const string PermissionModeFlag = "--permission-mode";
    private const string DangerouslySkipPermissionsFlag = "--dangerously-skip-permissions";
    private const string AllowDangerouslySkipPermissionsFlag = "--allow-dangerously-skip-permissions";
    private const string AllowedToolsFlag = "--allowed-tools";
    private const string DisallowedToolsFlag = "--disallowed-tools";
    private const string ToolsFlag = "--tools";
    private const string AddDirectoryFlag = "--add-dir";
    private const string McpConfigFlag = "--mcp-config";
    private const string StrictMcpConfigFlag = "--strict-mcp-config";
    private const string SystemPromptFlag = "--system-prompt";
    private const string AppendSystemPromptFlag = "--append-system-prompt";
    private const string ContinueFlag = "--continue";
    private const string ResumeFlag = "--resume";
    private const string SessionIdFlag = "--session-id";
    private const string ForkSessionFlag = "--fork-session";
    private const string NoSessionPersistenceFlag = "--no-session-persistence";
    private const string MaxBudgetUsdFlag = "--max-budget-usd";
    private const string SettingsFlag = "--settings";
    private const string SettingSourcesFlag = "--setting-sources";
    private const string PluginDirectoryFlag = "--plugin-dir";
    private const string DisableSlashCommandsFlag = "--disable-slash-commands";
    private const string IdeFlag = "--ide";
    private const string ChromeFlag = "--chrome";
    private const string NoChromeFlag = "--no-chrome";
    private const string BetasFlag = "--betas";
    private const string AgentsFlag = "--agents";
    private const string VerboseFlag = "--verbose";

    private const string StreamJsonFormat = "stream-json";
    private const string TextFormat = "text";

    private const string AnthropicApiKeyEnv = "ANTHROPIC_API_KEY";
    private const string AnthropicBaseUrlEnv = "ANTHROPIC_BASE_URL";
    private const string ClaudeCodeNestingEnv = "CLAUDECODE";
    private const string ContinueAndResumeConflictMessage = "ContinueMostRecent and ResumeSessionId cannot both be set.";
    private const string FlagAssignmentSeparator = "=";
    private const string ReplayUserMessagesUnsupportedMessage =
        "ReplayUserMessages requires Claude Code stream-json input, which this SDK does not support yet.";
    private const string AdditionalCliArgumentsReservedFlagMessagePrefix =
        "AdditionalCliArguments cannot override SDK-managed Claude Code flag";
    private const string Space = " ";
    private const string MessageQuote = "'";
    private const string MessageSuffix = ".";
    private const string ProcessTerminationTimeoutMustBePositiveMessage = "Process termination timeout must be positive.";

    private static readonly HashSet<string> ReservedAdditionalCliFlags = new(StringComparer.Ordinal)
    {
        ShortPrintFlag,
        PrintFlag,
        OutputFormatFlag,
        InputFormatFlag,
        JsonSchemaFlag,
        IncludePartialMessagesFlag,
        ReplayUserMessagesFlag,
        VerboseFlag,
    };

    private readonly Lazy<CliLaunchCommand> _cliLaunchCommand;
    private readonly IReadOnlyDictionary<string, string>? _environmentOverride;
    private readonly bool _inheritEnvironmentVariables;
    private readonly TimeSpan _processTerminationTimeout;
    private readonly int _maximumProcessOutputCharacters;
    private readonly JsonObject? _baseSettings;
    private readonly IClaudeProcessRunner _processRunner;
    private readonly ILogger _logger;

    public ClaudeExec(
        string? executablePath = null,
        IReadOnlyDictionary<string, string>? environmentOverride = null,
        JsonObject? baseSettings = null,
        ILogger? logger = null)
        : this(executablePath, environmentOverride, baseSettings, null, logger, true, ClaudeOptions.DefaultProcessTerminationTimeout)
    {
    }

    public ClaudeExec(
        bool inheritEnvironmentVariables,
        string? executablePath = null,
        IReadOnlyDictionary<string, string>? environmentOverride = null,
        JsonObject? baseSettings = null,
        ILogger? logger = null)
        : this(executablePath, environmentOverride, baseSettings, null, logger, inheritEnvironmentVariables, ClaudeOptions.DefaultProcessTerminationTimeout)
    {
    }

    public ClaudeExec(
        bool inheritEnvironmentVariables,
        TimeSpan processTerminationTimeout,
        string? executablePath = null,
        IReadOnlyDictionary<string, string>? environmentOverride = null,
        JsonObject? baseSettings = null,
        ILogger? logger = null)
        : this(executablePath, environmentOverride, baseSettings, null, logger, inheritEnvironmentVariables, processTerminationTimeout)
    {
    }

    internal ClaudeExec(
        string? executablePath,
        IReadOnlyDictionary<string, string>? environmentOverride,
        JsonObject? baseSettings,
        IClaudeProcessRunner? processRunner,
        ILogger? logger = null,
        bool inheritEnvironmentVariables = true,
        TimeSpan? processTerminationTimeout = null,
        int maximumProcessOutputCharacters = ClaudeOptions.DefaultMaximumProcessOutputCharacters,
        int cliMetadataMaximumFileCharacters = ClaudeOptions.DefaultCliMetadataMaximumFileCharacters,
        CliLaunchCommand? launchCommand = null)
    {
        var resolvedTerminationTimeout = processTerminationTimeout ?? ClaudeOptions.DefaultProcessTerminationTimeout;
        if (resolvedTerminationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(processTerminationTimeout), resolvedTerminationTimeout, ProcessTerminationTimeoutMustBePositiveMessage);
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumProcessOutputCharacters);

        _environmentOverride = environmentOverride;
        _inheritEnvironmentVariables = inheritEnvironmentVariables;
        _cliLaunchCommand = new Lazy<CliLaunchCommand>(() => launchCommand ?? ClaudeCliLocator.FindClaudeCommand(
            executablePath, BuildEnvironment(null, null), cliMetadataMaximumFileCharacters));
        _processTerminationTimeout = resolvedTerminationTimeout;
        _maximumProcessOutputCharacters = maximumProcessOutputCharacters;
        _baseSettings = baseSettings;
        _processRunner = processRunner ?? new DefaultClaudeProcessRunner();
        _logger = logger ?? NullLogger.Instance;
    }

    public IAsyncEnumerable<string> RunAsync(ClaudeExecArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return RunWithDiagnosticsAsync(args, args.CancellationToken);
    }

    internal CliLaunchCommand GetCliLaunchCommand() => _cliLaunchCommand.Value;

    internal IReadOnlyList<string> BuildCommandArgs(ClaudeExecArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        ValidateArgs(args);

        if (args.ContinueMostRecent && !string.IsNullOrWhiteSpace(args.ResumeSessionId))
        {
            throw new InvalidOperationException(ContinueAndResumeConflictMessage);
        }

        var commandArgs = new List<string>
        {
            PrintFlag,
            OutputFormatFlag, StreamJsonFormat,
            InputFormatFlag, TextFormat,
            VerboseFlag,
        };

        if (!string.IsNullOrWhiteSpace(args.JsonSchema))
        {
            commandArgs.Add(JsonSchemaFlag);
            commandArgs.Add(args.JsonSchema);
        }

        if (args.IncludePartialMessages)
        {
            commandArgs.Add(IncludePartialMessagesFlag);
        }

        if (!string.IsNullOrWhiteSpace(args.Model))
        {
            commandArgs.Add(ModelFlag);
            commandArgs.Add(args.Model);
        }

        if (!string.IsNullOrWhiteSpace(args.Agent))
        {
            commandArgs.Add(AgentFlag);
            commandArgs.Add(args.Agent);
        }

        if (!string.IsNullOrWhiteSpace(args.FallbackModel))
        {
            commandArgs.Add(FallbackModelFlag);
            commandArgs.Add(args.FallbackModel);
        }

        if (args.PermissionMode.HasValue)
        {
            commandArgs.Add(PermissionModeFlag);
            commandArgs.Add(args.PermissionMode.Value.ToCliValue());
        }

        if (args.DangerouslySkipPermissions)
        {
            commandArgs.Add(DangerouslySkipPermissionsFlag);
        }

        if (args.AllowDangerouslySkipPermissions)
        {
            commandArgs.Add(AllowDangerouslySkipPermissionsFlag);
        }

        AddJoinedFlag(commandArgs, AllowedToolsFlag, args.AllowedTools);
        AddJoinedFlag(commandArgs, DisallowedToolsFlag, args.DisallowedTools);
        AddJoinedFlag(commandArgs, ToolsFlag, args.Tools);
        AddRepeatedFlag(commandArgs, AddDirectoryFlag, args.AdditionalDirectories);
        AddRepeatedFlag(commandArgs, McpConfigFlag, args.McpConfigs);
        AddRepeatedFlag(commandArgs, PluginDirectoryFlag, args.PluginDirectories);
        AddRepeatedFlag(commandArgs, BetasFlag, args.Betas);

        if (args.StrictMcpConfig)
        {
            commandArgs.Add(StrictMcpConfigFlag);
        }

        if (!string.IsNullOrWhiteSpace(args.SystemPrompt))
        {
            commandArgs.Add(SystemPromptFlag);
            commandArgs.Add(args.SystemPrompt);
        }

        if (!string.IsNullOrWhiteSpace(args.AppendSystemPrompt))
        {
            commandArgs.Add(AppendSystemPromptFlag);
            commandArgs.Add(args.AppendSystemPrompt);
        }

        if (args.ContinueMostRecent)
        {
            commandArgs.Add(ContinueFlag);
        }
        else if (!string.IsNullOrWhiteSpace(args.ResumeSessionId))
        {
            commandArgs.Add(ResumeFlag);
            commandArgs.Add(args.ResumeSessionId);
        }

        if (!string.IsNullOrWhiteSpace(args.SessionId))
        {
            commandArgs.Add(SessionIdFlag);
            commandArgs.Add(args.SessionId);
        }

        if (args.ForkSession)
        {
            commandArgs.Add(ForkSessionFlag);
        }

        if (args.NoSessionPersistence)
        {
            commandArgs.Add(NoSessionPersistenceFlag);
        }

        var maxBudget = args.MaxBudgetUsd;
        if (maxBudget.HasValue)
        {
            commandArgs.Add(MaxBudgetUsdFlag);
            commandArgs.Add(maxBudget.Value.ToString(CultureInfo.InvariantCulture));
        }

        var settings = MergeSettings(_baseSettings, args.Settings);
        if (settings is not null)
        {
            commandArgs.Add(SettingsFlag);
            commandArgs.Add(settings.ToJsonString());
        }

        if (args.SettingSources is { Count: > 0 })
        {
            commandArgs.Add(SettingSourcesFlag);
            commandArgs.Add(string.Join(',', args.SettingSources.Select(static source => source.ToCliValue())));
        }

        if (args.DisableSlashCommands)
        {
            commandArgs.Add(DisableSlashCommandsFlag);
        }

        if (args.Ide == true)
        {
            commandArgs.Add(IdeFlag);
        }

        if (args.Chrome == true)
        {
            commandArgs.Add(ChromeFlag);
        }
        else if (args.Chrome == false)
        {
            commandArgs.Add(NoChromeFlag);
        }

        if (args.InlineAgents is { Count: > 0 })
        {
            commandArgs.Add(AgentsFlag);
            var inlineAgents = args.InlineAgents as Dictionary<string, InlineAgentDefinition>
                               ?? args.InlineAgents.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
            commandArgs.Add(JsonSerializer.Serialize(inlineAgents, ClaudeJsonSerializerContext.Default.DictionaryStringInlineAgentDefinition));
        }

        if (args.AdditionalCliArguments is not null)
        {
            foreach (var argument in args.AdditionalCliArguments)
            {
                if (!string.IsNullOrWhiteSpace(argument))
                {
                    commandArgs.Add(argument);
                }
            }
        }

        return commandArgs;
    }

    internal IReadOnlyDictionary<string, string> BuildEnvironment(string? baseUrl, string? apiKey)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);

        if (_inheritEnvironmentVariables)
        {
            foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
            {
                if (variable.Key is string key
                    && variable.Value is string value
                    && !string.Equals(key, ClaudeCodeNestingEnv, StringComparison.OrdinalIgnoreCase))
                {
                    environment[key] = value;
                }
            }
        }

        if (_environmentOverride is not null)
        {
            foreach (var (key, value) in _environmentOverride)
            {
                environment[key] = value;
            }
        }

        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            environment[AnthropicBaseUrlEnv] = baseUrl;
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            environment[AnthropicApiKeyEnv] = apiKey;
        }

        return environment;
    }

    private async IAsyncEnumerable<string> RunWithDiagnosticsAsync(
        ClaudeExecArgs args,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            ClaudeExecLog.Cancelled(_logger);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var command = _cliLaunchCommand.Value;
        var workingDirectory = string.IsNullOrWhiteSpace(args.WorkingDirectory)
            ? Environment.CurrentDirectory
            : args.WorkingDirectory;
        var invocation = new ClaudeProcessInvocation(
            command.ExecutablePath,
            workingDirectory,
            BuildCommandArgs(args),
            BuildEnvironment(args.BaseUrl, args.ApiKey),
            args.Input,
            _processTerminationTimeout)
        {
            PrefixArguments = command.PrefixArguments,
            MaximumProcessOutputCharacters = _maximumProcessOutputCharacters,
        };
        ClaudeExecLog.Starting(_logger, invocation.ExecutablePath, invocation.Arguments.Count);

        var lineCount = 0;

        IAsyncEnumerator<string> enumerator;
        try
        {
            enumerator = _processRunner
                .RunAsync(invocation, _logger, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ClaudeExecLog.Cancelled(_logger);
            throw;
        }
        catch (Exception)
        {
            ClaudeExecLog.Failed(_logger);
            throw;
        }

        await using (enumerator)
        {
            while (true)
            {
                string line;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }

                    line = enumerator.Current;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    ClaudeExecLog.Cancelled(_logger);
                    throw;
                }
                catch (Exception)
                {
                    ClaudeExecLog.Failed(_logger);
                    throw;
                }

                lineCount += 1;
                yield return line;
            }
        }

        ClaudeExecLog.Completed(_logger, lineCount);
    }

    private static void AddJoinedFlag(List<string> commandArgs, string flag, IReadOnlyList<string>? values)
    {
        if (values is not { Count: > 0 })
        {
            return;
        }

        var materialized = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        if (materialized.Length == 0)
        {
            return;
        }

        commandArgs.Add(flag);
        commandArgs.Add(string.Join(',', materialized));
    }

    private static void AddRepeatedFlag(List<string> commandArgs, string flag, IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            commandArgs.Add(flag);
            commandArgs.Add(value);
        }
    }

    private static void ValidateArgs(ClaudeExecArgs args)
    {
        if (args.ReplayUserMessages)
        {
            throw new InvalidOperationException(ReplayUserMessagesUnsupportedMessage);
        }

        if (TryFindReservedAdditionalCliFlag(args.AdditionalCliArguments, out var reservedFlag))
        {
            throw new InvalidOperationException(
                string.Concat(AdditionalCliArgumentsReservedFlagMessagePrefix, Space, MessageQuote, reservedFlag, MessageQuote, MessageSuffix));
        }
    }

    private static bool TryFindReservedAdditionalCliFlag(IReadOnlyList<string>? additionalCliArguments, out string reservedFlag)
    {
        reservedFlag = string.Empty;

        if (additionalCliArguments is null)
        {
            return false;
        }

        foreach (var argument in additionalCliArguments)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                continue;
            }

            foreach (var flag in ReservedAdditionalCliFlags)
            {
                if (string.Equals(argument, flag, StringComparison.Ordinal)
                    || argument.StartsWith(string.Concat(flag, FlagAssignmentSeparator), StringComparison.Ordinal))
                {
                    reservedFlag = flag;
                    return true;
                }
            }
        }

        return false;
    }

    private static JsonObject? MergeSettings(JsonObject? baseSettings, JsonObject? perTurnSettings)
    {
        if (baseSettings is null && perTurnSettings is null)
        {
            return null;
        }

        var merged = baseSettings is null
            ? new JsonObject()
            : JsonNode.Parse(baseSettings.ToJsonString())?.AsObject() ?? new JsonObject();

        if (perTurnSettings is null)
        {
            return merged;
        }

        MergeInto(merged, perTurnSettings);
        return merged;
    }

    private static void MergeInto(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source)
        {
            if (value is JsonObject sourceObject
                && target[key] is JsonObject targetObject)
            {
                MergeInto(targetObject, sourceObject);
                continue;
            }

            target[key] = value?.DeepClone();
        }
    }
}

internal sealed record ClaudeProcessInvocation(
    string ExecutablePath,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    string Input,
    TimeSpan ProcessTerminationTimeout)
{
    public ImmutableArray<string> PrefixArguments { get; init; } = [];

    public int MaximumProcessOutputCharacters { get; init; } = ClaudeOptions.DefaultMaximumProcessOutputCharacters;

    public Action? StandardErrorReaderCompleted { get; init; }

    public Action? StandardOutputReadCompleted { get; init; }

    public Action? StandardErrorOutputLimitExceeded { get; init; }

    public Action<bool>? StandardInputWriteFailed { get; init; }
}

internal interface IClaudeProcessRunner
{
    IAsyncEnumerable<string> RunAsync(
        ClaudeProcessInvocation invocation,
        ILogger logger,
        CancellationToken cancellationToken);
}

internal sealed class DefaultClaudeProcessRunner : IClaudeProcessRunner
{
    private const string StartExecutableFailedMessagePrefix = "Failed to start Claude Code executable";
    private const string ProcessFailedWithoutStderrMessage = "Claude Code process failed without stderr output.";
    private const string CliExitedWithCodeMessagePrefix = "Claude Code CLI exited with code";
    private const string ProcessTerminationUnconfirmedMessage = "Could not confirm that the Claude Code process exited after termination was requested.";
    private const string StderrTerminationUnconfirmedMessage = "Could not confirm that the Claude Code stderr stream closed within the configured process termination timeout.";
    private const string ProcessOutputLimitExceededMessage = "Claude Code process exceeded the configured output limit.";
    private const string StandardOutputTerminationUnconfirmedMessage = "Claude Code standard output did not close within the configured time limit.";
    private const string ProcessAndReaderCleanupUnconfirmedMessage = "Claude Code process and output cleanup could not be confirmed.";
    private const string Space = " ";
    private const string MessageQuote = "'";
    private const string MessageSuffix = ".";
    private const string PeriodSpace = ". ";
    private const int ProcessOutputBufferCharacters = 4096;

    public async IAsyncEnumerable<string> RunAsync(
        ClaudeProcessInvocation invocation,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(invocation.ExecutablePath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = invocation.WorkingDirectory,
        };

        foreach (var argument in invocation.PrefixArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Clear();
        foreach (var (key, value) in invocation.Environment)
        {
            startInfo.Environment[key] = value;
        }

        using var process = new Process { StartInfo = startInfo };
        Task<BoundedProcessOutput>? standardErrorTask = null;
        Task<string?>? standardOutputReadTask = null;
        Task? standardInputWriteTask = null;
        BoundedProcessOutputReader? standardOutputReader = null;
        using var outputCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var cancellationSignal = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        var standardErrorLimitExceeded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? cleanupFailure = null;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(
                    string.Concat(StartExecutableFailedMessagePrefix, Space, MessageQuote, invocation.ExecutablePath, MessageQuote, MessageSuffix));
            }
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                string.Concat(StartExecutableFailedMessagePrefix, Space, MessageQuote, invocation.ExecutablePath, MessageQuote, MessageSuffix),
                exception);
        }

        try
        {
            standardErrorTask = ReadBoundedProcessOutputAsync(process.StandardError,
                invocation.MaximumProcessOutputCharacters, CancellationToken.None, () =>
                {
                    standardErrorLimitExceeded.TrySetResult(true);
                    invocation.StandardErrorOutputLimitExceeded?.Invoke();
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (Exception)
                    {
                        // The bounded cleanup path below reports whether process exit was confirmed.
                    }

                    outputCancellation.Cancel();
                }, invocation.StandardErrorReaderCompleted);
            standardOutputReader = new BoundedProcessOutputReader(process.StandardOutput,
                invocation.MaximumProcessOutputCharacters, invocation.StandardOutputReadCompleted);
            standardOutputReadTask = standardOutputReader.ReadLineAsync(CancellationToken.None).AsTask();
            standardInputWriteTask = WriteStandardInputAsync(process.StandardInput, invocation.Input,
                invocation.StandardInputWriteFailed is null
                    ? null
                    : () => invocation.StandardInputWriteFailed(process.HasExited),
                outputCancellation.Token);
            string? line;
            while (true)
            {
                var readLineTask = standardOutputReadTask!;
                var completedTask = standardInputWriteTask is null
                    ? await Task.WhenAny(readLineTask, standardErrorLimitExceeded.Task, cancellationSignal).ConfigureAwait(false)
                    : await Task.WhenAny(readLineTask, standardErrorLimitExceeded.Task, standardInputWriteTask, cancellationSignal).ConfigureAwait(false);
                if (completedTask == cancellationSignal)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                if (completedTask == standardErrorLimitExceeded.Task || standardErrorLimitExceeded.Task.IsCompleted)
                {
                    try
                    {
                        await readLineTask.WaitAsync(invocation.ProcessTerminationTimeout, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // The stderr overflow handler canceled the sibling reader after requesting process exit.
                    }
                    catch (TimeoutException)
                    {
                        // Closing the redirected stream in finally gets a second bounded chance to settle the read.
                    }

                    await EnsureProcessStoppedAsync(process, logger, invocation.ExecutablePath,
                        invocation.ProcessTerminationTimeout).ConfigureAwait(false);
                    throw new InvalidOperationException(ProcessOutputLimitExceededMessage);
                }

                if (standardInputWriteTask is not null && completedTask == standardInputWriteTask)
                {
                    await AwaitStandardInputWriteAsync(standardInputWriteTask, process, standardErrorTask!,
                        invocation.ProcessTerminationTimeout, cancellationToken).ConfigureAwait(false);
                    standardInputWriteTask = null;
                    continue;
                }

                try
                {
                    line = await readLineTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    standardOutputReadTask = null;
                    await ThrowIfExitedWithFailureAsync(process, standardErrorTask, invocation.ProcessTerminationTimeout)
                        .ConfigureAwait(false);
                    throw;
                }
                standardOutputReadTask = null;
                if (line is null)
                {
                    if (standardInputWriteTask is not null)
                    {
                        await AwaitStandardInputWriteAsync(standardInputWriteTask, process, standardErrorTask!,
                            invocation.ProcessTerminationTimeout, cancellationToken).ConfigureAwait(false);
                        standardInputWriteTask = null;
                    }
                    break;
                }

                if (line.Length == 0)
                {
                    standardOutputReadTask = standardOutputReader.ReadLineAsync(CancellationToken.None).AsTask();
                    continue;
                }

                yield return line;
                standardOutputReadTask = standardOutputReader.ReadLineAsync(CancellationToken.None).AsTask();
            }

            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await ThrowIfExitedWithFailureAsync(process, standardErrorTask, invocation.ProcessTerminationTimeout).ConfigureAwait(false);
                throw;
            }

            var capturedStandardError = await ReadStandardErrorAsync(standardErrorTask, invocation.ProcessTerminationTimeout).ConfigureAwait(false);
            var standardError = capturedStandardError.Text;

            if (process.ExitCode != 0)
            {
                var details = string.IsNullOrWhiteSpace(standardError)
                    ? ProcessFailedWithoutStderrMessage
                    : standardError.Trim();
                throw CliExecutionFailureException.FromProcessExit(process.ExitCode,
                    string.Concat(CliExitedWithCodeMessagePrefix, Space, process.ExitCode.ToString(CultureInfo.InvariantCulture), PeriodSpace, details));
            }
        }
        finally
        {
            Exception? processExitFailure = null;
            try
            {
                await EnsureProcessStoppedAsync(process, logger, invocation.ExecutablePath,
                    invocation.ProcessTerminationTimeout).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                processExitFailure = exception;
            }

            var readerFailures = new List<Exception>(4);
            try
            {
                process.StandardInput.BaseStream.Dispose();
            }
            catch (Exception exception)
            {
                readerFailures.Add(exception);
            }

            if (standardInputWriteTask is not null)
            {
                try
                {
                    await standardInputWriteTask.WaitAsync(invocation.ProcessTerminationTimeout, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (outputCancellation.IsCancellationRequested && standardInputWriteTask.IsCanceled)
                {
                    // Owned I/O cancellation ends the stdin pump after root termination was requested.
                }
                catch (IOException) when (standardErrorLimitExceeded.Task.IsCompleted &&
                                          standardInputWriteTask.IsFaulted &&
                                          standardInputWriteTask.Exception?.GetBaseException() is IOException)
                {
                    // The visible stderr output-limit failure owns this induced broken pipe.
                }
                catch (IOException) when (process.HasExited && process.ExitCode != 0 &&
                                          standardInputWriteTask.IsFaulted &&
                                          standardInputWriteTask.Exception?.GetBaseException() is IOException)
                {
                    // The confirmed nonzero root exit owns this completed stdin broken pipe.
                }
                catch (Exception exception)
                {
                    readerFailures.Add(exception);
                }
            }

            if (standardOutputReader is not null && !standardOutputReader.ReachedEndOfStream)
            {
                var drainResult = await DrainStandardOutputToEofAsync(standardOutputReader, standardOutputReadTask,
                    invocation.ProcessTerminationTimeout).ConfigureAwait(false);
                standardOutputReadTask = drainResult.PendingRead;
                if (drainResult.Failure is not null)
                {
                    readerFailures.Add(drainResult.Failure);
                }
            }

            if (standardErrorTask is not null)
            {
                try
                {
                    await ReadStandardErrorAsync(standardErrorTask, invocation.ProcessTerminationTimeout).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    readerFailures.Add(exception);
                }
            }

            try
            {
                outputCancellation.Cancel();
            }
            catch (Exception exception)
            {
                readerFailures.Add(exception);
            }

            try
            {
                process.StandardOutput.Dispose();
            }
            catch (Exception exception)
            {
                readerFailures.Add(exception);
            }

            try
            {
                process.StandardError.Dispose();
            }
            catch (Exception exception)
            {
                readerFailures.Add(exception);
            }
            var standardOutputFailure = await ObserveStandardOutputReadAsync(
                standardOutputReadTask, invocation.ProcessTerminationTimeout).ConfigureAwait(false);
            if (standardOutputReader is not null && (standardOutputReadTask is null || standardOutputReadTask.IsCompleted))
            {
                standardOutputReader.SignalReadCompleted();
            }
            var standardOutputLimitFailureIsExpected = standardOutputReadTask is { IsFaulted: true } &&
                standardOutputReadTask.Exception?.GetBaseException() is InvalidOperationException standardOutputLimitException &&
                string.Equals(standardOutputLimitException.Message, ProcessOutputLimitExceededMessage, StringComparison.Ordinal);
            Exception? standardErrorFailure = null;
            if (standardErrorTask is not null)
            {
                try
                {
                    await ReadStandardErrorAsync(standardErrorTask, invocation.ProcessTerminationTimeout).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    standardErrorFailure = exception;
                }
            }

            if (standardOutputFailure is not null && !standardOutputLimitFailureIsExpected)
            {
                readerFailures.Add(standardOutputFailure);
            }

            var standardErrorLimitFailureIsExpected = standardErrorTask is { IsFaulted: true } &&
                standardErrorTask.Exception?.GetBaseException() is InvalidOperationException standardErrorLimitException &&
                string.Equals(standardErrorLimitException.Message, ProcessOutputLimitExceededMessage, StringComparison.Ordinal);
            if (standardErrorFailure is not null && !standardErrorLimitFailureIsExpected)
            {
                readerFailures.Add(standardErrorFailure);
            }

            if (processExitFailure is not null && readerFailures.Count > 0)
            {
                cleanupFailure = new InvalidOperationException(
                    processExitFailure.Message,
                    new AggregateException(new[] { processExitFailure }.Concat(readerFailures)));
            }
            else if (processExitFailure is not null)
            {
                cleanupFailure = processExitFailure;
            }
            else if (readerFailures.Count == 1)
            {
                cleanupFailure = readerFailures[0];
            }
            else if (readerFailures.Count > 1)
            {
                cleanupFailure = new InvalidOperationException(
                    ProcessAndReaderCleanupUnconfirmedMessage,
                    new AggregateException(readerFailures));
            }

            if (cleanupFailure is not null)
            {
                ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
            }
        }
    }

    private static async Task<Exception?> ObserveStandardOutputReadAsync(
        Task<string?>? standardOutputReadTask,
        TimeSpan timeout)
    {
        if (standardOutputReadTask is null)
        {
            return null;
        }

        try
        {
            await standardOutputReadTask.WaitAsync(timeout).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (TimeoutException exception)
        {
            return new InvalidOperationException(StandardOutputTerminationUnconfirmedMessage, exception);
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task<StandardOutputDrainResult> DrainStandardOutputToEofAsync(
        BoundedProcessOutputReader reader,
        Task<string?>? pendingRead,
        TimeSpan timeout)
    {
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        var currentRead = pendingRead;
        try
        {
            while (true)
            {
                currentRead ??= reader.ReadLineAsync(CancellationToken.None).AsTask();
                var line = await currentRead.WaitAsync(timeoutCancellation.Token).ConfigureAwait(false);
                currentRead = null;
                if (line is null)
                {
                    return new StandardOutputDrainResult(null, null);
                }
            }
        }
        catch (OperationCanceledException exception) when (timeoutCancellation.IsCancellationRequested)
        {
            return new StandardOutputDrainResult(currentRead,
                new InvalidOperationException(StandardOutputTerminationUnconfirmedMessage, exception));
        }
        catch (Exception exception)
        {
            return new StandardOutputDrainResult(currentRead, exception);
        }
    }

    private static async Task<BoundedProcessOutput> ReadBoundedProcessOutputAsync(
        TextReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken,
        Action? onLimitExceeded = null,
        Action? onCompleted = null)
    {
        var output = new StringBuilder(Math.Min(maximumCharacters, ProcessOutputBufferCharacters));
        var buffer = new char[ProcessOutputBufferCharacters];
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
            {
                var remaining = maximumCharacters - output.Length;
                var append = Math.Min(remaining, read);
                if (append > 0)
                {
                    output.Append(buffer, 0, append);
                }

                if (append < read)
                {
                    onLimitExceeded?.Invoke();
                    throw new InvalidOperationException(ProcessOutputLimitExceededMessage);
                }
            }
        }
        finally
        {
            onCompleted?.Invoke();
        }

        return new BoundedProcessOutput(output.ToString());
    }

    private static async Task WriteStandardInputAsync(
        StreamWriter standardInput,
        string input,
        Action? onWriteFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            await standardInput.WriteAsync(input.AsMemory(), cancellationToken).ConfigureAwait(false);
            await standardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            await standardInput.DisposeAsync().ConfigureAwait(false);
        }
        catch (IOException)
        {
            onWriteFailure?.Invoke();
            throw;
        }
    }

    private static async Task AwaitStandardInputWriteAsync(
        Task standardInputWriteTask,
        Process process,
        Task<BoundedProcessOutput> standardErrorTask,
        TimeSpan processTerminationTimeout,
        CancellationToken cancellationToken)
    {
        try
        {
            await standardInputWriteTask.WaitAsync(processTerminationTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ThrowIfExitedWithFailureAsync(process, standardErrorTask, processTerminationTimeout).ConfigureAwait(false);
            throw;
        }
        catch (IOException) when (standardInputWriteTask.IsFaulted &&
                                  standardInputWriteTask.Exception?.GetBaseException() is IOException)
        {
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(processTerminationTimeout, CancellationToken.None)
                .ConfigureAwait(false);
            await ThrowIfExitedWithFailureAsync(process, standardErrorTask, processTerminationTimeout).ConfigureAwait(false);
            throw;
        }
        catch (Exception) when (process.HasExited)
        {
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(processTerminationTimeout, cancellationToken).ConfigureAwait(false);
            await ThrowIfExitedWithFailureAsync(process, standardErrorTask, processTerminationTimeout).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task ThrowIfExitedWithFailureAsync(
        Process process,
        Task<BoundedProcessOutput> standardErrorTask,
        TimeSpan processTerminationTimeout)
    {
        if (!process.HasExited || process.ExitCode == 0)
        {
            return;
        }

        var standardError = (await ReadStandardErrorAsync(standardErrorTask, processTerminationTimeout).ConfigureAwait(false)).Text;
        var details = string.IsNullOrWhiteSpace(standardError)
            ? ProcessFailedWithoutStderrMessage
            : standardError.Trim();
        throw CliExecutionFailureException.FromProcessExit(process.ExitCode,
            string.Concat(CliExitedWithCodeMessagePrefix, Space, process.ExitCode.ToString(CultureInfo.InvariantCulture), PeriodSpace, details));
    }

    private static async Task<BoundedProcessOutput> ReadStandardErrorAsync(Task<BoundedProcessOutput> standardErrorTask, TimeSpan timeout)
    {
        try
        {
            return await standardErrorTask.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException(StderrTerminationUnconfirmedMessage, exception);
        }
    }

    private static async Task EnsureProcessStoppedAsync(
        Process process,
        ILogger logger,
        string executablePath,
        TimeSpan processTerminationTimeout)
    {
        if (process.HasExited)
        {
            return;
        }

        Exception? killException = null;
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception)
        {
            ClaudeExecLog.ProcessKillFailed(logger, executablePath);
            killException = exception;
        }

        try
        {
            await process.WaitForExitAsync().WaitAsync(processTerminationTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            Exception failure = killException is null ? exception : new AggregateException(killException, exception);
            ClaudeExecLog.ProcessKillFailed(logger, executablePath);
            throw new InvalidOperationException(ProcessTerminationUnconfirmedMessage, failure);
        }
    }
}

internal sealed record BoundedProcessOutput(string Text);
internal sealed record StandardOutputDrainResult(Task<string?>? PendingRead, Exception? Failure);

internal sealed class BoundedProcessOutputReader(
    TextReader reader,
    int maximumCharacters,
    Action? onReadCompleted = null)
{
    private const int BufferCharacters = 4096;
    private const string OutputLimitExceededMessage = "Claude Code process exceeded the configured output limit.";
    private readonly char[] _buffer = new char[BufferCharacters];
    private readonly StringBuilder _line = new(Math.Min(maximumCharacters, BufferCharacters));
    private int _bufferCount;
    private int _bufferIndex;
    private int _charactersRead;
    private bool _endOfStream;

    internal bool ReachedEndOfStream => _endOfStream;

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_bufferIndex >= _bufferCount)
            {
                if (_endOfStream)
                {
                    return TakeFinalLine();
                }

                _bufferCount = await reader.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                _bufferIndex = 0;
                if (_bufferCount == 0)
                {
                    _endOfStream = true;
                    return TakeFinalLine();
                }
            }

            var character = _buffer[_bufferIndex++];
            if (_charactersRead >= maximumCharacters)
            {
                throw new InvalidOperationException(OutputLimitExceededMessage);
            }

            _charactersRead++;
            if (character == '\n')
            {
                if (_line.Length > 0 && _line[^1] == '\r')
                {
                    _line.Length--;
                }

                var result = _line.ToString();
                _line.Clear();
                return result;
            }

            _line.Append(character);
        }
    }

    private bool _readCompletionSignaled;

    internal void SignalReadCompleted()
    {
        if (_readCompletionSignaled)
        {
            return;
        }

        _readCompletionSignaled = true;
        onReadCompleted?.Invoke();
    }

    private string? TakeFinalLine()
    {
        if (_line.Length == 0)
        {
            return null;
        }

        var result = _line.ToString();
        _line.Clear();
        return result;
    }
}
