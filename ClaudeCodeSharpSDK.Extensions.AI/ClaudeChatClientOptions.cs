using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Configuration;

namespace ManagedCode.ClaudeCodeSharpSDK.Extensions.AI;

public sealed record ClaudeChatClientOptions
{
    public ClaudeOptions? ClaudeOptions { get; set; }

    public string? DefaultModel { get; set; }

    public ThreadOptions? DefaultThreadOptions { get; set; }
}
