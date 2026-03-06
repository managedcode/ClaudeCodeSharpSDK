namespace ManagedCode.ClaudeCodeSharpSDK.Models;

public static class ClaudeModels
{
    private const string LatestSonnetDescription = "Alias for the latest Sonnet model.";
    private const string LatestOpusDescription = "Alias for the latest Opus model.";
    private const string LatestHaikuDescription = "Alias for the latest Haiku model.";
    private const string ClaudeHaiku45DisplayName = "Claude Haiku 4.5";
    private const string ClaudeHaiku45SnapshotDisplayName = "Claude Haiku 4.5 (2025-10-01)";
    private const string ClaudeHaiku35DisplayName = "Claude Haiku 3.5";
    private const string ClaudeHaiku35SnapshotDisplayName = "Claude Haiku 3.5 (2024-10-22)";
    private const string ClaudeHaiku3DisplayName = "Claude Haiku 3";
    private const string ClaudeOpus45AliasDisplayName = "Claude Opus 4.5";
    private const string ClaudeOpus45SnapshotDisplayName = "Claude Opus 4.5 (2025-11-01)";
    private const string ClaudeOpus41AliasDisplayName = "Claude Opus 4.1";
    private const string ClaudeOpus41SnapshotDisplayName = "Claude Opus 4.1 (2025-08-05)";
    private const string ClaudeOpus40DisplayName = "Claude Opus 4";
    private const string ClaudeSonnet45DisplayName = "Claude Sonnet 4.5";
    private const string ClaudeSonnet45SnapshotDisplayName = "Claude Sonnet 4.5 (2025-09-29)";
    private const string ClaudeSonnet40DisplayName = "Claude Sonnet 4";
    private const string ClaudeSonnet37AliasDisplayName = "Claude Sonnet 3.7";
    private const string ClaudeSonnet37SnapshotDisplayName = "Claude Sonnet 3.7 (2025-02-19)";
    private const string ClaudeSonnet35AliasDisplayName = "Claude Sonnet 3.5";
    private const string ClaudeSonnet35SnapshotDisplayName = "Claude Sonnet 3.5 (2024-10-22)";
    private const string ClaudeSonnet35JuneDisplayName = "Claude Sonnet 3.5 (2024-06-20)";
    private const string ClaudeSonnet3DisplayName = "Claude Sonnet 3";
    private const string ClaudeOpus3DisplayName = "Claude Opus 3";

    public const string Sonnet = "sonnet";
    public const string Opus = "opus";
    public const string Haiku = "haiku";

    public const string ClaudeSonnet45Alias = "claude-sonnet-4-5";
    public const string ClaudeSonnet45 = "claude-sonnet-4-5-20250929";
    public const string ClaudeHaiku45Alias = "claude-haiku-4-5";
    public const string ClaudeHaiku45 = "claude-haiku-4-5-20251001";
    public const string ClaudeOpus45Alias = "claude-opus-4-5";
    public const string ClaudeOpus45 = "claude-opus-4-5-20251101";
    public const string ClaudeOpus41Alias = "claude-opus-4-1";
    public const string ClaudeOpus41 = "claude-opus-4-1-20250805";
    public const string ClaudeOpus40 = "claude-opus-4-20250514";
    public const string ClaudeSonnet40 = "claude-sonnet-4-20250514";
    public const string ClaudeSonnet37Alias = "claude-3-7-sonnet-latest";
    public const string ClaudeSonnet37 = "claude-3-7-sonnet-20250219";
    public const string ClaudeSonnet35Alias = "claude-3-5-sonnet-latest";
    public const string ClaudeSonnet35 = "claude-3-5-sonnet-20241022";
    public const string ClaudeSonnet35June = "claude-3-5-sonnet-20240620";
    public const string ClaudeHaiku35Alias = "claude-3-5-haiku-latest";
    public const string ClaudeHaiku35 = "claude-3-5-haiku-20241022";
    public const string ClaudeOpus3 = "claude-3-opus-20240229";
    public const string ClaudeSonnet3 = "claude-3-sonnet-20240229";
    public const string ClaudeHaiku3 = "claude-3-haiku-20240307";

    public static IReadOnlyList<ClaudeModelMetadata> Known { get; } =
    [
        new(Sonnet, Sonnet, LatestSonnetDescription, true),
        new(Opus, Opus, LatestOpusDescription, true),
        new(Haiku, Haiku, LatestHaikuDescription, true),
        new(ClaudeSonnet45Alias, ClaudeSonnet45DisplayName, null, true),
        new(ClaudeSonnet45, ClaudeSonnet45SnapshotDisplayName, null, true),
        new(ClaudeHaiku45Alias, ClaudeHaiku45DisplayName, null, true),
        new(ClaudeHaiku45, ClaudeHaiku45SnapshotDisplayName, null, true),
        new(ClaudeOpus45Alias, ClaudeOpus45AliasDisplayName, null, true),
        new(ClaudeOpus45, ClaudeOpus45SnapshotDisplayName, null, true),
        new(ClaudeOpus41Alias, ClaudeOpus41AliasDisplayName, null, true),
        new(ClaudeOpus41, ClaudeOpus41SnapshotDisplayName, null, true),
        new(ClaudeOpus40, ClaudeOpus40DisplayName, null, true),
        new(ClaudeSonnet40, ClaudeSonnet40DisplayName, null, true),
        new(ClaudeSonnet37Alias, ClaudeSonnet37AliasDisplayName, null, true),
        new(ClaudeSonnet37, ClaudeSonnet37SnapshotDisplayName, null, true),
        new(ClaudeSonnet35Alias, ClaudeSonnet35AliasDisplayName, null, true),
        new(ClaudeSonnet35, ClaudeSonnet35SnapshotDisplayName, null, true),
        new(ClaudeSonnet35June, ClaudeSonnet35JuneDisplayName, null, true),
        new(ClaudeHaiku35Alias, ClaudeHaiku35DisplayName, null, true),
        new(ClaudeHaiku35, ClaudeHaiku35SnapshotDisplayName, null, true),
        new(ClaudeOpus3, ClaudeOpus3DisplayName, null, true),
        new(ClaudeSonnet3, ClaudeSonnet3DisplayName, null, true),
        new(ClaudeHaiku3, ClaudeHaiku3DisplayName, null, true),
    ];
}
