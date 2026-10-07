using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo(AssemblyNames.TestsAssemblyName)]
[assembly: InternalsVisibleTo(AssemblyNames.ExtensionsAiAssemblyName)]

internal static class AssemblyNames
{
    internal const string TestsAssemblyName = "ManagedCode.ClaudeCodeSharpSDK.Tests";
    internal const string ExtensionsAiAssemblyName = "ManagedCode.ClaudeCodeSharpSDK.Extensions.AI";
}
