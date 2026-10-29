using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartLedger.Infrastructure.Extraction;

namespace SmartLedger.Infrastructure.Gemini;

/// <summary>
/// The one place that talks to Google Gemini's generateContent. Every caller asks for structured output (a JSON
/// schema Gemini must follow), so nothing ever parses free text. Retries for rate limits and outages come from the
/// HTTP resilience handler registered with it.
/// </summary>
internal sealed class GeminiApi(HttpClient http, IOptions<GeminiOptions> options, ILogger<GeminiApi> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The model that will answer, for recording alongside what it wrote.</summary>
    public string Model => options.Value.Model;

    public static object Text(string text) => new { Text = text };

    public static object InlineData(string mimeType, byte[] data) =>
        new { InlineData = new { MimeType = mimeType, Data = Convert.ToBase64String(data) } };

    /// <returns>The JSON Gemini wrote, which follows <paramref name="responseSchema"/>.</returns>
    /// <exception cref="GeminiUnavailableException">No key, no connection, an error status, or no answer.</exception>
    public async Task<string> GenerateJsonAsync(
        string systemInstruction, IReadOnlyList<object> parts, object responseSchema, double temperature,
        CancellationToken cancellationToken)
    {
        var gemini = options.Value;
        if (string.IsNullOrWhiteSpace(gemini.ApiKey))
        {
            throw new GeminiUnavailableException("AI isn't configured: set Gemini:ApiKey.");
        }

        var body = new
        {
            SystemInstruction = new { Parts = new[] { Text(systemInstruction) } },
            Contents = new[] { new { Role = "user", Parts = parts } },
            GenerationConfig = new
            {
                Temperature = temperature,
                ResponseMimeType = "application/json",
                ResponseSchema = responseSchema,
            },
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, $"models/{gemini.Model}:generateContent")
        {
            Content = JsonContent.Create(body, options: Json),
        };
        // A header rather than "?key=" in the URL, so the key never shows up in request logs.
        message.Headers.Add("x-goog-api-key", gemini.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Network errors, and the resilience handler giving up (timeouts, open circuit) after its retries.
            throw new GeminiUnavailableException("The AI service couldn't be reached.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // The body explains bad keys, quota and unsupported files; never includes our key.
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Gemini returned {Status}: {Detail}", (int)response.StatusCode, Truncate(detail, 2000));
                throw new GeminiUnavailableException(
                    response.StatusCode == HttpStatusCode.TooManyRequests
                        ? "The AI service's free-tier limit was reached."
                        : $"The AI service answered {(int)response.StatusCode}.");
            }

            var reply = await response.Content.ReadFromJsonAsync<GenerateContentResponse>(Json, cancellationToken);
            var text = reply?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault(p => p.Text is not null)?.Text;

            if (string.IsNullOrWhiteSpace(text))
            {
                // E.g. the response was blocked by a safety filter: nothing to read.
                logger.LogWarning("Gemini returned no content (finish reason {Reason})", reply?.Candidates?.FirstOrDefault()?.FinishReason);
                throw new GeminiUnavailableException("The AI service returned no answer.");
            }

            return text;
        }
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    // ---- Gemini's response envelope ----

    private sealed record GenerateContentResponse(List<Candidate>? Candidates);

    private sealed record Candidate(CandidateContent? Content, string? FinishReason);

    private sealed record CandidateContent(List<Part>? Parts);

    private sealed record Part(string? Text);
}

/// <summary>Gemini couldn't be used right now (missing key, outage, quota, empty answer). Trying later may work.</summary>
internal sealed class GeminiUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
