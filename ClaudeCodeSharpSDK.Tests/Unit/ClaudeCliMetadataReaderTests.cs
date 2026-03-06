using ManagedCode.ClaudeCodeSharpSDK.Internal;
using ManagedCode.ClaudeCodeSharpSDK.Models;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Unit;

public class ClaudeCliMetadataReaderTests
{
    private const string DefaultModelJsonTemplate = "{\"model\":\"__MODEL__\",\"statusLine\":{\"enabled\":true}}";
    private const string HighestStableGitOutput = "deadbeef\trefs/tags/v2.0.74\nfeedface\trefs/tags/v2.0.75-beta.1\ncafebabe\trefs/tags/v2.0.75";
    private const string InstalledVersionOutput = "2.0.75 (Claude Code)";
    private const string InvalidVersionText = "not-a-version";
    private const string ModelPlaceholder = "__MODEL__";
    private const string NumericPrereleaseGitOutput = "deadbeef\trefs/tags/v2.0.75-beta.2\nfeedface\trefs/tags/v2.0.75-beta.10";
    private const string PrereleaseTenVersion = "2.0.75-beta.10";
    private const string PrereleaseTwoVersion = "2.0.75-beta.2";
    private const string StableVersion = "2.0.75";
    private const string StableVsPrereleaseVersion = "2.0.75-beta.1";

    [Test]
    public async Task ParseInstalledVersion_ReturnsFirstTokenForClaudeCodeOutput()
    {
        var parsed = ClaudeCliMetadataReader.ParseInstalledVersion(InstalledVersionOutput);

        await Assert.That(parsed).IsEqualTo(StableVersion);
    }

    [Test]
    public async Task ParseLatestPublishedVersion_PicksHighestGitTag()
    {
        var parsed = ClaudeCliMetadataReader.ParseLatestPublishedVersion(HighestStableGitOutput);

        await Assert.That(parsed).IsEqualTo(StableVersion);
    }

    [Test]
    public async Task IsNewerVersion_TreatsStableAsNotOlderThanMatchingPrerelease()
    {
        var isNewer = ClaudeCliMetadataReader.IsNewerVersion(StableVsPrereleaseVersion, StableVersion);

        await Assert.That(isNewer).IsFalse();
    }

    [Test]
    public async Task IsNewerVersion_UsesNumericSemVerComparisonForPrereleaseIdentifiers()
    {
        var isNewer = ClaudeCliMetadataReader.IsNewerVersion(PrereleaseTenVersion, PrereleaseTwoVersion);

        await Assert.That(isNewer).IsTrue();
    }

    [Test]
    public async Task ParseLatestPublishedVersion_UsesNumericSemVerComparisonForPrereleaseIdentifiers()
    {
        var parsed = ClaudeCliMetadataReader.ParseLatestPublishedVersion(NumericPrereleaseGitOutput);

        await Assert.That(parsed).IsEqualTo(PrereleaseTenVersion);
    }

    [Test]
    public async Task ParseDefaultModelFromJson_ReadsModelProperty()
    {
        var parsed = ClaudeCliMetadataReader.ParseDefaultModelFromJson(
            DefaultModelJsonTemplate.Replace(ModelPlaceholder, ClaudeModels.ClaudeOpus45, StringComparison.Ordinal));

        await Assert.That(parsed).IsEqualTo(ClaudeModels.ClaudeOpus45);
    }

    [Test]
    public async Task TryParseSemanticVersion_RejectsInvalidText()
    {
        var parsed = ClaudeCliMetadataReader.TryParseSemanticVersion(InvalidVersionText, out _);

        await Assert.That(parsed).IsFalse();
    }
}
