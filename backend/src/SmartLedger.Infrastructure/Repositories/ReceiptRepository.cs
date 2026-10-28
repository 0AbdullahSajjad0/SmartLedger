using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmartLedger.Application.Receipts;
using SmartLedger.Domain.Entities;
using SmartLedger.Domain.Enums;
using SmartLedger.Infrastructure.Persistence;

namespace SmartLedger.Infrastructure.Repositories;

internal sealed class ReceiptRepository(AppDbContext db) : IReceiptRepository
{
    private const int MaxNumberingAttempts = 3;

    public async Task AddAsync(Guid userId, IReadOnlyList<Receipt> receipts, CancellationToken cancellationToken)
    {
        db.Receipts.AddRange(receipts);

        // Two uploads at the same moment can pick the same next number; the unique index on
        // (UserId, Number) rejects the second, which then picks again.
        for (var attempt = 1; ; attempt++)
        {
            var next = await db.Receipts.Where(r => r.UserId == userId).MaxAsync(r => (int?)r.Number, cancellationToken) ?? 0;
            foreach (var receipt in receipts)
            {
                receipt.Number = ++next;
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException ex) when (attempt < MaxNumberingAttempts && IsDuplicateKey(ex))
            {
            }
        }
    }

    public Task<Receipt?> GetAsync(Guid userId, Guid receiptId, CancellationToken cancellationToken) =>
        db.Receipts
            .AsNoTracking()
            .Include(r => r.LineItems)
            .SingleOrDefaultAsync(r => r.Id == receiptId && r.UserId == userId, cancellationToken);

    public async Task<(IReadOnlyList<Receipt> Items, int TotalCount)> ListAsync(
        Guid userId, ReceiptStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Receipts.AsNoTracking().Where(r => r.UserId == userId);
        if (status is not null)
        {
            query = query.Where(r => r.Status == status);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(r => r.Number)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<Receipt?> GetForUpdateAsync(Guid receiptId, CancellationToken cancellationToken) =>
        db.Receipts
            .Include(r => r.LineItems)
            .SingleOrDefaultAsync(r => r.Id == receiptId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListIdsWithStatusAsync(ReceiptStatus status, CancellationToken cancellationToken) =>
        await db.Receipts
            .Where(r => r.Status == status)
            .OrderBy(r => r.CreatedAt)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    // 2601: duplicate key in a unique index; 2627: unique constraint violation.
    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
