using System.Text.Json.Nodes;
using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Execution;
using ManagedCode.ClaudeCodeSharpSDK.Models;
using ManagedCode.ClaudeCodeSharpSDK.Tests.Shared;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Unit;

public class ClaudeExecTests
{
    private const string AcceptEditsPermissionMode = "acceptEdits";
    private const string AddDirectoryFlag = "--add-dir";
    private const string AgentsFlag = "--agents";
    private const string AllowedToolsFlag = "--allowed-tools";
    private const string AnthropicApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY";
    private const string AnthropicBaseUrlEnvironmentVariable = "ANTHROPIC_BASE_URL";
    private const string AppendPromptText = "Append prompt";
    private const string AppendSystemPromptFlag = "--append-system-prompt";
    private const string BaseHookValue = "base-hook";
    private const string BashToolName = "Bash";
    private const string BetasFlag = "--betas";
    private const string ClaudeTestEnvironmentVariable = "CLAUDE_TEST_ENV";
    private const string ClaudeThemeValue = "claude";
    private const string CodeReviewerDescription = "Code reviewer";
    private const string CustomFlag = "--custom-flag";
    private const string DescriptionPropertyName = "Description";
    private const string DescriptionPropertyNameCamel = "description";
    private const string DisallowedToolsFlag = "--disallowed-tools";
    private const string ExampleBaseUrl = "https://example.invalid";
    private const string FeatureABeta = "feature-a";
    private const string FeatureBBeta = "feature-b";
    private const string FlagNotFoundMessagePrefix = "Flag '";
    private const string FlagNotFoundMessageSuffix = "' was not found.";
    private const string HealthCheckInput = "Health check";
    private const string HooksKey = "hooks";
    private const string InputFormatFlag = "--input-format";
    private const string NameFlag = "--name";
    private const string SessionNameValue = "my-session";
    private const string MaxBudgetFlag = "--max-budget-usd";
    private const string MaxBudgetValue = "0.25";
    private const string McpConfigFlag = "--mcp-config";
    private const string McpConfigOne = """{"name":"one","command":"npx","args":["a","b"]}""";
    private const string McpConfigPath = "/tmp/mcp.json";
    private const string McpConfigTwo = """{"name":"two","command":"uvx","args":["c","d"]}""";
    private const string ModelFlag = "--model";
    private const string OutputFormatFlag = "--output-format";
    private const string OutputSchemaMessageFragment = "ReplayUserMessages";
    private const string PerTurnHookValue = "per-turn-hook";
    private const string PermissionModeFlag = "--permission-mode";
    private const string PluginDirectoryA = "/plugins/a";
    private const string PluginDirectoryB = "/plugins/b";
    private const string PluginDirectoryFlag = "--plugin-dir";
    private const string PostHookKey = "post";
    private const string PreHookKey = "pre";
    private const string PresentValue = "present";
    private const string PrintFlag = "--print";
    private const string ReadToolName = "Read";
    private const string RepoDirectory = "/repo";
    private const string ReservedOutputFormatFlag = "--output-format";
    private const string ResumeFlag = "--resume";
    private const string ResumeSessionId = "session-1";
    private const string ReviewerAgentKey = "reviewer";
    private const string ReviewerPrompt = "Review code changes";
    private const string SettingSourcesFlag = "--setting-sources";
    private const string SettingsFlag = "--settings";
    private const string StreamJsonOutputFormat = "stream-json";
    private const string SummarizeDiffInput = "Summarize the diff";
    private const string SummarizeInput = "Summarize";
    private const string SystemPromptFlag = "--system-prompt";
    private const string SystemPromptText = "System prompt";
    private const string TestApiKey = "test-key";
    private const string TextInputFormat = "text";
    private const string ThemeKey = "theme";
    private const string TmpDirectory = "/tmp";
    private const string UserProjectSettingSources = "user,project";
    private const string VerboseFlag = "--verbose";
    private const string WriteToolName = "Write";
    private static readonly string[] AdditionalDirectories = [RepoDirectory, TmpDirectory];
    private static readonly string[] AllowedTools = [ReadToolName, WriteToolName];
    private static readonly string[] BetaFlags = [FeatureABeta, FeatureBBeta];
    private static readonly string[] McpConfigs = [McpConfigOne, McpConfigTwo];
    private static readonly string[] PluginDirectories = [PluginDirectoryA, PluginDirectoryB];

