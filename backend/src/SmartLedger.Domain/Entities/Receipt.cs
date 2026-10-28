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

    /// <summary>The day <see cref="ExchangeRate"/> is for: the receipt date, or the nearest earlier day with rates.</summary>
    public DateOnly? ExchangeRateDate { get; private set; }

    /// <summary>
    /// What the bank actually charged, in <see cref="BaseCurrency"/>, when the user entered it from their statement.
    /// Banks convert at their own rate plus fees, so this wins over the calculated amount.
    /// </summary>
    public decimal? AmountCharged { get; private set; }

    /// <summary><see cref="Total"/> converted to <see cref="BaseCurrency"/>. Reports sum this value.</summary>
    public decimal? BaseCurrencyTotal { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    /// <summary>Details can be corrected while the receipt waits for review, or after AI couldn't read it.</summary>
    public bool IsEditable => Status is ReceiptStatus.NeedsReview or ReceiptStatus.Failed;

    /// <summary>Everything approval needs. Vendor, date, currency and total come from the receipt; the category from the user.</summary>
    public bool IsReadyToApprove =>
        VendorName is not null && ReceiptDate is not null && Currency is not null && Total is not null && CategoryId is not null;

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

    /// <summary>
    /// The user corrected the details. A receipt AI couldn't read becomes an ordinary one waiting for review, now
    /// that a person has filled it in.
    /// </summary>
    public void MarkEdited()
    {
        if (!IsEditable)
        {
            throw new InvalidOperationException($"Receipt is {Status}; reopen it before changing it.");
        }

        Status = ReceiptStatus.NeedsReview;
        FailureReason = null;
    }

    /// <summary>Approves the receipt and freezes its value in the user's base currency.</summary>
    /// <param name="exchangeRate">Multiplier from the receipt's currency to <paramref name="baseCurrency"/>.</param>
    /// <param name="rateDate">The day the rate is for.</param>
    /// <param name="amountCharged">The amount on the user's statement, if they entered one; it replaces the calculated amount.</param>
    public void Approve(string baseCurrency, decimal exchangeRate, DateOnly rateDate, decimal? amountCharged, DateTimeOffset approvedAt)
    {
        EnsureStatus(ReceiptStatus.NeedsReview);

        if (!IsReadyToApprove)
        {
            throw new InvalidOperationException("Vendor, date, currency, total and category are required before approving.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exchangeRate);
        if (amountCharged is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amountCharged), "The amount charged can't be negative.");
        }

        Status = ReceiptStatus.Approved;
        BaseCurrency = baseCurrency;
        ExchangeRate = exchangeRate;
        ExchangeRateDate = rateDate;
        AmountCharged = amountCharged;
        BaseCurrencyTotal = amountCharged ?? Math.Round(Total!.Value * exchangeRate, 2, MidpointRounding.AwayFromZero);
        ApprovedAt = approvedAt;
    }

    /// <summary>Not an expense to keep: a duplicate, a personal purchase, or something that isn't a receipt.</summary>
    public void Reject()
    {
        if (!IsEditable)
        {
            throw new InvalidOperationException($"Receipt is {Status}; only receipts waiting for review can be rejected.");
        }

        Status = ReceiptStatus.Rejected;
    }

    /// <summary>
    /// Takes back an approval or rejection. The frozen conversion is cleared, so approving again uses a fresh rate
    /// and the receipt leaves reports until then.
    /// </summary>
    public void Reopen()
    {
        if (Status is not (ReceiptStatus.Approved or ReceiptStatus.Rejected))
        {
            throw new InvalidOperationException($"Receipt is {Status}; only approved or rejected receipts can be reopened.");
        }

        Status = ReceiptStatus.NeedsReview;
        BaseCurrency = null;
        ExchangeRate = null;
        ExchangeRateDate = null;
        AmountCharged = null;
        BaseCurrencyTotal = null;
        ApprovedAt = null;
    }

    private void EnsureStatus(ReceiptStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"Receipt is {Status}; this action needs it to be {expected}.");
        }
    }
}
