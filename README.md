# ManagedCode.ClaudeCodeSharpSDK

[![CI](https://github.com/managedcode/ClaudeCodeSharpSDK/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/managedcode/ClaudeCodeSharpSDK/actions/workflows/ci.yml)
[![Release](https://github.com/managedcode/ClaudeCodeSharpSDK/actions/workflows/release.yml/badge.svg?branch=main)](https://github.com/managedcode/ClaudeCodeSharpSDK/actions/workflows/release.yml)
[![CodeQL](https://github.com/managedcode/ClaudeCodeSharpSDK/actions/workflows/codeql.yml/badge.svg?branch=main)](https://github.com/managedcode/ClaudeCodeSharpSDK/actions/workflows/codeql.yml)
[![NuGet](https://img.shields.io/nuget/v/ManagedCode.ClaudeCodeSharpSDK.svg)](https://www.nuget.org/packages/ManagedCode.ClaudeCodeSharpSDK)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

`ManagedCode.ClaudeCodeSharpSDK` is a .NET SDK for driving the Claude Code CLI from C#.

It is intentionally CLI-first. The library does not reimplement Anthropic APIs or invent its own transport. It wraps the local `claude` binary, runs Claude Code in print mode, and maps the emitted protocol into typed C# models.

## What You Get

- `ClaudeClient` / `ClaudeThread` API for start, resume, run, and stream workflows
- real Claude Code print-mode transport via `claude -p`
- typed parsing for `stream-json` events
- structured output with `StructuredOutputSchema`
- optional `Microsoft.Extensions.AI` adapter package
- repository automation that tracks upstream changes in `anthropics/claude-code`

## Source Of Truth

This SDK follows the real Claude Code CLI contract in print mode:

- `claude -p --output-format json`
- `claude -p --output-format stream-json --verbose`
- upstream reference repository: [anthropics/claude-code](https://github.com/anthropics/claude-code)

If the docs in this repository and the observed CLI behavior ever diverge, the observed CLI behavior wins and the SDK should be updated to match.

Upstream tracking is built into the repo:

- reference submodule: [`submodules/anthropic-claude-code`](submodules/anthropic-claude-code)
- sync workflow: [`.github/workflows/claude-cli-watch.yml`](.github/workflows/claude-cli-watch.yml)

## Packages

Core SDK:

```bash
dotnet add package ManagedCode.ClaudeCodeSharpSDK
```

Optional `Microsoft.Extensions.AI` adapter:

```bash
dotnet add package ManagedCode.ClaudeCodeSharpSDK.Extensions.AI
```

## Prerequisites

Before using the SDK, you need:

- `claude` installed and available in `PATH`, or configured via `ClaudeOptions.ClaudeExecutablePath`
- an authenticated local Claude Code session for real runs; `claude auth status` must not fail with `401` or `Please run /login`

Quick sanity check:

```bash
claude --version
claude --help
claude auth status
```

## Quickstart

```csharp
using ManagedCode.ClaudeCodeSharpSDK.Client;

using var client = new ClaudeClient();
using var thread = client.StartThread();

var result = await thread.RunAsync("Diagnose failing tests and propose a fix.");

Console.WriteLine(result.FinalResponse);
Console.WriteLine($"Items: {result.Items.Count}");
```

`ClaudeClient` auto-starts by default. If you want explicit lifecycle control, create it with `AutoStart = false` and call `StartAsync()` yourself.

## Core Concepts

### Client

`ClaudeClient` owns executable discovery, lifecycle, and metadata queries:

- `StartThread()`
- `ResumeThread(sessionId)`
- `GetCliMetadata()`
- `GetCliUpdateStatus()`

### Thread

`ClaudeThread` represents one Claude Code conversation/session:

- turns are serialized per thread instance
- `RunAsync(...)` returns the final response plus collected items
- `RunStreamedAsync(...)` exposes the parsed event stream

### Transport

At runtime the SDK executes Claude Code in print mode and parses `stream-json` output. It does not maintain a separate protocol implementation.

## Streaming

```csharp
using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Models;

using var client = new ClaudeClient();
using var thread = client.StartThread();

var streamed = await thread.RunStreamedAsync("Implement the fix.");

await foreach (var evt in streamed.Events)
{
    switch (evt)
    {
        case ItemCompletedEvent completed:
            Console.WriteLine($"Item: {completed.Item.Type}");
            break;

        case TurnCompletedEvent done:
            Console.WriteLine(done.Result);
            Console.WriteLine($"Output tokens: {done.Usage.OutputTokens}");
            break;
    }
}
```

## Structured Output

```csharp
using System.Text.Json.Serialization;
using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Models;

public sealed record RepositorySummary(string Summary, string Status);

[JsonSerializable(typeof(RepositorySummary))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

var schema = StructuredOutputSchema.Map<RepositorySummary>(
    additionalProperties: false,
    (response => response.Summary, StructuredOutputSchema.PlainText()),
    (response => response.Status, StructuredOutputSchema.PlainText()));

using var client = new ClaudeClient();
using var thread = client.StartThread();

var result = await thread.RunAsync(
    "Summarize the repository status as JSON.",
    schema,
    AppJsonContext.Default.RepositorySummary);

Console.WriteLine(result.TypedResponse.Status);
Console.WriteLine(result.TypedResponse.Summary);
```

If you want full turn control, use `TurnOptions`:

```csharp
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

var result = await thread.RunAsync(
    "Summarize the repository status as JSON.",
    AppJsonContext.Default.RepositorySummary,
    new TurnOptions
    {
        OutputSchema = schema,
        CancellationToken = timeout.Token,
    });
```

Notes:

- typed runs require `TurnOptions.OutputSchema` or a direct `outputSchema` overload
- the `JsonTypeInfo<T>` overloads are the AOT-safe path
- reflection-based typed overloads are intentionally marked as AOT-unsafe

## Resume An Existing Session

```csharp
using ManagedCode.ClaudeCodeSharpSDK.Client;

using var client = new ClaudeClient();
using var thread = client.ResumeThread("existing-session-id");

var result = await thread.RunAsync("Continue from the previous plan.");
Console.WriteLine(result.FinalResponse);
```

## Thread Options

`ThreadOptions` maps the Claude Code flags currently supported by this SDK.

```csharp
using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Models;

var options = new ThreadOptions
{
    Model = ClaudeModels.ClaudeOpus45,
    PermissionMode = PermissionMode.AcceptEdits,
    AllowedTools = ["Read", "Write", "Edit"],
    DisallowedTools = ["Bash"],
    AdditionalDirectories = ["/workspace", "/tmp"],
    SystemPrompt = "Be concise and explicit about risk.",
    AppendSystemPrompt = "Prefer concrete file paths in explanations.",
    MaxBudgetUsd = 0.50m,
    NoSessionPersistence = true,
    AdditionalCliArguments = ["--some-future-flag", "custom-value"],
};

using var thread = client.StartThread(options);
```

Not every flag in the upstream CLI is necessarily surfaced yet, but unsupported future non-transport flags can still be passed through `AdditionalCliArguments`. SDK-managed transport flags such as `--print`, `--output-format`, `--input-format`, and `--json-schema` are reserved and rejected if passed through manually.

## Metadata And Update Checks

```csharp
using System.Linq;
using ManagedCode.ClaudeCodeSharpSDK.Client;

using var client = new ClaudeClient();

var metadata = client.GetCliMetadata();
Console.WriteLine($"Installed Claude Code: {metadata.InstalledVersion}");
Console.WriteLine($"Default model: {metadata.DefaultModel}");

foreach (var model in metadata.Models.Where(model => model.IsListed))
{
    Console.WriteLine(model.Slug);
}

var update = client.GetCliUpdateStatus();
if (update.IsUpdateAvailable)
{
    Console.WriteLine(update.UpdateMessage);
    Console.WriteLine(update.UpdateCommand);
}
```

Current metadata support includes:

- installed CLI version from `claude --version`
- default model discovery from Claude settings files with SDK fallback to `sonnet`
- SDK-known model aliases/constants
- upstream tag comparison against `anthropics/claude-code`

## Logging

```csharp
using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Configuration;
using Microsoft.Extensions.Logging;

public sealed class ConsoleClaudeLogger : ILogger
{
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Console.WriteLine($"[{logLevel}] {formatter(state, exception)}");
        if (exception is not null)
        {
            Console.WriteLine(exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}

using var client = new ClaudeClient(new ClaudeOptions
{
    Logger = new ConsoleClaudeLogger(),
});
```

## Current Limitations

- the current SDK transport is print-mode only
- `LocalImageInput` exists in the model layer but is rejected in the current Claude print-mode implementation
- `TurnOptions.ReplayUserMessages` is reserved for future stream-json input support and currently throws
- the `Microsoft.Extensions.AI` adapter is text-first and does not expose custom Claude internal item types
- `ChatOptions.Tools` is ignored because Claude Code manages tools internally
- authenticated end-to-end behavior depends on the local Claude Code session available on the machine

## Microsoft.Extensions.AI Adapter

```csharp
using Microsoft.Extensions.AI;
using ManagedCode.ClaudeCodeSharpSDK.Extensions.AI;

IChatClient client = new ClaudeChatClient();

var response = await client.GetResponseAsync(
[
    new ChatMessage(ChatRole.User, "Summarize the repository."),
]);

Console.WriteLine(response.Text);
```

Streaming:

```csharp
await foreach (var update in client.GetStreamingResponseAsync(
[
    new ChatMessage(ChatRole.User, "Implement the fix."),
]))
{
    Console.Write(update.Text);
}
```

Claude-specific options flow through `ChatOptions.AdditionalProperties`:

```csharp
using ManagedCode.ClaudeCodeSharpSDK.Client;
using Microsoft.Extensions.AI;

var options = new ChatOptions
{
    AdditionalProperties = new AdditionalPropertiesDictionary
    {
        ["claude:working_directory"] = "/workspace",
        ["claude:permission_mode"] = PermissionMode.AcceptEdits,
        ["claude:allowed_tools"] = new[] { "Read", "Write" },
        ["claude:max_budget_usd"] = 0.25m,
    },
};
```

See:

- [docs/Features/meai-integration.md](docs/Features/meai-integration.md)
- [docs/ADR/003-microsoft-extensions-ai-integration.md](docs/ADR/003-microsoft-extensions-ai-integration.md)

## Development

Clone with submodules:

```bash
git clone https://github.com/managedcode/ClaudeCodeSharpSDK.git
cd ClaudeCodeSharpSDK
git submodule update --init --recursive
```

Build and test:

```bash
dotnet build ManagedCode.ClaudeCodeSharpSDK.slnx -c Release -warnaserror
dotnet test --solution ManagedCode.ClaudeCodeSharpSDK.slnx -c Release
```

Smoke-only subset:

```bash
dotnet test --project ClaudeCodeSharpSDK.Tests/ClaudeCodeSharpSDK.Tests.csproj -c Release --no-build -- --treenode-filter "/*/*/*/ClaudeCli_Smoke_*"
```

## Documentation Map

- architecture: [docs/Architecture/Overview.md](docs/Architecture/Overview.md)
- thread execution: [docs/Features/thread-run-flow.md](docs/Features/thread-run-flow.md)
- CLI metadata: [docs/Features/cli-metadata.md](docs/Features/cli-metadata.md)
- M.E.AI adapter: [docs/Features/meai-integration.md](docs/Features/meai-integration.md)
- automation and upstream sync: [docs/Features/release-and-sync-automation.md](docs/Features/release-and-sync-automation.md)
- ADRs: [docs/ADR](docs/ADR)
