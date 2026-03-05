using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Configuration;
using ManagedCode.ClaudeCodeSharpSDK.Execution;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Unit;

public class ClaudeClientTests
{
    [Test]
    public async Task StartThread_WithAutoStartDisabled_Throws()
    {
        using var client = new ClaudeClient(
            new ClaudeClientOptions
            {
                AutoStart = false,
                ClaudeOptions = new ClaudeOptions { ClaudeExecutablePath = "claude" },
            },
            exec: null);

        var exception = await Assert.That(() =>
        {
            client.StartThread();
        }).ThrowsException();
        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("Call StartAsync first");
    }

    [Test]
    public async Task StartStopAndDispose_TrackConnectionState()
    {
        using var client = new ClaudeClient(
            new ClaudeClientOptions
            {
                AutoStart = true,
                ClaudeOptions = new ClaudeOptions { ClaudeExecutablePath = "claude" },
            },
            exec: null);

        await Assert.That(client.State).IsEqualTo(ClaudeClientState.Disconnected);

        await client.StartAsync();
        await Assert.That(client.State).IsEqualTo(ClaudeClientState.Connected);

        await client.StopAsync();
        await Assert.That(client.State).IsEqualTo(ClaudeClientState.Disconnected);

        client.Dispose();
        await Assert.That(client.State).IsEqualTo(ClaudeClientState.Disposed);
    }

    [Test]
    public async Task ResumeThread_SeedsExistingThreadId()
    {
        var exec = new ClaudeExec("claude");
        using var client = new ClaudeClient(
            new ClaudeClientOptions
            {
                AutoStart = true,
                ClaudeOptions = new ClaudeOptions { ClaudeExecutablePath = "claude" },
            },
            exec);

        using var thread = client.ResumeThread("session-42");

        await Assert.That(thread.Id).IsEqualTo("session-42");
    }
}
