using System.Text.Json.Nodes;
using ManagedCode.ClaudeCodeSharpSDK.Internal;

namespace ManagedCode.ClaudeCodeSharpSDK.Models;

public sealed record Usage(
    int InputTokens,
    int CacheCreationInputTokens,
    int CacheReadInputTokens,
    int OutputTokens)
{
    public int CachedInputTokens => CacheCreationInputTokens + CacheReadInputTokens;
}

public sealed record ThreadError(string Message);

public sealed record SessionInfo(
    string SessionId,
    string WorkingDirectory,
    string? Model,
    string? PermissionMode,
    string? ClaudeCodeVersion,
    string? OutputStyle,
    string? ApiKeySource,
    IReadOnlyList<string> Tools,
    IReadOnlyList<string> McpServers,
    IReadOnlyList<string> SlashCommands,
    IReadOnlyList<string> Agents,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> Plugins);

public abstract record ThreadEvent(string Type);

public sealed record ThreadStartedEvent(SessionInfo Session)
    : ThreadEvent(ClaudeProtocolConstants.EventTypes.System)
{
    public string ThreadId => Session.SessionId;
}

public sealed record TurnStartedEvent()
    : ThreadEvent(ClaudeProtocolConstants.EventTypes.TurnStarted);

public sealed record TurnCompletedEvent(
    Usage Usage,
    string Result,
    double? TotalCostUsd,
    int? DurationMs,
    int? DurationApiMs,
    int? NumTurns)
    : ThreadEvent(ClaudeProtocolConstants.EventTypes.Result);

public sealed record TurnFailedEvent(
    ThreadError Error,
    Usage? Usage = null,
    double? TotalCostUsd = null,
    int? DurationMs = null,
    int? DurationApiMs = null,
    int? NumTurns = null)
    : ThreadEvent(ClaudeProtocolConstants.EventTypes.Result);

public sealed record ItemStartedEvent(ThreadItem Item)
    : ThreadEvent(ClaudeProtocolConstants.EventTypes.ItemStarted);

public sealed record ItemUpdatedEvent(ThreadItem Item)
    : ThreadEvent(ClaudeProtocolConstants.EventTypes.ItemUpdated);

public sealed record ItemCompletedEvent(ThreadItem Item)
    : ThreadEvent(ClaudeProtocolConstants.EventTypes.ItemCompleted);

public sealed record ThreadErrorEvent(string Message)
    : ThreadEvent(ClaudeProtocolConstants.EventTypes.Error);

public sealed record UnknownEvent(string RawType, JsonNode Payload)
    : ThreadEvent(RawType);