    [Test]
    public async Task BuildCommandArgs_MapsClaudeFlagsAndMergedSettings()
    {
        var exec = new ClaudeExec(
            executablePath: TestConstants.ClaudeExecutablePath,
            environmentOverride: null,
            baseSettings: CreateBaseSettings());

        var commandArgs = exec.BuildCommandArgs(new ClaudeExecArgs
        {
            Input = SummarizeDiffInput,
            Model = ClaudeModels.ClaudeOpus45,
            PermissionMode = PermissionMode.AcceptEdits,
            AllowedTools = AllowedTools,
            DisallowedTools = [BashToolName],
            AdditionalDirectories = AdditionalDirectories,
            McpConfigs = [McpConfigPath],
            SystemPrompt = SystemPromptText,
            AppendSystemPrompt = AppendPromptText,
            ResumeSessionId = ResumeSessionId,
            MaxBudgetUsd = 0.25m,
            SettingSources = [SettingSource.User, SettingSource.Project],
            PluginDirectories = PluginDirectories,
            InlineAgents = new Dictionary<string, InlineAgentDefinition>(StringComparer.Ordinal)
            {
                [ReviewerAgentKey] = new(CodeReviewerDescription, ReviewerPrompt),
            },
            Settings = CreateTurnSettings(),
            AdditionalCliArguments = [CustomFlag],
        });

        await Assert.That(commandArgs[0]).IsEqualTo(PrintFlag);
        await Assert.That(commandArgs[1]).IsEqualTo(OutputFormatFlag);
        await Assert.That(commandArgs[2]).IsEqualTo(StreamJsonOutputFormat);
        await Assert.That(commandArgs[3]).IsEqualTo(InputFormatFlag);
        await Assert.That(commandArgs[4]).IsEqualTo(TextInputFormat);
        await Assert.That(commandArgs[5]).IsEqualTo(VerboseFlag);
        await Assert.That(GetRequiredFlagValue(commandArgs, ModelFlag)).IsEqualTo(ClaudeModels.ClaudeOpus45);
        await Assert.That(GetRequiredFlagValue(commandArgs, PermissionModeFlag)).IsEqualTo(AcceptEditsPermissionMode);
        await Assert.That(GetRequiredFlagValue(commandArgs, AllowedToolsFlag)).IsEqualTo(string.Join(',', AllowedTools));
        await Assert.That(GetRequiredFlagValue(commandArgs, DisallowedToolsFlag)).IsEqualTo(BashToolName);
        await Assert.That(GetAllFlagValues(commandArgs, AddDirectoryFlag)).IsEquivalentTo(AdditionalDirectories);
        await Assert.That(GetAllFlagValues(commandArgs, McpConfigFlag)).IsEquivalentTo([McpConfigPath]);
        await Assert.That(GetRequiredFlagValue(commandArgs, SystemPromptFlag)).IsEqualTo(SystemPromptText);
        await Assert.That(GetRequiredFlagValue(commandArgs, AppendSystemPromptFlag)).IsEqualTo(AppendPromptText);
        await Assert.That(GetRequiredFlagValue(commandArgs, ResumeFlag)).IsEqualTo(ResumeSessionId);
        await Assert.That(GetRequiredFlagValue(commandArgs, MaxBudgetFlag)).IsEqualTo(MaxBudgetValue);
        await Assert.That(GetRequiredFlagValue(commandArgs, SettingSourcesFlag)).IsEqualTo(UserProjectSettingSources);
        await Assert.That(GetAllFlagValues(commandArgs, PluginDirectoryFlag)).IsEquivalentTo(PluginDirectories);
        await Assert.That(commandArgs[^1]).IsEqualTo(CustomFlag);

        var settings = JsonNode.Parse(GetRequiredFlagValue(commandArgs, SettingsFlag))!.AsObject();
        await Assert.That(settings[ThemeKey]!.GetValue<string>()).IsEqualTo(ClaudeThemeValue);
        await Assert.That(settings[HooksKey]![PreHookKey]!.GetValue<string>()).IsEqualTo(BaseHookValue);
        await Assert.That(settings[HooksKey]![PostHookKey]!.GetValue<string>()).IsEqualTo(PerTurnHookValue);

        var agents = JsonNode.Parse(GetRequiredFlagValue(commandArgs, AgentsFlag))!.AsObject();
        var reviewer = agents[ReviewerAgentKey]!.AsObject();
        var description = reviewer[DescriptionPropertyName]?.GetValue<string>() ?? reviewer[DescriptionPropertyNameCamel]?.GetValue<string>();
        await Assert.That(description).IsEqualTo(CodeReviewerDescription);
    }

