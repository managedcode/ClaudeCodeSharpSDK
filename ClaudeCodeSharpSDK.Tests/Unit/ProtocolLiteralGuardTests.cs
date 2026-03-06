using ManagedCode.ClaudeCodeSharpSDK.Tests.Shared;

namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Unit;

public class ProtocolLiteralGuardTests
{
    private const string CouldNotLocateRepositoryRootMessage = "Could not locate repository root from test execution directory.";
    private const string CouldNotLocateSourceFileMessagePrefix = "Could not locate source file '";
    private const string CouldNotLocateSourceFileMessageMiddle = "' under any known source directories: ";
    private const string CouldNotLocateSourceFileMessageSuffix = ".";
    private const string EventsFileName = "Events.cs";
    private const string EventsLiteralFragment = "ThreadEvent(\"";
    private const string ItemsFileName = "Items.cs";
    private const string ItemsLiteralFragment = "ThreadItem(Id, \"";
    private const string SdkSourceDirectoryName = "ClaudeCodeSharpSDK";
    private const string SrcDirectoryName = "src";
    private static readonly string[] SourceDirectories = [SdkSourceDirectoryName, SrcDirectoryName];

    [Test]
    public async Task ItemsFile_DoesNotContainInlineThreadItemTypeLiterals()
    {
        var content = await File.ReadAllTextAsync(ResolveSdkSourceFilePath(ItemsFileName));
        await Assert.That(content.Contains(ItemsLiteralFragment, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task EventsFile_DoesNotContainInlineThreadEventTypeLiterals()
    {
        var content = await File.ReadAllTextAsync(ResolveSdkSourceFilePath(EventsFileName));
        await Assert.That(content.Contains(EventsLiteralFragment, StringComparison.Ordinal)).IsFalse();
    }

    private static string ResolveSdkSourceFilePath(string fileName)
    {
        var repositoryRoot = ResolveRepositoryRootPath();
        foreach (var sourceDirectory in SourceDirectories)
        {
            var sourceRoot = Path.Combine(repositoryRoot, sourceDirectory);
            if (!Directory.Exists(sourceRoot))
            {
                continue;
            }

            foreach (var candidatePath in Directory.EnumerateFiles(sourceRoot, fileName, SearchOption.AllDirectories))
            {
                return candidatePath;
            }
        }

        throw new InvalidOperationException(
            string.Concat(
                CouldNotLocateSourceFileMessagePrefix,
                fileName,
                CouldNotLocateSourceFileMessageMiddle,
                string.Join(TestConstants.CommaSpace, SourceDirectories),
                CouldNotLocateSourceFileMessageSuffix));
    }

    private static string ResolveRepositoryRootPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, TestConstants.SolutionFileName)))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(CouldNotLocateRepositoryRootMessage);
    }
}
