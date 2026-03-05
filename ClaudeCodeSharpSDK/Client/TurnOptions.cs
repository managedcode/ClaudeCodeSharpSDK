using ManagedCode.ClaudeCodeSharpSDK.Models;

namespace ManagedCode.ClaudeCodeSharpSDK.Client;

public sealed record TurnOptions
{
    public StructuredOutputSchema? OutputSchema { get; init; }

    public bool IncludePartialMessages { get; init; }

    public bool ReplayUserMessages { get; init; }

    public decimal? MaxBudgetUsd { get; init; }

    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;
}
