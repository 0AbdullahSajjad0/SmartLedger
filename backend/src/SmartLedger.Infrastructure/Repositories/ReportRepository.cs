using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmartLedger.Application.Reports;
using SmartLedger.Domain.Entities;
using SmartLedger.Domain.Enums;
using SmartLedger.Infrastructure.Persistence;

namespace SmartLedger.Infrastructure.Repositories;

internal sealed class ReportRepository(AppDbContext db) : IReportRepository
{
    public async Task<IReadOnlyList<MonthSpending>> MonthlyTotalsAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await Approved(userId, from, to)
            .GroupBy(r => new { r.ReceiptDate!.Value.Year, r.ReceiptDate!.Value.Month })
            .Select(g => new MonthSpending(g.Key.Year, g.Key.Month, g.Sum(r => r.BaseCurrencyTotal ?? 0m)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Receipt>> ApprovedAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await Approved(userId, from, to)
            .AsNoTracking()
            .OrderBy(r => r.ReceiptDate)
            .ThenBy(r => r.Number)
            .ToListAsync(cancellationToken);

    public Task<MonthlyInsight?> GetInsightAsync(Guid userId, int year, int month, CancellationToken cancellationToken) =>
        db.MonthlyInsights.SingleOrDefaultAsync(i => i.UserId == userId && i.Year == year && i.Month == month, cancellationToken);

    public void AddInsight(MonthlyInsight insight) => db.MonthlyInsights.Add(insight);

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

    private IQueryable<Receipt> Approved(Guid userId, DateOnly from, DateOnly to) =>
        db.Receipts.Where(r =>
            r.UserId == userId && r.Status == ReceiptStatus.Approved && r.ReceiptDate >= from && r.ReceiptDate < to);
}
