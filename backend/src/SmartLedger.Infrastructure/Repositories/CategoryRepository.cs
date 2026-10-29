using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmartLedger.Application.Categories;
using SmartLedger.Domain.Entities;
using SmartLedger.Domain.Enums;
using SmartLedger.Infrastructure.Persistence;

namespace SmartLedger.Infrastructure.Repositories;

internal sealed class CategoryRepository(AppDbContext db) : ICategoryRepository
{
    public async Task<IReadOnlyList<Category>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Categories
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

    public Task<Category?> GetForUpdateAsync(Guid userId, Guid categoryId, CancellationToken cancellationToken) =>
        db.Categories.SingleOrDefaultAsync(c => c.Id == categoryId && c.UserId == userId, cancellationToken);

    public void Add(Category category) => db.Categories.Add(category);

    // One grouped query in SQL (it uses the (UserId, ReceiptDate) index), not a list of receipts summed in memory.
    public async Task<IReadOnlyList<CategorySpending>> SpendingAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.Receipts
            .Where(r => r.UserId == userId && r.Status == ReceiptStatus.Approved && r.ReceiptDate >= from && r.ReceiptDate < to)
            .GroupBy(r => r.CategoryId)
            .Select(g => new CategorySpending(g.Key, g.Count(), g.Sum(r => r.BaseCurrencyTotal ?? 0m)))
            .ToListAsync(cancellationToken);

    public async Task<bool> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        // The unique (UserId, Name) index: another request created or renamed a category to the same name first.
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return false;
        }
    }
}
