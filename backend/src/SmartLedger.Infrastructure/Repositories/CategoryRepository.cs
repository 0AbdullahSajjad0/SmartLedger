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

    public async Task<IReadOnlyDictionary<Guid, decimal?>> BudgetsAsync(
        Guid userId, DateOnly month, CancellationToken cancellationToken)
    {
        var start = CategoryBudget.FirstOfMonth(month);

        var history = await db.CategoryBudgets
            .AsNoTracking()
            .Where(b => b.EffectiveFrom <= start && db.Categories.Any(c => c.Id == b.CategoryId && c.UserId == userId))
            .ToListAsync(cancellationToken);

        return history
            .GroupBy(b => b.CategoryId)
            .ToDictionary(g => g.Key, g => CategoryBudget.For(g, start));
    }

    public Task<List<CategoryBudget>> BudgetHistoryForUpdateAsync(Guid categoryId, CancellationToken cancellationToken) =>
        db.CategoryBudgets.Where(b => b.CategoryId == categoryId).ToListAsync(cancellationToken);

    public void AddBudget(CategoryBudget budget) => db.CategoryBudgets.Add(budget);

    public void RemoveBudget(CategoryBudget budget) => db.CategoryBudgets.Remove(budget);

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
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return false;
        }
    }
}
