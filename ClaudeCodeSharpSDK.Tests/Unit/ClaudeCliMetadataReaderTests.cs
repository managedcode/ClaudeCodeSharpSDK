using ManagedCode.ClaudeCodeSharpSDK.Internal;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Unit;

public class ClaudeCliMetadataReaderTests
{
    [Test]
    public async Task ParseInstalledVersion_ReturnsFirstTokenForClaudeCodeOutput()
    {
        var parsed = ClaudeCliMetadataReader.ParseInstalledVersion("2.0.75 (Claude Code)");

        await Assert.That(parsed).IsEqualTo("2.0.75");
    }

    [Test]
    public async Task ParseLatestPublishedVersion_PicksHighestGitTag()
    {
        const string gitOutput = """
                                 deadbeef	refs/tags/v2.0.74
                                 feedface	refs/tags/v2.0.75-beta.1
                                 cafebabe	refs/tags/v2.0.75
                                 """;

        var parsed = ClaudeCliMetadataReader.ParseLatestPublishedVersion(gitOutput);

        await Assert.That(parsed).IsEqualTo("2.0.75");
    }

    [Test]
    public async Task IsNewerVersion_TreatsStableAsNotOlderThanMatchingPrerelease()
    {
        var isNewer = ClaudeCliMetadataReader.IsNewerVersion("2.0.75-beta.1", "2.0.75");

        await Assert.That(isNewer).IsFalse();
    }

    [Test]
    public async Task IsNewerVersion_UsesNumericSemVerComparisonForPrereleaseIdentifiers()
    {
        var isNewer = ClaudeCliMetadataReader.IsNewerVersion("2.0.75-beta.10", "2.0.75-beta.2");

        await Assert.That(isNewer).IsTrue();
    }

    [Test]
    public async Task ParseLatestPublishedVersion_UsesNumericSemVerComparisonForPrereleaseIdentifiers()
    {
        const string gitOutput = """
                                 deadbeef	refs/tags/v2.0.75-beta.2
                                 feedface	refs/tags/v2.0.75-beta.10
                                 """;

        var parsed = ClaudeCliMetadataReader.ParseLatestPublishedVersion(gitOutput);

        await Assert.That(parsed).IsEqualTo("2.0.75-beta.10");
    }

    [Test]
    public async Task ParseDefaultModelFromJson_ReadsModelProperty()
    {
        var parsed = ClaudeCliMetadataReader.ParseDefaultModelFromJson("""{"model":"claude-opus-4-5","statusLine":{"enabled":true}}""");

        await Assert.That(parsed).IsEqualTo("claude-opus-4-5");
    }

    [Test]
    public async Task TryParseSemanticVersion_RejectsInvalidText()
    {
        var parsed = ClaudeCliMetadataReader.TryParseSemanticVersion("not-a-version", out _);

        await Assert.That(parsed).IsFalse();
    }
}
