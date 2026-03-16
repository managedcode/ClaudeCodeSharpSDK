using System.Reflection;
using ManagedCode.ClaudeCodeSharpSDK.Models;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Unit;

public class ClaudeModelsTests
{
    [Test]
    public async Task ClaudeModels_KnownContainAllPublicModelSlugs()
    {
        var sdkModelSlugs = GetSdkModelSlugs();
        var knownModelSlugs = ClaudeModels.Known
            .Select(static model => model.Slug)
            .ToArray();
        var missingModelSlugs = sdkModelSlugs
            .Except(knownModelSlugs, StringComparer.Ordinal)
            .ToArray();

        await Assert.That(missingModelSlugs).IsEmpty();
    }

    [Test]
    public async Task ClaudeModels_KnownDoNotContainDuplicateSlugs()
    {
        var duplicateModelSlugs = ClaudeModels.Known
            .GroupBy(static model => model.Slug, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        await Assert.That(duplicateModelSlugs).IsEmpty();
    }

    private static string[] GetSdkModelSlugs()
    {
        return typeof(ClaudeModels)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(static field => field is { IsLiteral: true, IsInitOnly: false, FieldType: not null } && field.FieldType == typeof(string))
            .Select(static field => (string)field.GetRawConstantValue()!)
            .ToArray();
    }
}
