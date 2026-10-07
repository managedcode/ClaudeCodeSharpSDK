using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace ManagedCode.ClaudeCodeSharpSDK.Configuration;

public sealed record ClaudeOptions
{
    public static readonly TimeSpan DefaultProcessTerminationTimeout = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan DefaultCliMetadataProbeTimeout = TimeSpan.FromSeconds(10);

    public const int DefaultCliMetadataMaximumOutputCharacters = 65536;

    public string? ClaudeExecutablePath { get; init; }

    public string? BaseUrl { get; init; }

    public string? ApiKey { get; init; }

    public JsonObject? Settings { get; init; }

    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }

    public bool InheritEnvironmentVariables { get; init; } = true;

    public TimeSpan ProcessTerminationTimeout { get; init; } = DefaultProcessTerminationTimeout;

    public TimeSpan CliMetadataProbeTimeout { get; init; } = DefaultCliMetadataProbeTimeout;

    public int CliMetadataMaximumOutputCharacters { get; init; } = DefaultCliMetadataMaximumOutputCharacters;

    public ILogger? Logger { get; init; }
}
