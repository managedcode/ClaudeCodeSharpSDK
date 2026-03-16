using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Models;
using ManagedCode.ClaudeCodeSharpSDK.Tests.Shared;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Integration;

[Property(TestConstants.RequiresClaudeAuthPropertyName, TestConstants.TrueString)]
[RequiresAuthenticatedClaude]
public class RealClaudeIntegrationTests
{
    private const string AgainStatusOnlyPrompt = "Again: reply with a JSON object where status is exactly \"ok\".";
    private const string PlainTextOkPrompt = "Reply with short plain text: ok.";
    private const string StatusOnlyPrompt = "Reply with a JSON object where status is exactly \"ok\".";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan TwoTurnTimeout = TimeSpan.FromMinutes(3);

    [Test]
    public async Task RunAsync_WithRealClaudeCli_ReturnsStructuredOutput()
    {
        var settings = RealClaudeTestSupport.GetRequiredSettings();

        using var client = RealClaudeTestSupport.CreateClient();
        using var thread = StartRealIntegrationThread(client, settings.Model);
        using var cancellation = new CancellationTokenSource(TestTimeout);
        var schema = IntegrationOutputSchemas.StatusOnly();

        var result = await thread.RunAsync<StatusResponse>(
            StatusOnlyPrompt,
            schema,
            IntegrationOutputJsonContext.Default.StatusResponse,
            cancellation.Token);

        await Assert.That(result.TypedResponse.Status).IsEqualTo(TestConstants.OkStatusValue);
        await Assert.That(result.Usage).IsNotNull();
    }

    [Test]
    public async Task RunStreamedAsync_WithRealClaudeCli_YieldsCompletedTurnEvent()
    {
        var settings = RealClaudeTestSupport.GetRequiredSettings();

        using var client = RealClaudeTestSupport.CreateClient();
        using var thread = StartRealIntegrationThread(client, settings.Model);
        using var cancellation = new CancellationTokenSource(TestTimeout);

        var streamed = await thread.RunStreamedAsync(
            PlainTextOkPrompt,
            new TurnOptions { CancellationToken = cancellation.Token });

        var hasTurnCompleted = false;
        var hasTurnFailed = false;
        var hasCompletedItem = false;

        await foreach (var threadEvent in streamed.Events.WithCancellation(cancellation.Token))
        {
            hasTurnCompleted |= threadEvent is TurnCompletedEvent;
            hasTurnFailed |= threadEvent is TurnFailedEvent;
            hasCompletedItem |= threadEvent is ItemCompletedEvent;
        }

        await Assert.That(hasCompletedItem).IsTrue();
        await Assert.That(hasTurnCompleted).IsTrue();
        await Assert.That(hasTurnFailed).IsFalse();
        await Assert.That(thread.Id).IsNotNull();
    }

    [Test]
    public async Task RunAsync_WithRealClaudeCli_SecondTurnKeepsThreadId()
    {
        var settings = RealClaudeTestSupport.GetRequiredSettings();

        using var client = RealClaudeTestSupport.CreateClient();
        using var thread = StartRealIntegrationThread(client, settings.Model);
        using var cancellation = new CancellationTokenSource(TwoTurnTimeout);
        var schema = IntegrationOutputSchemas.StatusOnly();

        var first = await thread.RunAsync<StatusResponse>(
            StatusOnlyPrompt,
            schema,
            IntegrationOutputJsonContext.Default.StatusResponse,
            cancellation.Token);

        var firstThreadId = thread.Id;
        await Assert.That(firstThreadId).IsNotNull();
        await Assert.That(first.Usage).IsNotNull();

        var second = await thread.RunAsync<StatusResponse>(
            AgainStatusOnlyPrompt,
            schema,
            IntegrationOutputJsonContext.Default.StatusResponse,
            cancellation.Token);

        await Assert.That(second.TypedResponse.Status).IsEqualTo(TestConstants.OkStatusValue);
        await Assert.That(second.Usage).IsNotNull();
        await Assert.That(thread.Id).IsEqualTo(firstThreadId);
    }

    private static ClaudeThread StartRealIntegrationThread(ClaudeClient client, string model)
    {
        return client.StartThread(new ThreadOptions
        {
            Model = model,
            DangerouslySkipPermissions = true,
        });
    }
}
