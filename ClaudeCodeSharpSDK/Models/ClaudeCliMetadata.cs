namespace ManagedCode.ClaudeCodeSharpSDK.Models;

public sealed record ClaudeCliMetadata(
    string InstalledVersion,
    string? DefaultModel,
    IReadOnlyList<ClaudeModelMetadata> Models);

public sealed record ClaudeCliUpdateStatus(
    string InstalledVersion,
    string? LatestVersion,
    bool IsUpdateAvailable,
    string? UpdateMessage,
    string? UpdateCommand);

public sealed record ClaudeModelMetadata(
    string Slug,
    string DisplayName,
    string? Description,
    bool IsListed);
