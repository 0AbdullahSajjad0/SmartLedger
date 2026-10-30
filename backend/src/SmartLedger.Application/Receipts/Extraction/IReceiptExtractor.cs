namespace SmartLedger.Application.Receipts.Extraction;

public interface IReceiptExtractor
{
    Task<ExtractedReceipt> ExtractAsync(ExtractionRequest request, CancellationToken cancellationToken);
}

public sealed record ExtractionRequest(
    Stream Content,
    string ContentType,
    IReadOnlyList<string> CategoryNames,
    string BaseCurrency);

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

public sealed class ReceiptExtractionUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
