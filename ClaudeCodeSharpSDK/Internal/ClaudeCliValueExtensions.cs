using ManagedCode.ClaudeCodeSharpSDK.Client;

namespace ManagedCode.ClaudeCodeSharpSDK.Internal;

internal static class ClaudeCliValueExtensions
{
    private static class PermissionModeValues
    {
        public const string AcceptEdits = "acceptEdits";
        public const string BypassPermissions = "bypassPermissions";
        public const string Default = "default";
        public const string Delegate = "delegate";
        public const string DontAsk = "dontAsk";
        public const string Plan = "plan";
    }

    private static class EffortLevelValues
    {
        public const string Low = "low";
        public const string Medium = "medium";
        public const string High = "high";
        public const string Max = "max";
    }

    private static class SettingSourceValues
    {
        public const string User = "user";
        public const string Project = "project";
        public const string Local = "local";
    }

    public static string ToCliValue(this PermissionMode permissionMode)
    {
        return permissionMode switch
        {
            PermissionMode.AcceptEdits => PermissionModeValues.AcceptEdits,
            PermissionMode.BypassPermissions => PermissionModeValues.BypassPermissions,
            PermissionMode.Default => PermissionModeValues.Default,
            PermissionMode.Delegate => PermissionModeValues.Delegate,
            PermissionMode.DontAsk => PermissionModeValues.DontAsk,
            PermissionMode.Plan => PermissionModeValues.Plan,
            _ => throw new ArgumentOutOfRangeException(nameof(permissionMode), permissionMode, null),
        };
    }

    public static string ToCliValue(this EffortLevel effortLevel)
    {
        return effortLevel switch
        {
            EffortLevel.Low => EffortLevelValues.Low,
            EffortLevel.Medium => EffortLevelValues.Medium,
            EffortLevel.High => EffortLevelValues.High,
            EffortLevel.Max => EffortLevelValues.Max,
            _ => throw new ArgumentOutOfRangeException(nameof(effortLevel), effortLevel, null),
        };
    }

    public static string ToCliValue(this SettingSource settingSource)
    {
        return settingSource switch
        {
            SettingSource.User => SettingSourceValues.User,
            SettingSource.Project => SettingSourceValues.Project,
            SettingSource.Local => SettingSourceValues.Local,
            _ => throw new ArgumentOutOfRangeException(nameof(settingSource), settingSource, null),
        };
    }
}
