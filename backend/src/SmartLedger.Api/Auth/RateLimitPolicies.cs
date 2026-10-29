namespace SmartLedger.Api.Auth;

internal static class RateLimitPolicies
{
    /// <summary>Limits sign-in, registration and refresh per client IP to slow down password guessing.</summary>
    public const string Auth = "auth";

    /// <summary>Limits receipt uploads per signed-in user, so one account can't flood storage or the server.</summary>
    public const string Uploads = "uploads";

    /// <summary>Limits AI-written analyses per signed-in user: each is a Gemini call on the shared free-tier quota.</summary>
    public const string Insights = "insights";
}
