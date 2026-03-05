using System.Text.Json.Serialization;
using ManagedCode.ClaudeCodeSharpSDK.Client;

namespace ManagedCode.ClaudeCodeSharpSDK.Internal;

[JsonSerializable(typeof(Dictionary<string, InlineAgentDefinition>))]
internal sealed partial class ClaudeJsonSerializerContext : JsonSerializerContext;
