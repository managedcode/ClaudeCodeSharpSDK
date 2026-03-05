using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Extensions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Tests;

public class ClaudeServiceCollectionExtensionsTests
{
    private const string ConfiguredDefaultModel = "configured-default-model";

    [Test]
    public async Task AddClaudeChatClient_RegistersIChatClient()
    {
        var services = new ServiceCollection();
        services.AddClaudeChatClient();
        var provider = services.BuildServiceProvider();
        var client = provider.GetService<IChatClient>();
        await Assert.That(client).IsNotNull();
        await Assert.That(client).IsTypeOf<ClaudeChatClient>();
    }

    [Test]
    public async Task AddClaudeChatClient_WithConfiguration_RegistersIChatClient()
    {
        var services = new ServiceCollection();
        services.AddClaudeChatClient(options => options.DefaultModel = ConfiguredDefaultModel);
        var provider = services.BuildServiceProvider();
        var client = provider.GetService<IChatClient>();
        await Assert.That(client).IsNotNull();

        var metadata = client!.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata;
        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.DefaultModelId).IsEqualTo(ConfiguredDefaultModel);
    }

    [Test]
    public async Task AddClaudeChatClient_DefaultThreadModel_IsExposedViaMetadata()
    {
        var services = new ServiceCollection();
        services.AddClaudeChatClient(options => options.DefaultThreadOptions = new ThreadOptions
        {
            Model = ConfiguredDefaultModel,
        });

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IChatClient>();
        var metadata = client.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata;

        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.DefaultModelId).IsEqualTo(ConfiguredDefaultModel);
    }

    [Test]
    public async Task AddKeyedClaudeChatClient_RegistersWithKey()
    {
        var services = new ServiceCollection();
        services.AddKeyedClaudeChatClient("claude");
        var provider = services.BuildServiceProvider();
        var client = provider.GetKeyedService<IChatClient>("claude");
        await Assert.That(client).IsNotNull();
    }

    [Test]
    public async Task AddKeyedClaudeChatClient_WithConfiguration_AppliesConfiguredDefaultModel()
    {
        var services = new ServiceCollection();
        services.AddKeyedClaudeChatClient("claude", options => options.DefaultModel = ConfiguredDefaultModel);
        var provider = services.BuildServiceProvider();
        var client = provider.GetKeyedService<IChatClient>("claude");

        await Assert.That(client).IsNotNull();

        var metadata = client!.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata;
        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.DefaultModelId).IsEqualTo(ConfiguredDefaultModel);
    }
}
