# Testing Strategy

## Goal

Verify `ManagedCode.ClaudeCodeSharpSDK` behavior against real Claude Code CLI contracts, with deterministic automated tests for both the core SDK and the optional `Microsoft.Extensions.AI` adapter.

## Test levels used in this repository

- Primary: TUnit behavior tests in `ClaudeCodeSharpSDK.Tests`
- Optional CI matrix: cross-platform Claude Code CLI smoke verification (`.github/workflows/claude-cli-smoke.yml`)

## Principles

- Test observable behavior, not implementation details.
- Use fake process runners for narrow unit tests where behavior is easier to isolate; use the real installed `claude` CLI for smoke and authenticated integration coverage.
- Treat `claude` as a prerequisite for smoke and authenticated integration runs and install it in CI/local setup before running those tests.
- CI validates Claude Code CLI smoke behavior on Linux/macOS/Windows without requiring login: CLI must be discoverable and invokable.
- Cross-platform CI smoke also validates unauthenticated behavior in an isolated profile.
- Real integration runs must use an existing Claude Code CLI login/session; test harness does not use API key environment variables.
- Authenticated local integration tests use an explicit TUnit skip condition when no local Claude Code session is available; missing auth is reported as skipped, not silently passed.
- Authenticated local integration skip detection uses a cached real Claude print-mode probe instead of `claude auth status`, because the CLI status command is not reliable in all local environments.
- Real integration model selection may be overridden with `CLAUDE_TEST_MODEL`; otherwise tests use the Claude Code default model discovered from local settings.
- Cover error paths and cancellation paths.
- Keep protocol parser coverage for all supported event/item kinds.
- Keep a large-stream parser performance profile test to catch regressions.
- Treat `claude -p --output-format json|stream-json` as the protocol source of truth for smoke and parser tests.

## Commands

- build: `dotnet build ManagedCode.ClaudeCodeSharpSDK.slnx -c Release -warnaserror`
- test: `dotnet test --solution ManagedCode.ClaudeCodeSharpSDK.slnx -c Release`
- coverage: `dotnet test --solution ManagedCode.ClaudeCodeSharpSDK.slnx -c Release -- --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml`
- claude smoke subset: `dotnet test --project ClaudeCodeSharpSDK.Tests/ClaudeCodeSharpSDK.Tests.csproj -c Release -- --treenode-filter "/*/*/*/ClaudeCli_Smoke_*"`
- ci/release non-auth full run: `dotnet test --solution ManagedCode.ClaudeCodeSharpSDK.slnx -c Release -- --treenode-filter "/*/*/*/*[RequiresClaudeAuth!=true]"`

Smoke subset is an additional gate and does not replace full-solution test execution. The focused smoke pattern uses the `ClaudeCli_Smoke_*` method prefix.

TUnit on Microsoft Testing Platform does not support `--filter`; run focused tests with `-- --treenode-filter "/*/*/<ClassName>/*"`.

## Test map

- Client lifecycle and concurrency: [ClaudeClientTests.cs](../../ClaudeCodeSharpSDK.Tests/Unit/ClaudeClientTests.cs)
- `ClaudeClient` API surface behavior: [ClaudeClientTests.cs](../../ClaudeCodeSharpSDK.Tests/Unit/ClaudeClientTests.cs)
- `ClaudeThread` run/stream/failure behavior: [ClaudeThreadTests.cs](../../ClaudeCodeSharpSDK.Tests/Unit/ClaudeThreadTests.cs)
- CLI arg/env/config behavior: [ClaudeExecTests.cs](../../ClaudeCodeSharpSDK.Tests/Unit/ClaudeExecTests.cs)
- CLI locator behavior: [ClaudeCliLocatorTests.cs](../../ClaudeCodeSharpSDK.Tests/Unit/ClaudeCliLocatorTests.cs)
- CLI metadata parsing behavior: [ClaudeCliMetadataReaderTests.cs](../../ClaudeCodeSharpSDK.Tests/Unit/ClaudeCliMetadataReaderTests.cs)
- SDK model catalog consistency: [ClaudeModelsTests.cs](../../ClaudeCodeSharpSDK.Tests/Unit/ClaudeModelsTests.cs)
- Cross-platform Claude Code CLI smoke behavior: [ClaudeCliSmokeTests.cs](../../ClaudeCodeSharpSDK.Tests/Integration/ClaudeCliSmokeTests.cs)
- Real process integration behavior: [ClaudeExecIntegrationTests.cs](../../ClaudeCodeSharpSDK.Tests/Integration/ClaudeExecIntegrationTests.cs)
- Real Claude Code CLI integration behavior (local login required): [RealClaudeIntegrationTests.cs](../../ClaudeCodeSharpSDK.Tests/Integration/RealClaudeIntegrationTests.cs)
- `Microsoft.Extensions.AI` mapper and DI behavior: [ClaudeCodeSharpSDK.Tests/MEAI](../../ClaudeCodeSharpSDK.Tests/MEAI)
- Protocol parser behavior: [ThreadEventParserTests.cs](../../ClaudeCodeSharpSDK.Tests/Unit/ThreadEventParserTests.cs)
- Protocol parser large-stream performance profile: [ThreadEventParserPerformanceTests.cs](../../ClaudeCodeSharpSDK.Tests/Performance/ThreadEventParserPerformanceTests.cs)
- Structured output schema behavior: [StructuredOutputSchemaTests.cs](../../ClaudeCodeSharpSDK.Tests/Unit/StructuredOutputSchemaTests.cs)
