using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmartLedger.Application.Budgets;
using SmartLedger.Domain.Entities;
using SmartLedger.Domain.Enums;
using SmartLedger.Infrastructure.Email;
using SmartLedger.Infrastructure.Persistence;

namespace SmartLedger.Infrastructure.Repositories;

internal sealed class BudgetAlertRepository(AppDbContext db) : IBudgetAlertRepository
{
    public async Task<IReadOnlyList<BudgetAlertLevel>> SentLevelsAsync(
        Guid categoryId, int year, int month, CancellationToken cancellationToken) =>
        await db.BudgetAlerts
            .Where(a => a.CategoryId == categoryId && a.Year == year && a.Month == month)
            .Select(a => a.Level)
            .ToListAsync(cancellationToken);

    public void Add(BudgetAlert alert) => db.BudgetAlerts.Add(alert);

    public async Task<bool> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        // The unique (CategoryId, Year, Month, Level) index: another approval recorded this alert first.
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Forget the rejected alert and its email, so a later save in this request doesn't try them again.
            foreach (var entry in db.ChangeTracker.Entries()
                         .Where(e => e.State == EntityState.Added && e.Entity is BudgetAlert or OutgoingEmail)
                         .ToList())
            {
                entry.State = EntityState.Detached;
            }
            return false;
        }
    }
}
