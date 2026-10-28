using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmartLedger.Application.ExchangeRates;
using SmartLedger.Domain.Entities;
using SmartLedger.Infrastructure.Persistence;

namespace SmartLedger.Infrastructure.Repositories;

internal sealed class ExchangeRateRepository(AppDbContext db) : IExchangeRateRepository
{
    public async Task<IReadOnlyDictionary<string, decimal>> GetAsync(
        DateOnly date, IReadOnlyCollection<string> currencies, CancellationToken cancellationToken) =>
        await db.ExchangeRates
            .AsNoTracking()
            .Where(r => r.EffectiveDate == date && currencies.Contains(r.Currency))
            .ToDictionaryAsync(r => r.Currency, r => r.UnitsPerUsd, cancellationToken);

    public async Task SaveAsync(DailyRates rates, CancellationToken cancellationToken)
    {
        var existing = await db.ExchangeRates
            .Where(r => r.EffectiveDate == rates.Date)
            .Select(r => r.Currency)
            .ToListAsync(cancellationToken);

        var added = rates.UnitsPerUsd
            .Where(rate => !existing.Contains(rate.Key))
            .Select(rate => new ExchangeRate
            {
                Currency = rate.Key,
                UnitsPerUsd = rate.Value,
                EffectiveDate = rates.Date,
                Source = rates.Source,
            })
            .ToList();
        if (added.Count == 0)
        {
            return;
        }

        db.ExchangeRates.AddRange(added);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Another request stored the same day a moment earlier; its rows are just as good. Forget ours, or the
            // next save on this context (e.g. the approval itself) would try to insert them again.
            foreach (var entry in db.ChangeTracker.Entries<ExchangeRate>().Where(e => e.State == EntityState.Added).ToList())
            {
                entry.State = EntityState.Detached;
            }
        }
    }
}
