using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Configuration;
using ManagedCode.ClaudeCodeSharpSDK.Execution;
using ManagedCode.ClaudeCodeSharpSDK.Tests.Shared;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Unit;

public class ClaudeClientTests
{
    private const string CallStartAsyncFirstMessageFragment = "Call StartAsync first";
    private const string ExistingSessionId = "session-42";

    [Test]
    public async Task StartThread_WithAutoStartDisabled_Throws()
    {
        using var client = new ClaudeClient(
            new ClaudeClientOptions
            {
                AutoStart = false,
                ClaudeOptions = new ClaudeOptions { ClaudeExecutablePath = TestConstants.ClaudeExecutablePath },
            },
            exec: null);

        var exception = await Assert.That(() =>
        {
            client.StartThread();
        }).ThrowsException();
        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains(CallStartAsyncFirstMessageFragment);
    }

    [Test]
    public async Task StartStopAndDispose_TrackConnectionState()
    {
        using var client = new ClaudeClient(
            new ClaudeClientOptions
            {
                AutoStart = true,
                ClaudeOptions = new ClaudeOptions { ClaudeExecutablePath = TestConstants.ClaudeExecutablePath },
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
        var exec = new ClaudeExec(TestConstants.ClaudeExecutablePath);
        using var client = new ClaudeClient(
            new ClaudeClientOptions
            {
                AutoStart = true,
                ClaudeOptions = new ClaudeOptions { ClaudeExecutablePath = TestConstants.ClaudeExecutablePath },
            },
            exec);

        using var thread = client.ResumeThread(ExistingSessionId);

        await Assert.That(thread.Id).IsEqualTo(ExistingSessionId);
    }
}
