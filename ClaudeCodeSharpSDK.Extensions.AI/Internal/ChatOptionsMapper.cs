using System.Globalization;
using System.Text.Json;
using ManagedCode.ClaudeCodeSharpSDK.Client;
using Microsoft.Extensions.AI;

namespace ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Internal;

internal static class ChatOptionsMapper
{
    internal const string WorkingDirectoryKey = "claude:working_directory";
    internal const string PermissionModeKey = "claude:permission_mode";
    internal const string AllowedToolsKey = "claude:allowed_tools";
    internal const string DisallowedToolsKey = "claude:disallowed_tools";
    internal const string SystemPromptKey = "claude:system_prompt";
    internal const string AppendSystemPromptKey = "claude:append_system_prompt";
    internal const string MaxBudgetUsdKey = "claude:max_budget_usd";

    internal static ThreadOptions ToThreadOptions(ChatOptions? chatOptions, ClaudeChatClientOptions clientOptions)
    {
        var defaults = clientOptions.DefaultThreadOptions ?? new ThreadOptions();

        var model = chatOptions?.ModelId ?? clientOptions.DefaultModel ?? defaults.Model;
        var workingDirectory = defaults.WorkingDirectory;
        var permissionMode = defaults.PermissionMode;
        var allowedTools = defaults.AllowedTools;
        var disallowedTools = defaults.DisallowedTools;
        var systemPrompt = defaults.SystemPrompt;
        var appendSystemPrompt = defaults.AppendSystemPrompt;
        var maxBudgetUsd = defaults.MaxBudgetUsd;

        if (chatOptions?.AdditionalProperties is { } props)
        {
            if (props.TryGetValue(WorkingDirectoryKey, out var value) && TryGetString(value, out var cwd))
            {
                workingDirectory = cwd;
            }
            else if (props.ContainsKey(WorkingDirectoryKey))
            {
                throw CreateInvalidValueException(WorkingDirectoryKey);
            }

            if (props.TryGetValue(PermissionModeKey, out value) && TryGetPermissionMode(value, out var mappedPermissionMode))
            {
                permissionMode = mappedPermissionMode;
            }
            else if (props.ContainsKey(PermissionModeKey))
            {
                throw CreateInvalidValueException(PermissionModeKey);
            }

            if (props.TryGetValue(AllowedToolsKey, out value) && TryGetStringList(value, out var allowed))
            {
                allowedTools = allowed;
            }
            else if (props.ContainsKey(AllowedToolsKey))
            {
                throw CreateInvalidValueException(AllowedToolsKey);
            }

            if (props.TryGetValue(DisallowedToolsKey, out value) && TryGetStringList(value, out var disallowed))
            {
                disallowedTools = disallowed;
            }
            else if (props.ContainsKey(DisallowedToolsKey))
            {
                throw CreateInvalidValueException(DisallowedToolsKey);
            }

            if (props.TryGetValue(SystemPromptKey, out value) && TryGetString(value, out var system))
            {
                systemPrompt = system;
            }
            else if (props.ContainsKey(SystemPromptKey))
            {
                throw CreateInvalidValueException(SystemPromptKey);
            }

            if (props.TryGetValue(AppendSystemPromptKey, out value) && TryGetString(value, out var append))
            {
                appendSystemPrompt = append;
            }
            else if (props.ContainsKey(AppendSystemPromptKey))
            {
                throw CreateInvalidValueException(AppendSystemPromptKey);
            }

            if (props.TryGetValue(MaxBudgetUsdKey, out value) && TryGetDecimal(value, out var budget))
            {
                maxBudgetUsd = budget;
            }
            else if (props.ContainsKey(MaxBudgetUsdKey))
            {
                throw CreateInvalidValueException(MaxBudgetUsdKey);
            }
        }

        return defaults with
        {
            Model = model,
            WorkingDirectory = workingDirectory,
            PermissionMode = permissionMode,
            AllowedTools = allowedTools,
            DisallowedTools = disallowedTools,
            SystemPrompt = systemPrompt,
            AppendSystemPrompt = appendSystemPrompt,
            MaxBudgetUsd = maxBudgetUsd,
        };
    }

    internal static TurnOptions ToTurnOptions(ChatOptions? chatOptions, CancellationToken cancellationToken)
    {
        _ = chatOptions;
        return new TurnOptions
        {
            CancellationToken = cancellationToken,
        };
    }

    private static bool TryGetString(object? value, out string result)
    {
        switch (value)
        {
            case string text when !string.IsNullOrWhiteSpace(text):
                result = text;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } jsonString:
                var parsed = jsonString.GetString();
                if (!string.IsNullOrWhiteSpace(parsed))
                {
                    result = parsed;
                    return true;
                }

                break;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryGetPermissionMode(object? value, out PermissionMode permissionMode)
    {
        switch (value)
        {
            case PermissionMode mapped:
                permissionMode = mapped;
                return true;
            case string text when Enum.TryParse<PermissionMode>(text, ignoreCase: true, out var parsedFromString):
                permissionMode = parsedFromString;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } jsonString
                when Enum.TryParse<PermissionMode>(jsonString.GetString(), ignoreCase: true, out var parsedFromJson):
                permissionMode = parsedFromJson;
                return true;
            default:
                permissionMode = default;
                return false;
        }
    }

    private static bool TryGetStringList(object? value, out IReadOnlyList<string> items)
    {
        switch (value)
        {
            case string text:
                items = text
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(static item => !string.IsNullOrWhiteSpace(item))
                    .ToArray();
                return true;
            case IReadOnlyList<string> list:
                items = list.Where(static item => !string.IsNullOrWhiteSpace(item)).ToArray();
                return true;
            case IEnumerable<string> enumerable:
                items = enumerable.Where(static item => !string.IsNullOrWhiteSpace(item)).ToArray();
                return true;
            case JsonElement { ValueKind: JsonValueKind.Array } array:
                items = array.EnumerateArray()
                    .Where(static item => item.ValueKind == JsonValueKind.String)
                    .Select(static item => item.GetString())
                    .Where(static item => !string.IsNullOrWhiteSpace(item))
                    .Cast<string>()
                    .ToArray();
                return true;
            default:
                items = Array.Empty<string>();
                return false;
        }
    }

    private static bool TryGetDecimal(object? value, out decimal result)
    {
        switch (value)
        {
            case decimal decimalValue:
                result = decimalValue;
                return true;
            case byte byteValue:
                result = byteValue;
                return true;
            case short shortValue:
                result = shortValue;
                return true;
            case int intValue:
                result = intValue;
                return true;
            case long longValue:
                result = longValue;
                return true;
            case float floatValue:
                result = (decimal)floatValue;
                return true;
            case double doubleValue:
                result = (decimal)doubleValue;
                return true;
            case string text when decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed):
                result = parsed;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Number } jsonNumber when jsonNumber.TryGetDecimal(out var jsonParsed):
                result = jsonParsed;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } jsonString
                when decimal.TryParse(jsonString.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var jsonStringParsed):
                result = jsonStringParsed;
                return true;
            default:
                result = default;
                return false;
        }
    }

    private static InvalidOperationException CreateInvalidValueException(string key)
    {
        return new InvalidOperationException($"Invalid value for Claude chat option '{key}'.");
    }
}
