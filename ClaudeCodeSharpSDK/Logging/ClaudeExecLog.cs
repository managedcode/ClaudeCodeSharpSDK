using Microsoft.Extensions.Logging;

namespace ManagedCode.ClaudeCodeSharpSDK.Logging;

internal static partial class ClaudeExecLog
{
    private const string StartingMessage = "Starting Claude Code CLI '{ExecutablePath}' with {ArgumentCount} arguments.";
    private const string CancelledMessage = "Claude Code CLI execution was cancelled.";
    private const string FailedMessage = "Claude Code CLI execution failed.";
    private const string CompletedMessage = "Claude Code CLI finished successfully with {LineCount} output lines.";
    private const string ProcessKillFailedMessage = "Failed to terminate Claude Code CLI process '{ExecutablePath}' during cleanup.";

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = StartingMessage)]
    public static partial void Starting(ILogger logger, string executablePath, int argumentCount);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = CancelledMessage)]
    public static partial void Cancelled(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = FailedMessage)]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Information,
        Message = CompletedMessage)]
    public static partial void Completed(ILogger logger, int lineCount);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Warning,
        Message = ProcessKillFailedMessage)]
    public static partial void ProcessKillFailed(ILogger logger, string executablePath, Exception exception);
}
