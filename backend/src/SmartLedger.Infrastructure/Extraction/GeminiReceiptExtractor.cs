using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartLedger.Application.Receipts.Extraction;

namespace SmartLedger.Infrastructure.Extraction;

/// <summary>
/// Reads receipts with Google Gemini. Gemini must answer in a fixed JSON shape (structured output), so we never
/// parse free text. Retries for rate limits and outages come from the HTTP resilience handler registered with it.
/// </summary>
internal sealed class GeminiReceiptExtractor(
    HttpClient http,
    IOptions<GeminiOptions> options,
    ILogger<GeminiReceiptExtractor> logger) : IReceiptExtractor
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<ExtractedReceipt> ExtractAsync(ExtractionRequest request, CancellationToken cancellationToken)
    {
        var gemini = options.Value;
        if (string.IsNullOrWhiteSpace(gemini.ApiKey))
        {
            throw new ReceiptExtractionUnavailableException("AI extraction isn't configured: set Gemini:ApiKey.");
        }

        using var buffer = new MemoryStream();
        await request.Content.CopyToAsync(buffer, cancellationToken);

        var body = new
        {
            SystemInstruction = new { Parts = new[] { new { Text = GeminiPrompt.System } } },
            Contents = new[]
            {
                new
                {
                    Role = "user",
                    Parts = new object[]
                    {
                        new { InlineData = new { MimeType = request.ContentType, Data = Convert.ToBase64String(buffer.ToArray()) } },
                        new { Text = GeminiPrompt.ForRequest(request) },
                    },
                },
            },
            GenerationConfig = new
            {
                Temperature = 0,
                ResponseMimeType = "application/json",
                ResponseSchema = GeminiPrompt.ResponseSchema(request.CategoryNames),
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
            throw new ReceiptExtractionUnavailableException("The AI service couldn't be reached.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // The body explains bad keys, quota and unsupported files; never includes our key.
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Gemini returned {Status}: {Detail}", (int)response.StatusCode, Truncate(detail, 2000));
                throw new ReceiptExtractionUnavailableException(
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
                throw new ReceiptExtractionUnavailableException("The AI service returned no answer.");
            }

            GeminiReceipt? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<GeminiReceipt>(text, Json);
            }
            catch (JsonException ex)
            {
                throw new ReceiptExtractionUnavailableException("The AI service's answer wasn't valid JSON.", ex);
            }

            if (parsed is null)
            {
                throw new ReceiptExtractionUnavailableException("The AI service's answer was empty.");
            }

            return new ExtractedReceipt(
                parsed.IsReceipt,
                parsed.UnreadableReason,
                parsed.VendorName,
                parsed.ReceiptDate,
                parsed.Currency,
                parsed.Subtotal,
                parsed.Tax,
                parsed.Total,
                parsed.PaymentMethod,
                parsed.Category,
                parsed.LineItems?.Select(l => new ExtractedLineItem(l.Description, l.Quantity, l.UnitPrice, l.Amount)).ToList() ?? [],
                parsed.FieldsToCheck?.Select(f => new ExtractedFlag(f.Field, f.Reason)).ToList() ?? [],
                text);
        }
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    // ---- Gemini's response envelope and our schema's shape ----

    private sealed record GenerateContentResponse(List<Candidate>? Candidates);

    private sealed record Candidate(CandidateContent? Content, string? FinishReason);

    private sealed record CandidateContent(List<Part>? Parts);

    private sealed record Part(string? Text);

    private sealed record GeminiReceipt(
        bool IsReceipt,
        string? UnreadableReason,
        string? VendorName,
        string? ReceiptDate,
        string? Currency,
        decimal? Subtotal,
        decimal? Tax,
        decimal? Total,
        string? PaymentMethod,
        string? Category,
        List<GeminiLineItem>? LineItems,
        List<GeminiFlag>? FieldsToCheck);

    private sealed record GeminiLineItem(string? Description, decimal? Quantity, decimal? UnitPrice, decimal? Amount);

    private sealed record GeminiFlag(string? Field, string? Reason);
}
