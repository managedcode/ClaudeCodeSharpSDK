namespace ManagedCode.ClaudeCodeSharpSDK.Tests.Shared;

internal sealed class RequiresAuthenticatedClaudeAttribute : TUnit.Core.SkipAttribute
{
    private const string SkipMessage = "Requires an authenticated local Claude Code session.";

    public RequiresAuthenticatedClaudeAttribute()
        : base(SkipMessage)
    {
    }

    public override Task<bool> ShouldSkip(TUnit.Core.TestRegisteredContext testContext)
    {
        _ = testContext;
        return Task.FromResult(!RealClaudeTestSupport.CanRunAuthenticatedTests());
    }
}
