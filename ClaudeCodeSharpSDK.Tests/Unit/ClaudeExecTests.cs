using System.Text.Json.Nodes;
using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Execution;
using ManagedCode.ClaudeCodeSharpSDK.Models;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Unit;

public class ClaudeExecTests
{
    [Test]
    public async Task BuildCommandArgs_MapsClaudeFlagsAndMergedSettings()
    {
        var exec = new ClaudeExec(
            executablePath: "claude",
            environmentOverride: null,
            baseSettings: new JsonObject
            {
                ["theme"] = "claude",
                ["hooks"] = new JsonObject
                {
                    ["pre"] = "base-hook",
                },
            });

        var commandArgs = exec.BuildCommandArgs(new ClaudeExecArgs
        {
            Input = "Summarize the diff",
            Model = ClaudeModels.ClaudeOpus45,
            PermissionMode = PermissionMode.AcceptEdits,
            AllowedTools = ["Read", "Write"],
            DisallowedTools = ["Bash"],
            AdditionalDirectories = ["/repo", "/tmp"],
            McpConfigs = ["/tmp/mcp.json"],
            SystemPrompt = "System prompt",
            AppendSystemPrompt = "Append prompt",
            ResumeSessionId = "session-1",
            MaxBudgetUsd = 0.25m,
            SettingSources = [SettingSource.User, SettingSource.Project],
            PluginDirectories = ["/plugins/a", "/plugins/b"],
            InlineAgents = new Dictionary<string, InlineAgentDefinition>(StringComparer.Ordinal)
            {
                ["reviewer"] = new("Code reviewer", "Review code changes"),
            },
            Settings = new JsonObject
            {
                ["hooks"] = new JsonObject
                {
                    ["post"] = "per-turn-hook",
                },
            },
            AdditionalCliArguments = ["--custom-flag"],
        });

        await Assert.That(commandArgs[0]).IsEqualTo("--print");
        await Assert.That(commandArgs[1]).IsEqualTo("--output-format");
        await Assert.That(commandArgs[2]).IsEqualTo("stream-json");
        await Assert.That(commandArgs[3]).IsEqualTo("--input-format");
        await Assert.That(commandArgs[4]).IsEqualTo("text");
        await Assert.That(commandArgs[5]).IsEqualTo("--verbose");
        await Assert.That(GetRequiredFlagValue(commandArgs, "--model")).IsEqualTo(ClaudeModels.ClaudeOpus45);
        await Assert.That(GetRequiredFlagValue(commandArgs, "--permission-mode")).IsEqualTo("acceptEdits");
        await Assert.That(GetRequiredFlagValue(commandArgs, "--allowed-tools")).IsEqualTo("Read,Write");
        await Assert.That(GetRequiredFlagValue(commandArgs, "--disallowed-tools")).IsEqualTo("Bash");
        await Assert.That(GetAllFlagValues(commandArgs, "--add-dir")).IsEquivalentTo(["/repo", "/tmp"]);
        await Assert.That(GetAllFlagValues(commandArgs, "--mcp-config")).IsEquivalentTo(["/tmp/mcp.json"]);
        await Assert.That(GetRequiredFlagValue(commandArgs, "--system-prompt")).IsEqualTo("System prompt");
        await Assert.That(GetRequiredFlagValue(commandArgs, "--append-system-prompt")).IsEqualTo("Append prompt");
        await Assert.That(GetRequiredFlagValue(commandArgs, "--resume")).IsEqualTo("session-1");
        await Assert.That(GetRequiredFlagValue(commandArgs, "--max-budget-usd")).IsEqualTo("0.25");
        await Assert.That(GetRequiredFlagValue(commandArgs, "--setting-sources")).IsEqualTo("user,project");
        await Assert.That(GetAllFlagValues(commandArgs, "--plugin-dir")).IsEquivalentTo(["/plugins/a", "/plugins/b"]);
        await Assert.That(commandArgs[^1]).IsEqualTo("--custom-flag");

        var settings = JsonNode.Parse(GetRequiredFlagValue(commandArgs, "--settings"))!.AsObject();
        await Assert.That(settings["theme"]!.GetValue<string>()).IsEqualTo("claude");
        await Assert.That(settings["hooks"]!["pre"]!.GetValue<string>()).IsEqualTo("base-hook");
        await Assert.That(settings["hooks"]!["post"]!.GetValue<string>()).IsEqualTo("per-turn-hook");

        var agents = JsonNode.Parse(GetRequiredFlagValue(commandArgs, "--agents"))!.AsObject();
        var reviewer = agents["reviewer"]!.AsObject();
        var description = reviewer["Description"]?.GetValue<string>() ?? reviewer["description"]?.GetValue<string>();
        await Assert.That(description).IsEqualTo("Code reviewer");
    }

