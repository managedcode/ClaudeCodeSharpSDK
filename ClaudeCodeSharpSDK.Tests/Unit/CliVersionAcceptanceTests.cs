using ManagedCode.ClaudeCodeSharpSDK.Internal;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Unit;

public sealed class CliVersionAcceptanceTests
{
    private const string VersionOutputSuffix = " (Claude Code)";
    private const string OlderVersion = "2.1.292";
    private const string CurrentVersion = "2.1.301";
    private const string FutureVersion = "9999.0.1-preview.1";

    [Test]
    [Arguments(OlderVersion)]
    [Arguments(CurrentVersion)]
    [Arguments(FutureVersion)]
    public async Task InstalledVersionDoesNotNeedToMatchTheSdkTarget(string version)
    {
        var output = string.Concat(version, VersionOutputSuffix);
        var observed = ClaudeCliMetadataReader.ParseInstalledVersion(output);

        await Assert.That(observed).IsEqualTo(version);
    }
}
