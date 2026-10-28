namespace SmartLedger.Infrastructure.Extraction;

/// <summary>Bound from the "Gemini" configuration section.</summary>
public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    /// <summary>A secret: user secrets locally, a Container App secret in production. Never in a file in the repo.</summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// An alias that follows Google's newest Flash-Lite model, so the app doesn't break when one is retired.
    /// Flash-Lite answered a receipt correctly in 1–2 s on the free tier, while the full Flash models were often
    /// overloaded (503) or took up to 100 s; reading a receipt doesn't need the bigger model.
    /// </summary>
    public string Model { get; init; } = "gemini-flash-lite-latest";

    public Uri BaseUrl { get; init; } = new("https://generativelanguage.googleapis.com/v1beta/");

    /// <summary>Pause between receipts, to stay inside the free tier's requests-per-minute limit.</summary>
    public TimeSpan DelayBetweenRequests { get; init; } = TimeSpan.FromSeconds(7);
}
