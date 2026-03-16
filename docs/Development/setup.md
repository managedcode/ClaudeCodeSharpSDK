# Development Setup

## Prerequisites

- .NET SDK `10.0.103` (see `global.json`)
- Claude Code CLI available locally (`claude` in PATH) for real runtime usage
- Git with submodule support

## Windows Claude process lookup

- Runtime lookup order:
  - vendored CLI binary under `node_modules/*/vendor/<target>/claude/claude.exe`
  - PATH candidates in order: `claude.exe`, `claude.cmd`, `claude.bat`, `claude`
- This allows local npm installs and PATH-based installs to work on Windows.

## Bootstrap

```bash
git submodule update --init --recursive
dotnet restore ManagedCode.ClaudeCodeSharpSDK.slnx
```

## Solution projects

- `ClaudeCodeSharpSDK/ClaudeCodeSharpSDK.csproj` — core `ManagedCode.ClaudeCodeSharpSDK` package.
- `ClaudeCodeSharpSDK.Extensions.AI/ClaudeCodeSharpSDK.Extensions.AI.csproj` — optional `IChatClient` adapter package (`ManagedCode.ClaudeCodeSharpSDK.Extensions.AI`).
- `ClaudeCodeSharpSDK.Extensions.AgentFramework/ClaudeCodeSharpSDK.Extensions.AgentFramework.csproj` — optional Microsoft Agent Framework adapter package (`ManagedCode.ClaudeCodeSharpSDK.Extensions.AgentFramework`).
- `ClaudeCodeSharpSDK.Tests/ClaudeCodeSharpSDK.Tests.csproj` — consolidated TUnit coverage for core SDK, CLI integration, `Microsoft.Extensions.AI`, and Microsoft Agent Framework adapter behavior.

## Local validation

```bash
dotnet build ManagedCode.ClaudeCodeSharpSDK.slnx -c Release -warnaserror
dotnet test --solution ManagedCode.ClaudeCodeSharpSDK.slnx -c Release
dotnet format ManagedCode.ClaudeCodeSharpSDK.slnx
```

Focused run (TUnit/MTP):

```bash
dotnet test --project ClaudeCodeSharpSDK.Tests/ClaudeCodeSharpSDK.Tests.csproj -c Release -- --treenode-filter "/*/*/ThreadEventParserTests/*"
```

## Packaging check

```bash
dotnet pack ClaudeCodeSharpSDK/ClaudeCodeSharpSDK.csproj -c Release --no-build -o artifacts
dotnet pack ClaudeCodeSharpSDK.Extensions.AI/ClaudeCodeSharpSDK.Extensions.AI.csproj -c Release --no-build -o artifacts
dotnet pack ClaudeCodeSharpSDK.Extensions.AgentFramework/ClaudeCodeSharpSDK.Extensions.AgentFramework.csproj -c Release --no-build -o artifacts
```

## CI/workflows

- CI: `.github/workflows/ci.yml`
- Release: `.github/workflows/release.yml`
- CodeQL: `.github/workflows/codeql.yml`
- Claude Code upstream sync watcher: `.github/workflows/claude-cli-watch.yml`
- Claude Code CLI smoke workflow: `.github/workflows/claude-cli-smoke.yml`

CI installs the published Claude Code CLI package `@anthropic-ai/claude-code`, while the reference submodule tracks upstream source changes from `anthropics/claude-code`.
