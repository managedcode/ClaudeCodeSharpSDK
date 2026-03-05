namespace ManagedCode.ClaudeCodeSharpSDK.Internal;

internal static class ClaudeProtocolConstants
{
    internal static class Properties
    {
        internal const string Agents = "agents";
        internal const string ApiKeySource = "apiKeySource";
        internal const string CacheCreationInputTokens = "cache_creation_input_tokens";
        internal const string CacheReadInputTokens = "cache_read_input_tokens";
        internal const string ClaudeCodeVersion = "claude_code_version";
        internal const string Content = "content";
        internal const string Cwd = "cwd";
        internal const string DurationApiMs = "duration_api_ms";
        internal const string DurationMs = "duration_ms";
        internal const string Error = "error";
        internal const string Id = "id";
        internal const string Input = "input";
        internal const string InputTokens = "input_tokens";
        internal const string IsError = "is_error";
        internal const string McpServers = "mcp_servers";
        internal const string Message = "message";
        internal const string Model = "model";
        internal const string Name = "name";
        internal const string NumTurns = "num_turns";
        internal const string OutputStyle = "output_style";
        internal const string OutputTokens = "output_tokens";
        internal const string ParentToolUseId = "parent_tool_use_id";
        internal const string PermissionMode = "permissionMode";
        internal const string Plugins = "plugins";
        internal const string Result = "result";
        internal const string Role = "role";
        internal const string SessionId = "session_id";
        internal const string Skills = "skills";
        internal const string SlashCommands = "slash_commands";
        internal const string StopReason = "stop_reason";
        internal const string Subtype = "subtype";
        internal const string Text = "text";
        internal const string ToolUseId = "tool_use_id";
        internal const string Tools = "tools";
        internal const string TotalCostUsd = "total_cost_usd";
        internal const string Type = "type";
        internal const string Usage = "usage";
        internal const string Uuid = "uuid";
    }

    internal static class EventTypes
    {
        internal const string Assistant = "assistant";
        internal const string Error = "error";
        internal const string ItemCompleted = "item.completed";
        internal const string ItemStarted = "item.started";
        internal const string ItemUpdated = "item.updated";
        internal const string Result = "result";
        internal const string System = "system";
        internal const string TurnStarted = "turn.started";
        internal const string User = "user";
    }

    internal static class Subtypes
    {
        internal const string Init = "init";
    }

    internal static class ContentTypes
    {
        internal const string Text = "text";
        internal const string ToolUse = "tool_use";
        internal const string ToolResult = "tool_result";
    }
}
