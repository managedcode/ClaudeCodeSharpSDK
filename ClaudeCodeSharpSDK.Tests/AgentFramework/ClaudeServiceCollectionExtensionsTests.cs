using ManagedCode.ClaudeCodeSharpSDK.Extensions.AgentFramework.Extensions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.AgentFramework;

public class ClaudeServiceCollectionExtensionsTests
{
    private const string AgentDescription = "Agent description";
    private const string AgentInstructions = "You are a coding assistant.";
    private const string AgentName = "claude-agent";
    private const string ConfiguredDefaultModel = "configured-default-model";
    private const string KeyedServiceName = "claude-agent";
    private const string ProviderName = "ClaudeCodeCLI";
    private const string ServicesParameterName = "services";
    private const string ServiceKeyParameterName = "serviceKey";

    [Test]
    public async Task AddClaudeCodeAgent_ThrowsForNullServices()
    {
        ServiceCollection? services = null;

        var action = () => services!.AddClaudeCodeAgent();

        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception).IsTypeOf<ArgumentNullException>();
        await Assert.That(((ArgumentNullException)exception!).ParamName).IsEqualTo(ServicesParameterName);
    }

    [Test]
    public async Task AddClaudeCodeAgent_RegistersAIAgentAndChatClient()
    {
        var services = new ServiceCollection();
        services.AddClaudeCodeAgent();

        var provider = services.BuildServiceProvider();
        var agent = provider.GetService<AIAgent>();
        var chatClient = provider.GetService<IChatClient>();
        var resolvedAgentChatClient = agent?.GetService(typeof(IChatClient)) as IChatClient;
        var metadata = resolvedAgentChatClient?.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata;

        await Assert.That(agent).IsNotNull();
        await Assert.That(chatClient).IsNotNull();
        await Assert.That(resolvedAgentChatClient).IsNotNull();
        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.ProviderName).IsEqualTo(ProviderName);
    }

    [Test]
    public async Task AddClaudeCodeAgent_WithConfiguration_AppliesAgentOptions()
    {
        var services = new ServiceCollection();
        services.AddClaudeCodeAgent(
            configureChatClient: options => options.DefaultModel = ConfiguredDefaultModel,
            configureAgent: options =>
            {
                options.Name = AgentName;
                options.Description = AgentDescription;
                options.ChatOptions = new ChatOptions
                {
                    Instructions = AgentInstructions,
                };
            });

        var provider = services.BuildServiceProvider();
        var agent = provider.GetRequiredService<AIAgent>();
        var chatClient = agent.GetService<IChatClient>();
        var metadata = chatClient?.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata;
        var agentOptions = agent.GetService<ChatClientAgentOptions>();

        await Assert.That(agent.Name).IsEqualTo(AgentName);
        await Assert.That(agent.Description).IsEqualTo(AgentDescription);
        await Assert.That(agentOptions).IsNotNull();
        await Assert.That(agentOptions!.ChatOptions).IsNotNull();
        await Assert.That(agentOptions.ChatOptions!.Instructions).IsEqualTo(AgentInstructions);
        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.DefaultModelId).IsEqualTo(ConfiguredDefaultModel);
    }

    [Test]
    public async Task AddKeyedClaudeCodeAgent_RegistersKeyedAgent()
    {
        var services = new ServiceCollection();
        services.AddKeyedClaudeCodeAgent(KeyedServiceName);

        var provider = services.BuildServiceProvider();
        var agent = provider.GetKeyedService<AIAgent>(KeyedServiceName);
        var chatClient = provider.GetKeyedService<IChatClient>(KeyedServiceName);
        var resolvedAgentChatClient = agent?.GetService(typeof(IChatClient)) as IChatClient;
        var metadata = resolvedAgentChatClient?.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata;

        await Assert.That(agent).IsNotNull();
        await Assert.That(chatClient).IsNotNull();
        await Assert.That(resolvedAgentChatClient).IsNotNull();
        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.ProviderName).IsEqualTo(ProviderName);
    }

    [Test]
    public async Task AddKeyedClaudeCodeAgent_ThrowsForNullServiceKey()
    {
        var services = new ServiceCollection();

        var action = () => services.AddKeyedClaudeCodeAgent(serviceKey: null!);

        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception).IsTypeOf<ArgumentNullException>();
        await Assert.That(((ArgumentNullException)exception!).ParamName).IsEqualTo(ServiceKeyParameterName);
    }

    [Test]
    public async Task AddKeyedClaudeCodeAgent_WithConfiguration_AppliesKeyedAgentOptions()
    {
        var services = new ServiceCollection();
        services.AddKeyedClaudeCodeAgent(
            KeyedServiceName,
            configureChatClient: options => options.DefaultModel = ConfiguredDefaultModel,
            configureAgent: options =>
            {
                options.Name = AgentName;
                options.ChatOptions = new ChatOptions
                {
                    Instructions = AgentInstructions,
                };
            });

        var provider = services.BuildServiceProvider();
        var agent = provider.GetRequiredKeyedService<AIAgent>(KeyedServiceName);
        var chatClient = agent.GetService<IChatClient>();
        var metadata = chatClient?.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata;
        var agentOptions = agent.GetService<ChatClientAgentOptions>();

        await Assert.That(agent.Name).IsEqualTo(AgentName);
        await Assert.That(agentOptions).IsNotNull();
        await Assert.That(agentOptions!.ChatOptions).IsNotNull();
        await Assert.That(agentOptions.ChatOptions!.Instructions).IsEqualTo(AgentInstructions);
        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.DefaultModelId).IsEqualTo(ConfiguredDefaultModel);
    }
}
