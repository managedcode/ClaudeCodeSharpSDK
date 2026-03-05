using System.Text.Json;
using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Tests;

public class ChatOptionsMapperTests
{
    [Test]
    public async Task ToThreadOptions_PrefersChatModelIdOverDefaults()
    {
        var chatOptions = new ChatOptions { ModelId = "claude-opus-4-5" };
        var clientOptions = new ClaudeChatClientOptions { DefaultModel = ClaudeModels.Sonnet };

        var result = ChatOptionsMapper.ToThreadOptions(chatOptions, clientOptions);

        await Assert.That(result.Model).IsEqualTo("claude-opus-4-5");
    }

    [Test]
    public async Task ToThreadOptions_MapsClaudeAdditionalProperties()
    {
        var chatOptions = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatOptionsMapper.WorkingDirectoryKey] = "/tmp/workspace",
                [ChatOptionsMapper.PermissionModeKey] = PermissionMode.AcceptEdits,
                [ChatOptionsMapper.AllowedToolsKey] = new[] { "Read", "Write" },
                [ChatOptionsMapper.DisallowedToolsKey] = new[] { "Bash" },
                [ChatOptionsMapper.SystemPromptKey] = "Use terse answers",
                [ChatOptionsMapper.AppendSystemPromptKey] = "Always include file paths",
                [ChatOptionsMapper.MaxBudgetUsdKey] = 0.25m,
            },
        };

        var result = ChatOptionsMapper.ToThreadOptions(chatOptions, new ClaudeChatClientOptions());

        await Assert.That(result.WorkingDirectory).IsEqualTo("/tmp/workspace");
        await Assert.That(result.PermissionMode).IsEqualTo(PermissionMode.AcceptEdits);
        await Assert.That(result.AllowedTools).IsEquivalentTo(["Read", "Write"]);
        await Assert.That(result.DisallowedTools).IsEquivalentTo(["Bash"]);
        await Assert.That(result.SystemPrompt).IsEqualTo("Use terse answers");
        await Assert.That(result.AppendSystemPrompt).IsEqualTo("Always include file paths");
        await Assert.That(result.MaxBudgetUsd).IsEqualTo(0.25m);
    }

    [Test]
    public async Task ToThreadOptions_AcceptsCommonScalarAndStringForms()
    {
        var chatOptions = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatOptionsMapper.PermissionModeKey] = "acceptEdits",
                [ChatOptionsMapper.AllowedToolsKey] = "Read, Write",
                [ChatOptionsMapper.DisallowedToolsKey] = new List<string> { "Bash", "  " },
                [ChatOptionsMapper.MaxBudgetUsdKey] = 0.5d,
            },
        };

        var result = ChatOptionsMapper.ToThreadOptions(chatOptions, new ClaudeChatClientOptions());

        await Assert.That(result.PermissionMode).IsEqualTo(PermissionMode.AcceptEdits);
        await Assert.That(result.AllowedTools).IsEquivalentTo(["Read", "Write"]);
        await Assert.That(result.DisallowedTools).IsEquivalentTo(["Bash"]);
        await Assert.That(result.MaxBudgetUsd).IsEqualTo(0.5m);
    }

    [Test]
    public async Task ToThreadOptions_InvalidClaudeAdditionalProperty_Throws()
    {
        var chatOptions = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatOptionsMapper.PermissionModeKey] = "invalid-mode",
            },
        };

        var exception = await Assert.That(() => ChatOptionsMapper.ToThreadOptions(chatOptions, new ClaudeChatClientOptions()))
            .ThrowsException();

        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains(ChatOptionsMapper.PermissionModeKey);
    }

    [Test]
    public async Task ToTurnOptions_ForwardsCancellationToken()
    {
        using var cts = new CancellationTokenSource();

        var result = ChatOptionsMapper.ToTurnOptions(null, cts.Token);

        await Assert.That(result.CancellationToken).IsEqualTo(cts.Token);
    }

    [Test]
    public async Task ToThreadOptions_ParsesJsonAndNumericAdditionalProperties()
    {
        using var json = JsonDocument.Parse(
            """
            {
              "cwd": "/workspace",
              "permission": "acceptEdits",
              "allowed": ["Read", "Write"],
              "denied": ["Bash"],
              "system": "Stay precise",
              "append": "Show file paths",
              "budget": 1.5
            }
            """);

        var root = json.RootElement;
        var chatOptions = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatOptionsMapper.WorkingDirectoryKey] = root.GetProperty("cwd").Clone(),
                [ChatOptionsMapper.PermissionModeKey] = root.GetProperty("permission").Clone(),
                [ChatOptionsMapper.AllowedToolsKey] = root.GetProperty("allowed").Clone(),
                [ChatOptionsMapper.DisallowedToolsKey] = root.GetProperty("denied").Clone(),
                [ChatOptionsMapper.SystemPromptKey] = root.GetProperty("system").Clone(),
                [ChatOptionsMapper.AppendSystemPromptKey] = root.GetProperty("append").Clone(),
                [ChatOptionsMapper.MaxBudgetUsdKey] = root.GetProperty("budget").Clone(),
            },
        };

        var result = ChatOptionsMapper.ToThreadOptions(chatOptions, new ClaudeChatClientOptions());

        await Assert.That(result.WorkingDirectory).IsEqualTo("/workspace");
        await Assert.That(result.PermissionMode).IsEqualTo(PermissionMode.AcceptEdits);
        await Assert.That(result.AllowedTools).IsEquivalentTo(["Read", "Write"]);
        await Assert.That(result.DisallowedTools).IsEquivalentTo(["Bash"]);
        await Assert.That(result.SystemPrompt).IsEqualTo("Stay precise");
        await Assert.That(result.AppendSystemPrompt).IsEqualTo("Show file paths");
        await Assert.That(result.MaxBudgetUsd).IsEqualTo(1.5m);
    }
}
