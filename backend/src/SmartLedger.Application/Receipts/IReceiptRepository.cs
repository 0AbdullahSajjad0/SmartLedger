using SmartLedger.Domain.Entities;
using SmartLedger.Domain.Enums;

namespace SmartLedger.Application.Receipts;

public interface IReceiptRepository
{
    Task AddAsync(Guid userId, IReadOnlyList<Receipt> receipts, CancellationToken cancellationToken);

    Task<Receipt?> GetAsync(Guid userId, Guid receiptId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<ReceiptStatus, int>> CountByStatusAsync(Guid userId, CancellationToken cancellationToken);

    Task<(IReadOnlyList<Receipt> Items, int TotalCount)> ListAsync(
        Guid userId, ReceiptStatus? status, ReceiptFilter filter, int page, int pageSize, CancellationToken cancellationToken);

    void Remove(Receipt receipt);

    Task<Receipt?> GetForUpdateAsync(Guid userId, Guid receiptId, CancellationToken cancellationToken);

    Task<Receipt?> GetForUpdateAsync(Guid receiptId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ListIdsWithStatusAsync(ReceiptStatus status, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
