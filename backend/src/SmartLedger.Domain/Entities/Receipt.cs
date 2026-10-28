using SmartLedger.Domain.Common;
using SmartLedger.Domain.Enums;

namespace SmartLedger.Domain.Entities;

public class Receipt : Entity
{
    public Guid UserId { get; set; }

    /// <summary>Per-user entry number shown in the ledger, e.g. 425 → "0425".</summary>
    public int Number { get; set; }

    public ReceiptStatus Status { get; private set; } = ReceiptStatus.Processing;

    // ---- Extracted details, in the receipt's own currency ----

    public string? VendorName { get; set; }

    public DateOnly? ReceiptDate { get; set; }

    /// <summary>ISO 4217 code as printed on the receipt, e.g. "EUR".</summary>
    public string? Currency { get; set; }

    public decimal? Subtotal { get; set; }

    public decimal? Tax { get; set; }

    public decimal? Total { get; set; }

    public string? PaymentMethod { get; set; }

    public Guid? CategoryId { get; set; }

    public bool CategorySuggestedByAi { get; set; }

    public string? Notes { get; set; }

    public List<LineItem> LineItems { get; set; } = [];

    // ---- AI extraction ----

    /// <summary>Fields the AI was unsure about. Stored as JSON.</summary>
    public List<ExtractionFlag> ExtractionFlags { get; set; } = [];

    /// <summary>The AI's full response, kept for debugging and reprocessing.</summary>
    public string? RawExtraction { get; set; }

    public string? FailureReason { get; private set; }

    /// <summary>Times the AI service was unavailable while reading this receipt; it's retried a few times, then marked failed.</summary>
    public int ExtractionAttempts { get; set; }

    // ---- Files (stored in object storage) ----

    public required string FileName { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>The upload exactly as received. Only ever served to its owner.</summary>
    public required string StorageKey { get; set; }

    /// <summary>
    /// The cleaned JPEG shown in the app (upright, size-limited, no metadata such as GPS location). Photos are sent to
    /// AI as this copy. Null until the background worker has made it, and for receipts uploaded before previews existed.
    /// </summary>
    public string? PreviewStorageKey { get; set; }

    // ---- Conversion, frozen at approval so past reports do not change when rates are updated ----

    /// <summary>The user's base currency at the time of approval.</summary>
    public string? BaseCurrency { get; private set; }

    /// <summary>Multiplier from <see cref="Currency"/> to <see cref="BaseCurrency"/>.</summary>
    public decimal? ExchangeRate { get; private set; }

    /// <summary><see cref="Total"/> converted to <see cref="BaseCurrency"/>. Reports sum this value.</summary>
    public decimal? BaseCurrencyTotal { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public void MarkExtracted()
    {
        EnsureStatus(ReceiptStatus.Processing);
        Status = ReceiptStatus.NeedsReview;
    }

    public void MarkFailed(string reason)
    {
        EnsureStatus(ReceiptStatus.Processing);
        Status = ReceiptStatus.Failed;
        FailureReason = reason;
    }

    /// <summary>Approves the receipt and freezes its value in the user's base currency.</summary>
    public void Approve(string baseCurrency, decimal exchangeRate, DateTimeOffset approvedAt)
    {
        EnsureStatus(ReceiptStatus.NeedsReview);

        if (VendorName is null || ReceiptDate is null || Currency is null || Total is null)
        {
            throw new InvalidOperationException("Vendor, date, currency and total are required before approving.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exchangeRate);

        Status = ReceiptStatus.Approved;
        BaseCurrency = baseCurrency;
        ExchangeRate = exchangeRate;
        BaseCurrencyTotal = Math.Round(Total.Value * exchangeRate, 2, MidpointRounding.AwayFromZero);
        ApprovedAt = approvedAt;
    }

    public void Reject()
    {
        EnsureStatus(ReceiptStatus.NeedsReview);
        Status = ReceiptStatus.Rejected;
    }

    private void EnsureStatus(ReceiptStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"Receipt is {Status}; this action needs it to be {expected}.");
        }
    }
}
