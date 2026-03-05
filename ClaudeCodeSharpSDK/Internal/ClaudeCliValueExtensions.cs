using ManagedCode.ClaudeCodeSharpSDK.Client;

namespace ManagedCode.ClaudeCodeSharpSDK.Internal;

internal static class ClaudeCliValueExtensions
{
    public static string ToCliValue(this PermissionMode permissionMode)
    {
        return permissionMode switch
        {
            PermissionMode.AcceptEdits => "acceptEdits",
            PermissionMode.BypassPermissions => "bypassPermissions",
            PermissionMode.Default => "default",
            PermissionMode.Delegate => "delegate",
            PermissionMode.DontAsk => "dontAsk",
            PermissionMode.Plan => "plan",
            _ => throw new ArgumentOutOfRangeException(nameof(permissionMode), permissionMode, null),
        };
    }

    public static string ToCliValue(this SettingSource settingSource)
    {
        return settingSource switch
        {
            SettingSource.User => "user",
            SettingSource.Project => "project",
            SettingSource.Local => "local",
            _ => throw new ArgumentOutOfRangeException(nameof(settingSource), settingSource, null),
        };
    }
}
