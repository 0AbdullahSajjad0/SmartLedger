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

    public async Task<IReadOnlyDictionary<ReceiptStatus, int>> CountByStatusAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Receipts
            .Where(r => r.UserId == userId)
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);

    public async Task<(IReadOnlyList<Receipt> Items, int TotalCount)> ListAsync(
        Guid userId, ReceiptStatus? status, ReceiptFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Receipts.AsNoTracking().Where(r => r.UserId == userId);
        if (status is not null)
        {
            query = query.Where(r => r.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = $"%{EscapeLike(filter.Search.Trim())}%";
            query = query.Where(r =>
                EF.Functions.Like(r.VendorName!, pattern, LikeEscape)
                || (r.VendorName == null && EF.Functions.Like(r.FileName, pattern, LikeEscape)));
        }

        if (filter.CategoryId is { } categoryId)
        {
            query = query.Where(r => r.CategoryId == categoryId);
        }

        if (filter.From is { } from)
        {
            query = query.Where(r => r.ReceiptDate >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(r => r.ReceiptDate <= to);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(r => r.Number)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<Receipt?> GetForUpdateAsync(Guid userId, Guid receiptId, CancellationToken cancellationToken) =>
        db.Receipts
            .Include(r => r.LineItems)
            .SingleOrDefaultAsync(r => r.Id == receiptId && r.UserId == userId, cancellationToken);

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

    public void Remove(Receipt receipt) => db.Receipts.Remove(receipt);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    private const string LikeEscape = "\\";

    private static string EscapeLike(string text) =>
        text.Replace(LikeEscape, LikeEscape + LikeEscape)
            .Replace("%", LikeEscape + "%")
            .Replace("_", LikeEscape + "_")
            .Replace("[", LikeEscape + "[");

    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
