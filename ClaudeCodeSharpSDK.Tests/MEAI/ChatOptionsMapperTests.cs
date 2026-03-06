using System.Text.Json;
using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Tests;

public class ChatOptionsMapperTests
{
    private const string TemporaryWorkspace = "/tmp/workspace";
    private const string RepositoryWorkspace = "/workspace";
    private const string AllowedToolRead = "Read";
    private const string AllowedToolWrite = "Write";
    private const string DisallowedToolBash = "Bash";
    private const string BlankToolEntry = "  ";
    private const string UseTerseAnswers = "Use terse answers";
    private const string AlwaysIncludeFilePaths = "Always include file paths";
    private const string AcceptEditsPermission = "acceptEdits";
    private const string AllowedToolsCsv = "Read, Write";
    private const string InvalidPermissionMode = "invalid-mode";
    private const string StayPrecise = "Stay precise";
    private const string ShowFilePaths = "Show file paths";
    private const string CwdPropertyName = "cwd";
    private const string PermissionPropertyName = "permission";
    private const string AllowedPropertyName = "allowed";
    private const string DeniedPropertyName = "denied";
    private const string SystemPropertyName = "system";
    private const string AppendPropertyName = "append";
    private const string BudgetPropertyName = "budget";
    private const string JsonAdditionalProperties = "{\"cwd\":\"/workspace\",\"permission\":\"acceptEdits\",\"allowed\":[\"Read\",\"Write\"],\"denied\":[\"Bash\"],\"system\":\"Stay precise\",\"append\":\"Show file paths\",\"budget\":1.5}";

    [Test]
    public async Task ToThreadOptions_PrefersChatModelIdOverDefaults()
    {
        var chatOptions = new ChatOptions { ModelId = ClaudeModels.ClaudeOpus45 };
        var clientOptions = new ClaudeChatClientOptions { DefaultModel = ClaudeModels.Sonnet };

        var result = ChatOptionsMapper.ToThreadOptions(chatOptions, clientOptions);

        await Assert.That(result.Model).IsEqualTo(ClaudeModels.ClaudeOpus45);
    }

    [Test]
    public async Task ToThreadOptions_MapsClaudeAdditionalProperties()
    {
        var chatOptions = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatOptionsMapper.WorkingDirectoryKey] = TemporaryWorkspace,
                [ChatOptionsMapper.PermissionModeKey] = PermissionMode.AcceptEdits,
                [ChatOptionsMapper.AllowedToolsKey] = new[] { AllowedToolRead, AllowedToolWrite },
                [ChatOptionsMapper.DisallowedToolsKey] = new[] { DisallowedToolBash },
                [ChatOptionsMapper.SystemPromptKey] = UseTerseAnswers,
                [ChatOptionsMapper.AppendSystemPromptKey] = AlwaysIncludeFilePaths,
                [ChatOptionsMapper.MaxBudgetUsdKey] = 0.25m,
            },
        };

        var result = ChatOptionsMapper.ToThreadOptions(chatOptions, new ClaudeChatClientOptions());

        await Assert.That(result.WorkingDirectory).IsEqualTo(TemporaryWorkspace);
        await Assert.That(result.PermissionMode).IsEqualTo(PermissionMode.AcceptEdits);
        await Assert.That(result.AllowedTools).IsEquivalentTo([AllowedToolRead, AllowedToolWrite]);
        await Assert.That(result.DisallowedTools).IsEquivalentTo([DisallowedToolBash]);
        await Assert.That(result.SystemPrompt).IsEqualTo(UseTerseAnswers);
        await Assert.That(result.AppendSystemPrompt).IsEqualTo(AlwaysIncludeFilePaths);
        await Assert.That(result.MaxBudgetUsd).IsEqualTo(0.25m);
    }

    [Test]
    public async Task ToThreadOptions_AcceptsCommonScalarAndStringForms()
    {
        var chatOptions = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatOptionsMapper.PermissionModeKey] = AcceptEditsPermission,
                [ChatOptionsMapper.AllowedToolsKey] = AllowedToolsCsv,
                [ChatOptionsMapper.DisallowedToolsKey] = new List<string> { DisallowedToolBash, BlankToolEntry },
                [ChatOptionsMapper.MaxBudgetUsdKey] = 0.5d,
            },
        };

        var result = ChatOptionsMapper.ToThreadOptions(chatOptions, new ClaudeChatClientOptions());

        await Assert.That(result.PermissionMode).IsEqualTo(PermissionMode.AcceptEdits);
        await Assert.That(result.AllowedTools).IsEquivalentTo([AllowedToolRead, AllowedToolWrite]);
        await Assert.That(result.DisallowedTools).IsEquivalentTo([DisallowedToolBash]);
        await Assert.That(result.MaxBudgetUsd).IsEqualTo(0.5m);
    }

    [Test]
    public async Task ToThreadOptions_InvalidClaudeAdditionalProperty_Throws()
    {
        var chatOptions = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatOptionsMapper.PermissionModeKey] = InvalidPermissionMode,
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
        using var json = JsonDocument.Parse(JsonAdditionalProperties);

        var root = json.RootElement;
        var chatOptions = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatOptionsMapper.WorkingDirectoryKey] = root.GetProperty(CwdPropertyName).Clone(),
                [ChatOptionsMapper.PermissionModeKey] = root.GetProperty(PermissionPropertyName).Clone(),
                [ChatOptionsMapper.AllowedToolsKey] = root.GetProperty(AllowedPropertyName).Clone(),
                [ChatOptionsMapper.DisallowedToolsKey] = root.GetProperty(DeniedPropertyName).Clone(),
                [ChatOptionsMapper.SystemPromptKey] = root.GetProperty(SystemPropertyName).Clone(),
                [ChatOptionsMapper.AppendSystemPromptKey] = root.GetProperty(AppendPropertyName).Clone(),
                [ChatOptionsMapper.MaxBudgetUsdKey] = root.GetProperty(BudgetPropertyName).Clone(),
            },
        };

        var result = ChatOptionsMapper.ToThreadOptions(chatOptions, new ClaudeChatClientOptions());

        await Assert.That(result.WorkingDirectory).IsEqualTo(RepositoryWorkspace);
        await Assert.That(result.PermissionMode).IsEqualTo(PermissionMode.AcceptEdits);
        await Assert.That(result.AllowedTools).IsEquivalentTo([AllowedToolRead, AllowedToolWrite]);
        await Assert.That(result.DisallowedTools).IsEquivalentTo([DisallowedToolBash]);
        await Assert.That(result.SystemPrompt).IsEqualTo(StayPrecise);
        await Assert.That(result.AppendSystemPrompt).IsEqualTo(ShowFilePaths);
        await Assert.That(result.MaxBudgetUsd).IsEqualTo(1.5m);
    }
}
