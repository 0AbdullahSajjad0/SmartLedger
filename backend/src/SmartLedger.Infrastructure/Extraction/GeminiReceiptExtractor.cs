using System.Text.Json;
using SmartLedger.Application.Receipts.Extraction;
using SmartLedger.Infrastructure.Gemini;

namespace SmartLedger.Infrastructure.Extraction;

internal sealed class GeminiReceiptExtractor(GeminiApi gemini) : IReceiptExtractor
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ExtractedReceipt> ExtractAsync(ExtractionRequest request, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await request.Content.CopyToAsync(buffer, cancellationToken);

        string text;
        try
        {
            text = await gemini.GenerateJsonAsync(
                GeminiPrompt.System,
                [GeminiApi.InlineData(request.ContentType, buffer.ToArray()), GeminiApi.Text(GeminiPrompt.ForRequest(request))],
                GeminiPrompt.ResponseSchema(request.CategoryNames),
                temperature: 0,
                cancellationToken);
        }
        catch (GeminiUnavailableException ex)
        {
            throw new ReceiptExtractionUnavailableException(ex.Message, ex);
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
