using System.Runtime.CompilerServices;
using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Internal;
using Microsoft.Extensions.AI;

namespace ManagedCode.ClaudeCodeSharpSDK.Extensions.AI;

public sealed class ClaudeChatClient : IChatClient
{
    private const string ProviderName = "ClaudeCodeCLI";

    private readonly ClaudeClient _client;
    private readonly ClaudeChatClientOptions _options;

    public ClaudeChatClient(ClaudeChatClientOptions? options = null)
    {
        _options = options ?? new ClaudeChatClientOptions();
        _client = new ClaudeClient(new ClaudeClientOptions
        {
            ClaudeOptions = _options.ClaudeOptions,
            AutoStart = true,
        });
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var prompt = ChatMessageMapper.ToClaudeInput(messages);
        var threadOptions = ChatOptionsMapper.ToThreadOptions(options, _options);
        var turnOptions = ChatOptionsMapper.ToTurnOptions(options, cancellationToken);

        var thread = options?.ConversationId is { } threadId
            ? _client.ResumeThread(threadId, threadOptions)
            : _client.StartThread(threadOptions);

        using (thread)
        {
            var result = await thread.RunAsync(prompt, turnOptions).ConfigureAwait(false);
            return ChatResponseMapper.ToChatResponse(result, thread.Id);
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var prompt = ChatMessageMapper.ToClaudeInput(messages);
        var threadOptions = ChatOptionsMapper.ToThreadOptions(options, _options);
        var turnOptions = ChatOptionsMapper.ToTurnOptions(options, cancellationToken);

        var thread = options?.ConversationId is { } threadId
            ? _client.ResumeThread(threadId, threadOptions)
            : _client.StartThread(threadOptions);

        using (thread)
        {
            var streamed = await thread.RunStreamedAsync(prompt, turnOptions).ConfigureAwait(false);
            await foreach (var update in StreamingEventMapper.ToUpdates(streamed.Events, cancellationToken)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                yield return update;
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey is not null)
        {
            return null;
        }

        if (serviceType == typeof(ChatClientMetadata))
        {
            return new ChatClientMetadata(
                providerName: ProviderName,
                providerUri: null,
                defaultModelId: _options.DefaultModel ?? _options.DefaultThreadOptions?.Model);
        }

        if (serviceType.IsInstanceOfType(this))
        {
            return this;
        }

        return null;
    }

    public void Dispose() => _client.Dispose();
}
