using ManagedCode.ClaudeCodeSharpSDK.Client;
using ManagedCode.ClaudeCodeSharpSDK.Tests.Shared;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Integration;

[Property("RequiresClaudeAuth", "true")]
[RequiresAuthenticatedClaude]
public class RealClaudeIntegrationTests
{
    [Test]
    public async Task RealClaude_RunAsync_WhenAuthenticated_ReturnsResponse()
    {
        var (client, settings) = RealClaudeTestSupport.CreateAuthenticatedClient();

        using (client)
        {
            using var thread = client.StartThread(new ThreadOptions
            {
                Model = settings.Model,
                DangerouslySkipPermissions = true,
                NoSessionPersistence = true,
            });

            var result = await thread.RunAsync("Reply with OK only.");

            await Assert.That(string.IsNullOrWhiteSpace(result.FinalResponse)).IsFalse();
        }
    }
}
