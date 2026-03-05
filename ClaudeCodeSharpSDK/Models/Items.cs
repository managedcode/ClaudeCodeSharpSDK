using System.Text.Json.Nodes;
using ManagedCode.ClaudeCodeSharpSDK.Internal;

namespace ManagedCode.ClaudeCodeSharpSDK.Models;

public sealed record MessageContentBlock(
    string Type,
    string? Text,
    string? Id,
    string? Name,
    string? ToolUseId,
    bool? IsError,
    JsonNode? Input,
    JsonNode? Raw);

public abstract record ThreadItem(string Id, string Type);

public sealed record AssistantMessageItem(
    string Id,
    string Model,
    string Text,
    IReadOnlyList<MessageContentBlock> Content,
    Usage? Usage,
    string? StopReason,
    string? Error)
    : ThreadItem(Id, ClaudeProtocolConstants.EventTypes.Assistant);

public sealed record UserMessageItem(
    string Id,
    string Text,
    IReadOnlyList<MessageContentBlock> Content)
    : ThreadItem(Id, ClaudeProtocolConstants.EventTypes.User);
