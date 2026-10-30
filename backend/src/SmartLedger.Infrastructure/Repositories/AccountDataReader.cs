using Microsoft.EntityFrameworkCore;
using SmartLedger.Application.Account;
using SmartLedger.Infrastructure.Persistence;

namespace SmartLedger.Infrastructure.Repositories;

internal sealed class AccountDataReader(AppDbContext db) : IAccountDataReader
{
    public async Task<AccountData?> ReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new AccountProfile(u.Email!, u.DisplayName, u.BaseCurrency))
            .SingleOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return null;
        }

        var categories = await db.Categories
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var budgets = await db.CategoryBudgets
            .AsNoTracking()
            .Where(b => db.Categories.Any(c => c.Id == b.CategoryId && c.UserId == userId))
            .ToListAsync(cancellationToken);

        // Split query: one for receipts and one for their line items, instead of repeating every receipt per line.
        var receipts = await db.Receipts
            .AsNoTracking()
            .Include(r => r.LineItems)
            .Where(r => r.UserId == userId)
            .OrderBy(r => r.Number)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var insights = await db.MonthlyInsights
            .AsNoTracking()
            .Where(i => i.UserId == userId)
            .OrderBy(i => i.Year).ThenBy(i => i.Month)
            .ToListAsync(cancellationToken);

        return new AccountData(profile, categories, budgets, receipts, insights);
    }
}
