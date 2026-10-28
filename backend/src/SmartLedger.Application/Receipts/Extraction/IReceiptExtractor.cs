namespace SmartLedger.Application.Receipts.Extraction;

/// <summary>Reads a receipt image or PDF and returns its details. Gemini in production, a fake in tests.</summary>
public interface IReceiptExtractor
{
    /// <exception cref="ReceiptExtractionUnavailableException">The AI service couldn't be used right now.</exception>
    Task<ExtractedReceipt> ExtractAsync(ExtractionRequest request, CancellationToken cancellationToken);
}

/// <param name="CategoryNames">The user's categories: the AI may only suggest one of these.</param>
/// <param name="BaseCurrency">The user's reporting currency: a last-resort hint when the receipt shows none.</param>
public sealed record ExtractionRequest(
    Stream Content,
    string ContentType,
    IReadOnlyList<string> CategoryNames,
    string BaseCurrency);

/// <summary>
/// What the AI says it read. Untrusted: <see cref="ExtractionSanitizer"/> checks every value before it's saved.
/// Amounts are in the receipt's own currency; the date is ISO "yyyy-MM-dd" text.
/// </summary>
public sealed record ExtractedReceipt(
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
    IReadOnlyList<ExtractedLineItem> LineItems,
    IReadOnlyList<ExtractedFlag> FieldsToCheck,
    string RawResponse);

public sealed record ExtractedLineItem(string? Description, decimal? Quantity, decimal? UnitPrice, decimal? Amount);

public sealed record ExtractedFlag(string? Field, string? Reason);

/// <summary>The AI service failed (rate limit, outage, missing key). The receipt can be tried again later.</summary>
public sealed class ReceiptExtractionUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
