namespace ManagedCode.ClaudeCodeSharpSDK.Models;

public static class ClaudeModels
{
    public const string Sonnet = "sonnet";
    public const string Opus = "opus";
    public const string Haiku = "haiku";

    public const string ClaudeSonnet45 = "claude-sonnet-4-5-20250929";
    public const string ClaudeOpus45 = "claude-opus-4-5";
    public const string ClaudeOpus41 = "claude-opus-4-1";
    public const string ClaudeSonnet40 = "claude-sonnet-4";

    public static IReadOnlyList<ClaudeModelMetadata> Known { get; } =
    [
        new(Sonnet, "sonnet", "Alias for the latest Sonnet model.", true),
        new(Opus, "opus", "Alias for the latest Opus model.", true),
        new(Haiku, "haiku", "Alias for the latest Haiku model.", true),
        new(ClaudeSonnet45, "Claude Sonnet 4.5", null, true),
        new(ClaudeOpus45, "Claude Opus 4.5", null, true),
        new(ClaudeOpus41, "Claude Opus 4.1", null, true),
        new(ClaudeSonnet40, "Claude Sonnet 4", null, true),
    ];
}
