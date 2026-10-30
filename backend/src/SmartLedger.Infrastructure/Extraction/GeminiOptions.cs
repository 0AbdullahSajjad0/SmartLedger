namespace SmartLedger.Infrastructure.Extraction;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string? ApiKey { get; init; }

    public string Model { get; init; } = "gemini-flash-lite-latest";

    public Uri BaseUrl { get; init; } = new("https://generativelanguage.googleapis.com/v1beta/");

    public TimeSpan DelayBetweenRequests { get; init; } = TimeSpan.FromSeconds(7);
}
