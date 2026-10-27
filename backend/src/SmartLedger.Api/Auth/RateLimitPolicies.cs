namespace SmartLedger.Api.Auth;

internal static class RateLimitPolicies
{
    /// <summary>Limits sign-in, registration and refresh per client IP to slow down password guessing.</summary>
    public const string Auth = "auth";
}
