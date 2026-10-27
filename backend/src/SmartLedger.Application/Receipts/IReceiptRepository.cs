using SmartLedger.Domain.Entities;
using SmartLedger.Domain.Enums;

namespace SmartLedger.Application.Receipts;

public interface IReceiptRepository
{
    /// <summary>Saves new receipts, giving each the user's next entry numbers (0001, 0002…).</summary>
    Task AddAsync(Guid userId, IReadOnlyList<Receipt> receipts, CancellationToken cancellationToken);

    /// <summary>Null when the receipt doesn't exist or belongs to another user.</summary>
    Task<Receipt?> GetAsync(Guid userId, Guid receiptId, CancellationToken cancellationToken);

    /// <summary>Newest first.</summary>
    Task<(IReadOnlyList<Receipt> Items, int TotalCount)> ListAsync(
        Guid userId, ReceiptStatus? status, int page, int pageSize, CancellationToken cancellationToken);
}
