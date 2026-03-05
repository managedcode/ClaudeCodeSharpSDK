using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace ManagedCode.ClaudeCodeSharpSDK.Configuration;

public sealed record ClaudeOptions
{
    public string? ClaudeExecutablePath { get; init; }

    public string? BaseUrl { get; init; }

    public string? ApiKey { get; init; }

    public JsonObject? Settings { get; init; }

    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }

    public ILogger? Logger { get; init; }
}
