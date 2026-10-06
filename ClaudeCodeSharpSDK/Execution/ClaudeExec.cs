using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Configuration;
using ManagedCode.ClaudeCodeSharpSDK.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Logging;
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

    private readonly string _executablePath;
    private readonly IReadOnlyDictionary<string, string>? _environmentOverride;
    private readonly bool _inheritEnvironmentVariables;
    private readonly TimeSpan _processTerminationTimeout;
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
        TimeSpan? processTerminationTimeout = null)
    {
        var resolvedTerminationTimeout = processTerminationTimeout ?? ClaudeOptions.DefaultProcessTerminationTimeout;
        if (resolvedTerminationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(processTerminationTimeout), resolvedTerminationTimeout, ProcessTerminationTimeoutMustBePositiveMessage);
        }

        _executablePath = ClaudeCliLocator.FindClaudePath(executablePath);
        _environmentOverride = environmentOverride;
        _inheritEnvironmentVariables = inheritEnvironmentVariables;
        _processTerminationTimeout = resolvedTerminationTimeout;
        _baseSettings = baseSettings;
        _processRunner = processRunner ?? new DefaultClaudeProcessRunner();
        _logger = logger ?? NullLogger.Instance;
    }

    public IAsyncEnumerable<string> RunAsync(ClaudeExecArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var commandArgs = BuildCommandArgs(args);
        var environment = BuildEnvironment(args.BaseUrl, args.ApiKey);
        var workingDirectory = string.IsNullOrWhiteSpace(args.WorkingDirectory)
            ? Environment.CurrentDirectory
            : args.WorkingDirectory;
        var invocation = new ClaudeProcessInvocation(
            _executablePath,
            workingDirectory,
            commandArgs,
            environment,
            args.Input,
            _processTerminationTimeout);

        return RunWithDiagnosticsAsync(invocation, args.CancellationToken);
    }

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
        ClaudeProcessInvocation invocation,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ClaudeExecLog.Starting(_logger, invocation.ExecutablePath, invocation.Arguments.Count);

        if (cancellationToken.IsCancellationRequested)
        {
            ClaudeExecLog.Cancelled(_logger);
            cancellationToken.ThrowIfCancellationRequested();
        }

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
    TimeSpan ProcessTerminationTimeout);

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
    private const string Space = " ";
    private const string MessageQuote = "'";
    private const string MessageSuffix = ".";
    private const string PeriodSpace = ". ";

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
        Task<string>? standardErrorTask = null;
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
            standardErrorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
            await process.StandardInput.WriteAsync(invocation.Input.AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();

            string? line;
            while (true)
            {
                try
                {
                    line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await ThrowIfExitedWithFailureAsync(process, standardErrorTask, invocation.ProcessTerminationTimeout).ConfigureAwait(false);
                    throw;
                }

                if (line is null)
                {
                    break;
                }

                if (line.Length == 0)
                {
                    continue;
                }

                yield return line;
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

            var standardError = await ReadStandardErrorAsync(standardErrorTask, invocation.ProcessTerminationTimeout).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                var details = string.IsNullOrWhiteSpace(standardError)
                    ? ProcessFailedWithoutStderrMessage
                    : standardError.Trim();
                throw new InvalidOperationException(
                    string.Concat(CliExitedWithCodeMessagePrefix, Space, process.ExitCode.ToString(CultureInfo.InvariantCulture), PeriodSpace, details));
            }
        }
        finally
        {
            await EnsureProcessStoppedAsync(process, logger, invocation.ExecutablePath, invocation.ProcessTerminationTimeout).ConfigureAwait(false);
            if (standardErrorTask is not null)
            {
                await ReadStandardErrorAsync(standardErrorTask, invocation.ProcessTerminationTimeout).ConfigureAwait(false);
            }
        }
    }

    private static async Task ThrowIfExitedWithFailureAsync(
        Process process,
        Task<string> standardErrorTask,
        TimeSpan processTerminationTimeout)
    {
        if (!process.HasExited || process.ExitCode == 0)
        {
            return;
        }

        var standardError = await ReadStandardErrorAsync(standardErrorTask, processTerminationTimeout).ConfigureAwait(false);
        var details = string.IsNullOrWhiteSpace(standardError)
            ? ProcessFailedWithoutStderrMessage
            : standardError.Trim();
        throw new InvalidOperationException(
            string.Concat(CliExitedWithCodeMessagePrefix, Space, process.ExitCode.ToString(CultureInfo.InvariantCulture), PeriodSpace, details));
    }

    private static async Task<string> ReadStandardErrorAsync(Task<string> standardErrorTask, TimeSpan timeout)
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
