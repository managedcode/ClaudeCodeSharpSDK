using System.Text.Json.Nodes;
using ManagedCode.ClaudeCodeSharpSDK.Client;

namespace ManagedCode.ClaudeCodeSharpSDK.Execution;

public sealed record ClaudeExecArgs
{
    public required string Input { get; init; }

    public string? BaseUrl { get; init; }

    public string? ApiKey { get; init; }

    public string? Model { get; init; }

    public string? Agent { get; init; }

    public string? FallbackModel { get; init; }

    public string? WorkingDirectory { get; init; }

    public PermissionMode? PermissionMode { get; init; }

    public bool DangerouslySkipPermissions { get; init; }

    public bool AllowDangerouslySkipPermissions { get; init; }

    public IReadOnlyList<string>? AllowedTools { get; init; }

    public IReadOnlyList<string>? DisallowedTools { get; init; }

    public IReadOnlyList<string>? Tools { get; init; }

    public IReadOnlyList<string>? AdditionalDirectories { get; init; }

    public IReadOnlyList<string>? McpConfigs { get; init; }

    public bool StrictMcpConfig { get; init; }

    public string? SystemPrompt { get; init; }

    public string? AppendSystemPrompt { get; init; }

    public bool ContinueMostRecent { get; init; }

    public string? ResumeSessionId { get; init; }

    public string? SessionId { get; init; }

    public bool ForkSession { get; init; }

    public bool NoSessionPersistence { get; init; }

    public decimal? MaxBudgetUsd { get; init; }

    public JsonObject? Settings { get; init; }

    public IReadOnlyList<SettingSource>? SettingSources { get; init; }

    public IReadOnlyList<string>? PluginDirectories { get; init; }

    public bool DisableSlashCommands { get; init; }

    public bool? Ide { get; init; }

    public bool? Chrome { get; init; }

    public IReadOnlyList<string>? Betas { get; init; }

    public IReadOnlyDictionary<string, InlineAgentDefinition>? InlineAgents { get; init; }

    public string? JsonSchema { get; init; }

    public bool IncludePartialMessages { get; init; }

    public bool ReplayUserMessages { get; init; }

    public string? SessionName { get; init; }

    public IReadOnlyList<string>? AdditionalCliArguments { get; init; }

    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;
}
