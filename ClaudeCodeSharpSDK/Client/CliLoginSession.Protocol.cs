using System.Text.Json.Nodes;
using ManagedCode.ClaudeCodeSharpSDK.Models;

namespace ManagedCode.ClaudeCodeSharpSDK.Client;

public sealed partial class CliLoginSession
{
    private const string AuthCommand = "auth";
    private const string LoginCommand = "login";
    private const string SettingsFlag = "--settings";
    private const string AuthorizationCodePrompt = "Paste code here if prompted";
    private const string AuthorizationUrlMissingMessage = "Claude requested an authorization code without supplying its sign-in URL.";

    internal static Task<CliLoginSession> StartAsync(CliLaunchCommand launchCommand,
        IReadOnlyDictionary<string, string> environment, JsonObject? settings, TimeSpan terminationTimeout,
        int maximumOutputCharacters, CancellationToken cancellationToken)
    {
        string[] arguments = settings is null
            ? [AuthCommand, LoginCommand]
            : [SettingsFlag, settings.ToJsonString(), AuthCommand, LoginCommand];
        return StartAsync(CreateStartInfo(launchCommand, environment, arguments), terminationTimeout,
            maximumOutputCharacters, cancellationToken);
    }

    private static Task InitializeNativeAsync() => Task.CompletedTask;

    private Task ProcessNativeLineAsync(bool standardError, string line)
    {
        if (!standardError)
        {
            CaptureAuthorizationUrl(line);
        }
        return Task.CompletedTask;
    }

    private bool ProcessNativePrompt(string text)
    {
        if (!text.Contains(AuthorizationCodePrompt, StringComparison.Ordinal))
        {
            return false;
        }
        PublishInstructions(_authorizationUrl ?? throw new InvalidDataException(AuthorizationUrlMissingMessage), null, true);
        return true;
    }

    private void ValidateNativeExit()
    {
        _authenticated = _process.ExitCode == 0 && Instructions is not null;
        if (!_authenticated)
        {
            throw new InvalidOperationException(string.Concat(LoginFailedMessage, _process.ExitCode));
        }
    }
}
