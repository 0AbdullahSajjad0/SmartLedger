namespace SmartLedger.Domain.Enums;

public enum ReceiptStatus
{
    Processing = 0,

    NeedsReview = 1,

    Approved = 2,

    Rejected = 3,

    Failed = 4,
}
