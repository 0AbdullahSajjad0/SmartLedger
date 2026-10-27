namespace SmartLedger.Domain.Enums;

public enum ReceiptStatus
{
    /// <summary>Uploaded; AI extraction is running.</summary>
    Processing = 0,

    /// <summary>AI extraction finished; waiting for the user to check and approve.</summary>
    NeedsReview = 1,

    /// <summary>Confirmed by the user and counted in reports.</summary>
    Approved = 2,

    /// <summary>Discarded by the user (e.g. a duplicate). Not counted in reports.</summary>
    Rejected = 3,

    /// <summary>AI could not read the file. See <see cref="Entities.Receipt.FailureReason"/>.</summary>
    Failed = 4,
}
