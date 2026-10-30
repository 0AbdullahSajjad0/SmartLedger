using Microsoft.EntityFrameworkCore;
using SmartLedger.Application.Common;
using SmartLedger.Infrastructure.Persistence;

namespace SmartLedger.Infrastructure.Identity;

internal sealed class UserDirectory(AppDbContext db) : IUserDirectory
{
    public Task<string?> GetBaseCurrencyAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users
            .Where(u => u.Id == userId)
            .Select(u => (string?)u.BaseCurrency)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<bool> WantsBudgetAlertsAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Id == userId && u.BudgetAlerts && u.EmailConfirmed, cancellationToken);
}
