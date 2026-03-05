using ManagedCode.ClaudeCodeSharpSDK.Configuration;

namespace ManagedCode.ClaudeCodeSharpSDK.Client;

public sealed record ClaudeClientOptions
{
    public ClaudeOptions? ClaudeOptions { get; init; }

    public bool AutoStart { get; init; } = true;
}