    [Test]
    public async Task BuildCommandArgs_RepeatsVarArgFlagsWithoutCommaPacking()
    {
        var exec = new ClaudeExec(executablePath: TestConstants.ClaudeExecutablePath);

        var commandArgs = exec.BuildCommandArgs(new ClaudeExecArgs
        {
            Input = HealthCheckInput,
            AdditionalDirectories = AdditionalDirectories,
            McpConfigs = McpConfigs,
            Betas = BetaFlags,
        });

        await Assert.That(GetAllFlagValues(commandArgs, AddDirectoryFlag)).IsEquivalentTo(AdditionalDirectories);
        await Assert.That(GetAllFlagValues(commandArgs, McpConfigFlag)).IsEquivalentTo(McpConfigs);
        await Assert.That(GetAllFlagValues(commandArgs, BetasFlag)).IsEquivalentTo(BetaFlags);
    }

    [Test]
    public async Task BuildEnvironment_IncludesAnthropicOverrides()
    {
        var exec = new ClaudeExec(
            executablePath: TestConstants.ClaudeExecutablePath,
            environmentOverride: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ClaudeTestEnvironmentVariable] = PresentValue,
            });

        var environment = exec.BuildEnvironment(ExampleBaseUrl, TestApiKey);

        await Assert.That(environment[ClaudeTestEnvironmentVariable]).IsEqualTo(PresentValue);
        await Assert.That(environment[AnthropicBaseUrlEnvironmentVariable]).IsEqualTo(ExampleBaseUrl);
        await Assert.That(environment[AnthropicApiKeyEnvironmentVariable]).IsEqualTo(TestApiKey);
    }

    [Test]
    public async Task BuildCommandArgs_WithReplayUserMessages_ThrowsUntilStreamJsonInputIsSupported()
    {
        var exec = new ClaudeExec(executablePath: TestConstants.ClaudeExecutablePath);

        var exception = await Assert.That(() => exec.BuildCommandArgs(new ClaudeExecArgs
        {
            Input = SummarizeInput,
            ReplayUserMessages = true,
        })).ThrowsException();

        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains(OutputSchemaMessageFragment);
    }

    [Test]
    public async Task BuildCommandArgs_WithReservedAdditionalCliFlag_Throws()
    {
        var exec = new ClaudeExec(executablePath: TestConstants.ClaudeExecutablePath);

        var exception = await Assert.That(() => exec.BuildCommandArgs(new ClaudeExecArgs
        {
            Input = SummarizeInput,
            AdditionalCliArguments = [ReservedOutputFormatFlag, TextInputFormat],
        })).ThrowsException();

        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains(ReservedOutputFormatFlag);
    }

    [Test]
    public async Task BuildCommandArgs_WithSessionName_IncludesNameFlag()
    {
        var exec = new ClaudeExec(executablePath: TestConstants.ClaudeExecutablePath);

        var commandArgs = exec.BuildCommandArgs(new ClaudeExecArgs
        {
            Input = SummarizeInput,
            SessionName = SessionNameValue,
        });

        await Assert.That(GetRequiredFlagValue(commandArgs, NameFlag)).IsEqualTo(SessionNameValue);
    }

    [Test]
    public async Task BuildCommandArgs_WithoutSessionName_DoesNotIncludeNameFlag()
    {
        var exec = new ClaudeExec(executablePath: TestConstants.ClaudeExecutablePath);

        var commandArgs = exec.BuildCommandArgs(new ClaudeExecArgs
        {
            Input = SummarizeInput,
        });

        await Assert.That(FindFlagIndex(commandArgs, NameFlag)).IsEqualTo(-1);
    }

    private static JsonObject CreateBaseSettings()
    {
        return new JsonObject
        {
            [ThemeKey] = ClaudeThemeValue,
            [HooksKey] = new JsonObject
            {
                [PreHookKey] = BaseHookValue,
            },
        };
    }

    private static JsonObject CreateTurnSettings()
    {
        return new JsonObject
        {
            [HooksKey] = new JsonObject
            {
                [PostHookKey] = PerTurnHookValue,
            },
        };
    }

    private static string GetRequiredFlagValue(IReadOnlyList<string> commandArgs, string flag)
    {
        var index = FindFlagIndex(commandArgs, flag);
        if (index < 0 || index == commandArgs.Count - 1)
        {
            throw new InvalidOperationException(string.Concat(FlagNotFoundMessagePrefix, flag, FlagNotFoundMessageSuffix));
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