    [Test]
    public async Task BuildCommandArgs_RepeatsVarArgFlagsWithoutCommaPacking()
    {
        var exec = new ClaudeExec(executablePath: "claude");

        var commandArgs = exec.BuildCommandArgs(new ClaudeExecArgs
        {
            Input = "Health check",
            AdditionalDirectories = ["/repo", "/tmp"],
            McpConfigs =
            [
                """{"name":"one","command":"npx","args":["a","b"]}""",
                """{"name":"two","command":"uvx","args":["c","d"]}""",
            ],
            Betas = ["feature-a", "feature-b"],
        });

        await Assert.That(GetAllFlagValues(commandArgs, "--add-dir")).IsEquivalentTo(["/repo", "/tmp"]);
        await Assert.That(GetAllFlagValues(commandArgs, "--mcp-config")).IsEquivalentTo(
            [
                """{"name":"one","command":"npx","args":["a","b"]}""",
                """{"name":"two","command":"uvx","args":["c","d"]}""",
            ]);
        await Assert.That(GetAllFlagValues(commandArgs, "--betas")).IsEquivalentTo(["feature-a", "feature-b"]);
    }

    [Test]
    public async Task BuildEnvironment_IncludesAnthropicOverrides()
    {
        var exec = new ClaudeExec(
            executablePath: "claude",
            environmentOverride: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CLAUDE_TEST_ENV"] = "present",
            });

        var environment = exec.BuildEnvironment("https://example.invalid", "test-key");

        await Assert.That(environment["CLAUDE_TEST_ENV"]).IsEqualTo("present");
        await Assert.That(environment["ANTHROPIC_BASE_URL"]).IsEqualTo("https://example.invalid");
        await Assert.That(environment["ANTHROPIC_API_KEY"]).IsEqualTo("test-key");
    }

    [Test]
    public async Task BuildCommandArgs_WithReplayUserMessages_ThrowsUntilStreamJsonInputIsSupported()
    {
        var exec = new ClaudeExec(executablePath: "claude");

        var exception = await Assert.That(() => exec.BuildCommandArgs(new ClaudeExecArgs
        {
            Input = "Summarize",
            ReplayUserMessages = true,
        })).ThrowsException();

        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("ReplayUserMessages");
    }

    [Test]
    public async Task BuildCommandArgs_WithReservedAdditionalCliFlag_Throws()
    {
        var exec = new ClaudeExec(executablePath: "claude");

        var exception = await Assert.That(() => exec.BuildCommandArgs(new ClaudeExecArgs
        {
            Input = "Summarize",
            AdditionalCliArguments = ["--output-format", "text"],
        })).ThrowsException();

        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("--output-format");
    }

    private static string GetRequiredFlagValue(IReadOnlyList<string> commandArgs, string flag)
    {
        var index = FindFlagIndex(commandArgs, flag);
        if (index < 0 || index == commandArgs.Count - 1)
        {
            throw new InvalidOperationException($"Flag '{flag}' was not found.");
        }

        return commandArgs[index + 1];
    }

    private static List<string> GetAllFlagValues(IReadOnlyList<string> commandArgs, string flag)
    {
        var values = new List<string>();
        for (var index = 0; index < commandArgs.Count - 1; index += 1)
        {
            if (string.Equals(commandArgs[index], flag, StringComparison.Ordinal))
            {
                values.Add(commandArgs[index + 1]);
            }
        }

        return values;
    }

    private static int FindFlagIndex(IReadOnlyList<string> commandArgs, string flag)
    {
        for (var index = 0; index < commandArgs.Count; index += 1)
        {
            if (string.Equals(commandArgs[index], flag, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
