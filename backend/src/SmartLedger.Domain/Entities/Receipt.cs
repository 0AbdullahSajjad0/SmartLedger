using SmartLedger.Domain.Common;
using SmartLedger.Domain.Enums;

namespace SmartLedger.Domain.Entities;

public class Receipt : Entity
{
    public Guid UserId { get; set; }

    public int Number { get; set; }

    public ReceiptStatus Status { get; private set; } = ReceiptStatus.Processing;

    public string? VendorName { get; set; }

    public DateOnly? ReceiptDate { get; set; }

    public string? Currency { get; set; }

    public decimal? Subtotal { get; set; }

    public decimal? Tax { get; set; }

    public decimal? Total { get; set; }

    public string? PaymentMethod { get; set; }

    public Guid? CategoryId { get; set; }

    public bool CategorySuggestedByAi { get; set; }

    public string? Notes { get; set; }

    public List<LineItem> LineItems { get; set; } = [];

    public List<ExtractionFlag> ExtractionFlags { get; set; } = [];

    public string? RawExtraction { get; set; }

    public string? FailureReason { get; private set; }

    public int ExtractionAttempts { get; set; }

    public required string FileName { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    public required string StorageKey { get; set; }

    public string? PreviewStorageKey { get; set; }

    public string? BaseCurrency { get; private set; }

    public decimal? ExchangeRate { get; private set; }

    public DateOnly? ExchangeRateDate { get; private set; }

    public decimal? AmountCharged { get; private set; }

    public decimal? BaseCurrencyTotal { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public bool IsEditable => Status is ReceiptStatus.NeedsReview or ReceiptStatus.Failed;

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

    public void MarkEdited()
    {
        if (!IsEditable)
        {
            throw new InvalidOperationException($"Receipt is {Status}; reopen it before changing it.");
        }

        Status = ReceiptStatus.NeedsReview;
        FailureReason = null;
    }

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

    public void Reject()
    {
        if (!IsEditable)
        {
            throw new InvalidOperationException($"Receipt is {Status}; only receipts waiting for review can be rejected.");
        }

        Status = ReceiptStatus.Rejected;
    }

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
