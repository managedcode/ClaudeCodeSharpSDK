using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Extensions;

public static class ClaudeServiceCollectionExtensions
{
    public static IServiceCollection AddClaudeChatClient(
        this IServiceCollection services,
        Action<ClaudeChatClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new ClaudeChatClientOptions();
        configure?.Invoke(options);
        services.AddSingleton<IChatClient>(new ClaudeChatClient(options));
        return services;
    }

    public static IServiceCollection AddKeyedClaudeChatClient(
        this IServiceCollection services,
        object serviceKey,
        Action<ClaudeChatClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceKey);

        var options = new ClaudeChatClientOptions();
        configure?.Invoke(options);
        services.AddKeyedSingleton<IChatClient>(serviceKey, new ClaudeChatClient(options));
        return services;
    }
}
