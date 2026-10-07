# Feature: Claude Code CLI Metadata

Links:
Architecture: [docs/Architecture/Overview.md](../Architecture/Overview.md)
Modules: [ClaudeClient.cs](../../ClaudeCodeSharpSDK/Client/ClaudeClient.cs), [ClaudeCliMetadataReader.cs](../../ClaudeCodeSharpSDK/Internal/ClaudeCliMetadataReader.cs), [ClaudeCliMetadata.cs](../../ClaudeCodeSharpSDK/Models/ClaudeCliMetadata.cs)
Source of truth: local `claude` CLI behavior + upstream `anthropics/claude-code` tags and settings layout

---

## Purpose

The SDK package version mirrors the targeted Claude Code CLI version in its first three numeric components and uses the fourth component for an SDK hotfix. `ClaudeCliCompatibility.TargetVersion` exposes the exact compatible CLI target without starting a process. `GetCliUpdateStatus()` separately reports the latest version discovered from upstream.

Expose runtime Claude Code CLI metadata to SDK consumers:

- installed `claude` version
- default model configured in local Claude config
- SDK-known Claude model aliases/constants
- update availability status vs latest upstream tagged Claude Code version

The repository's upstream sync workflow separately tracks source changes in `anthropics/claude-code`.

---

## Scope

### In scope

- `ClaudeClient.GetCliMetadata()` public API.
- `ClaudeClient.GetCliUpdateStatus()` public API.
- Reading version from `claude --version`.
- Reading default model from Claude settings discovery (`.claude/settings.local.json`, `.claude/settings.json`, `~/.claude/settings.json`) with SDK fallback alias `sonnet`.
- Reading latest upstream tagged version from `https://github.com/anthropics/claude-code.git`.
- Returning `claude update` in update status when a newer upstream tag exists.

### Out of scope

- Remote model discovery over Anthropic APIs.
- Mutating user Claude config files.
- Replacing Claude Code CLI model selection logic.

---

## Business Rules

- Metadata read is read-only and does not mutate local Claude state.
- SDK option and metadata decisions are based on real Claude Code CLI behavior, not any separate SDK surface.
- Update check failures (for example missing `git` or network access) must return actionable status messages and never silently fail.
- Version/update probes must drain subprocess stdout and stderr concurrently so CLI metadata reads cannot deadlock on buffered process output.
- Metadata subprocesses use the same configured environment policy as SDK execution, and their runtime/output bounds are configured by `ClaudeOptions.CliMetadataProbeTimeout` and `ClaudeOptions.CliMetadataMaximumOutputCharacters`.
- A timeout kills the process tree and confirms root-process exit. Output is drained concurrently with bounded capture; exceeding either stream's cap fails the probe instead of parsing partial output.
- The upstream sync watcher must raise a repository issue when `anthropics/claude-code` moves ahead of the pinned submodule SHA.

---

## Diagram

```mermaid
flowchart LR
  Client["ClaudeClient.GetCliMetadata()"] --> Version["claude --version"]
  Client --> Update["ClaudeClient.GetCliUpdateStatus()"]
  Client --> Config[".claude/settings.local.json /.claude/settings.json / ~/.claude/settings.json"]
  Update --> Git["git ls-remote --tags anthropics/claude-code"]
  Version --> Metadata["ClaudeCliMetadata"]
  Git --> UpdateStatus["ClaudeCliUpdateStatus"]
  Config --> Metadata
```

---

## Verification

- Unit parsing/update-check coverage: [ClaudeCliMetadataReaderTests.cs](../../ClaudeCodeSharpSDK.Tests/Unit/ClaudeCliMetadataReaderTests.cs)
- CLI arg behavior: [ClaudeExecTests.cs](../../ClaudeCodeSharpSDK.Tests/Unit/ClaudeExecTests.cs)
