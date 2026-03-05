using Microsoft.Extensions.Logging;

namespace ManagedCode.ClaudeCodeSharpSDK.Logging;

internal static partial class ClaudeExecLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Starting Claude Code CLI '{ExecutablePath}' with {ArgumentCount} arguments.")]
    public static partial void Starting(ILogger logger, string executablePath, int argumentCount);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Claude Code CLI execution was cancelled.")]
    public static partial void Cancelled(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = "Claude Code CLI execution failed.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Information,
        Message = "Claude Code CLI finished successfully with {LineCount} output lines.")]
    public static partial void Completed(ILogger logger, int lineCount);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Warning,
        Message = "Failed to terminate Claude Code CLI process '{ExecutablePath}' during cleanup.")]
    public static partial void ProcessKillFailed(ILogger logger, string executablePath, Exception exception);
}
