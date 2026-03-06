using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.ClaudeCodeSharpSDK.Models;

namespace ManagedCode.ClaudeCodeSharpSDK.Internal;

internal static class ThreadEventParser
{
    private const string ErrorEventDefaultMessage = "Claude Code emitted an error event.";
    private const string ParagraphSeparator = "\n\n";
    private const string MissingRequiredPropertyMessagePrefix = "Missing required property";
    private const string MissingRequiredStringPropertyMessagePrefix = "Missing required string property";
    private const string Space = " ";
    private const string MessageQuote = "'";
    private const string MessageSuffix = ".";

    public static ThreadEvent Parse(string line)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(line);

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        var type = GetRequiredString(root, ClaudeProtocolConstants.Properties.Type);

        return type switch
        {
            ClaudeProtocolConstants.EventTypes.System => ParseSystem(root),
            ClaudeProtocolConstants.EventTypes.Assistant => new ItemCompletedEvent(ParseAssistantMessage(root)),
            ClaudeProtocolConstants.EventTypes.User => new ItemCompletedEvent(ParseUserMessage(root)),
            ClaudeProtocolConstants.EventTypes.Result => ParseResult(root),
            ClaudeProtocolConstants.EventTypes.Error => new ThreadErrorEvent(GetOptionalString(root, ClaudeProtocolConstants.Properties.Error)
                                                                            ?? GetOptionalString(root, ClaudeProtocolConstants.Properties.Result)
                                                                            ?? ErrorEventDefaultMessage),
            _ => new UnknownEvent(type, JsonNode.Parse(root.GetRawText()) ?? new JsonObject()),
        };
    }

    private static ThreadEvent ParseSystem(JsonElement root)
    {
        var subtype = GetOptionalString(root, ClaudeProtocolConstants.Properties.Subtype);
        if (!string.Equals(subtype, ClaudeProtocolConstants.Subtypes.Init, StringComparison.Ordinal))
        {
            return new UnknownEvent(ClaudeProtocolConstants.EventTypes.System, JsonNode.Parse(root.GetRawText()) ?? new JsonObject());
        }

        return new ThreadStartedEvent(new SessionInfo(
            GetRequiredString(root, ClaudeProtocolConstants.Properties.SessionId),
            GetRequiredString(root, ClaudeProtocolConstants.Properties.Cwd),
            GetOptionalString(root, ClaudeProtocolConstants.Properties.Model),
            GetOptionalString(root, ClaudeProtocolConstants.Properties.PermissionMode),
            GetOptionalString(root, ClaudeProtocolConstants.Properties.ClaudeCodeVersion),
            GetOptionalString(root, ClaudeProtocolConstants.Properties.OutputStyle),
            GetOptionalString(root, ClaudeProtocolConstants.Properties.ApiKeySource),
            ParseStringArray(root, ClaudeProtocolConstants.Properties.Tools),
            ParseStringArray(root, ClaudeProtocolConstants.Properties.McpServers),
            ParseStringArray(root, ClaudeProtocolConstants.Properties.SlashCommands),
            ParseStringArray(root, ClaudeProtocolConstants.Properties.Agents),
            ParseStringArray(root, ClaudeProtocolConstants.Properties.Skills),
            ParseStringArray(root, ClaudeProtocolConstants.Properties.Plugins)));
    }

    private static ThreadEvent ParseResult(JsonElement root)
    {
        var usage = ParseOptionalUsage(root, ClaudeProtocolConstants.Properties.Usage);
        var totalCost = GetOptionalDouble(root, ClaudeProtocolConstants.Properties.TotalCostUsd);
        var durationMs = GetOptionalInt32(root, ClaudeProtocolConstants.Properties.DurationMs);
        var durationApiMs = GetOptionalInt32(root, ClaudeProtocolConstants.Properties.DurationApiMs);
        var numTurns = GetOptionalInt32(root, ClaudeProtocolConstants.Properties.NumTurns);
        var resultText = GetOptionalString(root, ClaudeProtocolConstants.Properties.Result) ?? string.Empty;
        var isError = GetOptionalBoolean(root, ClaudeProtocolConstants.Properties.IsError) ?? false;

        if (isError)
        {
            return new TurnFailedEvent(
                new ThreadError(resultText),
                usage,
                totalCost,
                durationMs,
                durationApiMs,
                numTurns);
        }

        return new TurnCompletedEvent(
            usage ?? new Usage(0, 0, 0, 0),
            resultText,
            totalCost,
            durationMs,
            durationApiMs,
            numTurns);
    }

    private static AssistantMessageItem ParseAssistantMessage(JsonElement root)
    {
        var message = GetRequiredProperty(root, ClaudeProtocolConstants.Properties.Message);
        var content = ParseContent(message);
        return new AssistantMessageItem(
            GetOptionalString(message, ClaudeProtocolConstants.Properties.Id)
            ?? GetRequiredString(root, ClaudeProtocolConstants.Properties.Uuid),
            GetOptionalString(message, ClaudeProtocolConstants.Properties.Model) ?? string.Empty,
            ExtractText(content),
            content,
            ParseOptionalUsage(message, ClaudeProtocolConstants.Properties.Usage),
            GetOptionalString(message, ClaudeProtocolConstants.Properties.StopReason),
            GetOptionalString(root, ClaudeProtocolConstants.Properties.Error));
    }

    private static UserMessageItem ParseUserMessage(JsonElement root)
    {
        var message = GetRequiredProperty(root, ClaudeProtocolConstants.Properties.Message);
        var content = ParseContent(message);
        return new UserMessageItem(
            GetOptionalString(message, ClaudeProtocolConstants.Properties.Id)
            ?? GetRequiredString(root, ClaudeProtocolConstants.Properties.Uuid),
            ExtractText(content),
            content);
    }

    private static Usage? ParseOptionalUsage(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var usageElement)
            || usageElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return new Usage(
            GetOptionalInt32(usageElement, ClaudeProtocolConstants.Properties.InputTokens) ?? 0,
            GetOptionalInt32(usageElement, ClaudeProtocolConstants.Properties.CacheCreationInputTokens) ?? 0,
            GetOptionalInt32(usageElement, ClaudeProtocolConstants.Properties.CacheReadInputTokens) ?? 0,
            GetOptionalInt32(usageElement, ClaudeProtocolConstants.Properties.OutputTokens) ?? 0);
    }

    private static List<MessageContentBlock> ParseContent(JsonElement message)
    {
        if (!message.TryGetProperty(ClaudeProtocolConstants.Properties.Content, out var contentElement)
            || contentElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var blocks = new List<MessageContentBlock>();
        foreach (var block in contentElement.EnumerateArray())
        {
            var raw = JsonNode.Parse(block.GetRawText());
            blocks.Add(new MessageContentBlock(
                GetRequiredString(block, ClaudeProtocolConstants.Properties.Type),
                GetOptionalString(block, ClaudeProtocolConstants.Properties.Text),
                GetOptionalString(block, ClaudeProtocolConstants.Properties.Id),
                GetOptionalString(block, ClaudeProtocolConstants.Properties.Name),
                GetOptionalString(block, ClaudeProtocolConstants.Properties.ToolUseId),
                GetOptionalBoolean(block, ClaudeProtocolConstants.Properties.IsError),
                ParseOptionalNode(block, ClaudeProtocolConstants.Properties.Input),
                raw));
        }

        return blocks;
    }

    private static string ExtractText(IReadOnlyList<MessageContentBlock> content)
    {
        return string.Join(
            ParagraphSeparator,
            content.Where(static block => !string.IsNullOrWhiteSpace(block.Text))
                .Select(static block => block.Text));
    }

    private static List<string> ParseStringArray(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var propertyElement)
            || propertyElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var item in propertyElement.EnumerateArray())
        {
            result.Add(item.ValueKind == JsonValueKind.String
                ? item.GetString() ?? string.Empty
                : item.GetRawText());
        }

        return result;
    }

    private static JsonElement GetRequiredProperty(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var result))
        {
            throw new InvalidOperationException(
                string.Concat(MissingRequiredPropertyMessagePrefix, Space, MessageQuote, property, MessageQuote, MessageSuffix));
        }

        return result;
    }

    private static string GetRequiredString(JsonElement element, string property)
    {
        var value = GetOptionalString(element, property);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                string.Concat(MissingRequiredStringPropertyMessagePrefix, Space, MessageQuote, property, MessageQuote, MessageSuffix));
        }

        return value;
    }

    private static string? GetOptionalString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? GetOptionalInt32(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result)
            ? result
            : null;
    }

    private static double? GetOptionalDouble(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.TryGetDouble(out var result)
            ? result
            : null;
    }

    private static bool? GetOptionalBoolean(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }

    private static JsonNode? ParseOptionalNode(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return JsonNode.Parse(value.GetRawText());
    }
}
