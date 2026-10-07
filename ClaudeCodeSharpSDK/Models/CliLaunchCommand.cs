using System.Collections.Immutable;

namespace ManagedCode.ClaudeCodeSharpSDK.Models;

/// <summary>Describes the trusted executable and literal prefix arguments used to start Claude Code.</summary>
public sealed record CliLaunchCommand(string ExecutablePath, ImmutableArray<string> PrefixArguments);
