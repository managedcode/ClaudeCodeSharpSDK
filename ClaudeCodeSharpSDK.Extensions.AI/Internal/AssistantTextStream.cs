using System.Text;

namespace ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Internal;

/// <summary>Reconciles native text blocks; Claude emits assistant snapshots between blocks of the same message.</summary>
internal sealed class AssistantTextStream
{
    private readonly Dictionary<string, Dictionary<int, StringBuilder>> _messages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _indices = new(StringComparer.Ordinal);
    public string? MessageId { get; private set; }

    public void Start(string messageId)
    {
        MessageId = messageId;
        _messages.TryAdd(messageId, []);
    }

    public string Append(int contentIndex, string text)
    {
        if (MessageId is not { } id)
        {
            throw new InvalidDataException("Claude emitted text outside an active assistant message.");
        }
        var blocks = _messages[id];
        var separator = string.Empty;
        if (!blocks.TryGetValue(contentIndex, out var block))
        {
            separator = blocks.Values.Any(static value => value.Length > 0) ? "\n\n" : string.Empty;
            block = new StringBuilder();
            blocks.Add(contentIndex, block);
        }
        _indices[id] = contentIndex;
        block.Append(text);
        return separator + text;
    }

    public string Complete(string messageId, string snapshot)
    {
        if (snapshot.Length == 0)
        {
            return string.Empty;
        }
        if (!_messages.TryGetValue(messageId, out var blocks) || !_indices.TryGetValue(messageId, out var index))
        {
            return snapshot;
        }
        var block = blocks[index];
        var emitted = block.ToString();
        if (!snapshot.StartsWith(emitted, StringComparison.Ordinal))
        {
            emitted = string.Join("\n\n", blocks.OrderBy(static pair => pair.Key).Select(static pair => pair.Value.ToString()));
        }
        if (!snapshot.StartsWith(emitted, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Claude completed assistant text differs from its streamed text blocks.");
        }
        var suffix = snapshot[emitted.Length..];
        block.Append(suffix);
        return suffix;
    }
}
