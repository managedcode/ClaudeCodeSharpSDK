using ManagedCode.ClaudeCodeSharpSDK.Configuration;
using ManagedCode.ClaudeCodeSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.ClaudeCodeSharpSDK.Extensions.AI.Tests;

public sealed class SharedChatClientConcurrencyTests
{
    private const string FixtureFileName = "shared-client-concurrency.js";
    private const string TestDirectoryName = "tests";
    private const string SandboxDirectoryName = ".sandbox";
    private const string ActiveMarkerSearchPattern = "*.active";
    private const string GuidFormat = "N";
    private const string MarkerDirectoryEnvironmentName = "CLAUDE_SHARED_CLIENT_MARKERS";
    private const string FirstPrompt = "independent fresh chat alpha";
    private const string SecondPrompt = "independent fresh chat beta";
    private const string ModelPlaceholder = "CLAUDE_MODEL";
    private const string NodeScript = """
        const fs = require('node:fs');
        const path = require('node:path');
        const markerDirectory = process.env.CLAUDE_SHARED_CLIENT_MARKERS;
        const markerPath = path.join(markerDirectory, `${process.pid}.active`);
        fs.writeFileSync(markerPath, 'active');
        const input = fs.readFileSync(0, 'utf8');
        const startedAt = Date.now();
        const waitCell = new Int32Array(new SharedArrayBuffer(4));
        while (Date.now() - startedAt < 5000) {
            if (fs.readdirSync(markerDirectory).filter(name => name.endsWith('.active')).length >= 2) break;
            Atomics.wait(waitCell, 0, 0, 20);
        }
        const activeCount = fs.readdirSync(markerDirectory).filter(name => name.endsWith('.active')).length;
        if (activeCount < 2) process.exit(31);
        const sessionId = String(process.pid);
        console.log(JSON.stringify({type:'system',subtype:'init',session_id:sessionId,cwd:process.cwd()}));
        console.log(JSON.stringify({type:'assistant',message:{id:'assistant-message',model:'CLAUDE_MODEL',role:'assistant',stop_reason:'end_turn',type:'message',usage:{input_tokens:1,cache_creation_input_tokens:0,cache_read_input_tokens:0,output_tokens:1},content:[{type:'text',text:input}]},session_id:sessionId,uuid:'assistant-event'}));
        console.log(JSON.stringify({type:'result',is_error:false,result:input}));
        """;

    [Test]
    public async Task SharedClient_ConcurrentCallsUseIndependentFreshChats()
    {
        var directory = CreateSandboxDirectory();
        var scriptPath = Path.Combine(directory, FixtureFileName);
        await File.WriteAllTextAsync(scriptPath, NodeScript.Replace(ModelPlaceholder,
            ClaudeModels.ClaudeSonnet45Alias, StringComparison.Ordinal));

        try
        {
            using var client = new ClaudeChatClient(new ClaudeChatClientOptions
            {
                ClaudeOptions = new ClaudeOptions
                {
                    ClaudeExecutablePath = scriptPath,
                    InheritEnvironmentVariables = true,
                    EnvironmentVariables = new Dictionary<string, string>
                    {
                        [MarkerDirectoryEnvironmentName] = directory,
                    },
                },
            });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            var responses = await Task.WhenAll(
                CollectUpdatesAsync(client, FirstPrompt, timeout.Token),
                CollectUpdatesAsync(client, SecondPrompt, timeout.Token));

            await Assert.That(ReadText(responses[0])).IsEqualTo(FirstPrompt);
            await Assert.That(ReadText(responses[1])).IsEqualTo(SecondPrompt);
            await Assert.That(ReadConversationId(responses[0])).IsNotEqualTo(ReadConversationId(responses[1]));
            await Assert.That(Directory.GetFiles(directory, ActiveMarkerSearchPattern)).Count().IsEqualTo(2);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string ReadText(IEnumerable<ChatResponseUpdate> updates) => updates
        .SelectMany(static update => update.Contents)
        .OfType<TextContent>()
        .Single().Text;

    private static async Task<List<ChatResponseUpdate>> CollectUpdatesAsync(
        ClaudeChatClient client,
        string prompt,
        CancellationToken cancellationToken)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, prompt)], cancellationToken: cancellationToken))
        {
            updates.Add(update);
        }

        return updates;
    }

    private static string? ReadConversationId(IEnumerable<ChatResponseUpdate> updates) =>
        updates.Select(static update => update.ConversationId).FirstOrDefault(static id => id is not null);

    private static string CreateSandboxDirectory()
    {
        var directory = Path.Combine(Environment.CurrentDirectory, TestDirectoryName, SandboxDirectoryName,
            Guid.NewGuid().ToString(GuidFormat));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
