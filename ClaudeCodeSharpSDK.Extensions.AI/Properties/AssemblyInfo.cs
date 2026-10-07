using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo(ExtensionsAssemblyNames.TestsAssemblyName)]

internal static class ExtensionsAssemblyNames
{
    internal const string TestsAssemblyName = "ManagedCode.ClaudeCodeSharpSDK.Tests";
}
