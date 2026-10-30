using Microsoft.Extensions.Logging;
using SmartLedger.Application.Categories;
using SmartLedger.Application.Common;
using SmartLedger.Application.Email;
using SmartLedger.Domain.Entities;
using SmartLedger.Domain.Enums;

namespace SmartLedger.Application.Budgets;

public interface IBudgetAlertRepository
{
    /// <summary>The levels already sent for this category and month.</summary>
    Task<IReadOnlyList<BudgetAlertLevel>> SentLevelsAsync(Guid categoryId, int year, int month, CancellationToken cancellationToken);

    void Add(BudgetAlert alert);

    /// <returns>False when another request recorded the same alert first (the unique index turned it down).</returns>
    Task<bool> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IBudgetAlertService
{
    /// <summary>
    /// Called after a receipt is approved: emails the user if its category has now reached 80% or 100% of its budget
    /// for the month. Never fails the approval; problems are logged.
    /// </summary>
    Task CheckAsync(Receipt approved, CancellationToken cancellationToken);
}

internal sealed class BudgetAlertService(
    IBudgetAlertRepository alerts,
    ICategoryRepository categories,
    IUserDirectory users,
    IEmailOutbox outbox,
    TimeProvider timeProvider,
    ILogger<BudgetAlertService> logger) : IBudgetAlertService
{
    public async Task CheckAsync(Receipt approved, CancellationToken cancellationToken)
    {
        try
        {
            await CheckOrThrowAsync(approved, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Checking budgets after approving receipt {ReceiptId} failed; no alert was sent", approved.Id);
        }
    }

    private async Task CheckOrThrowAsync(Receipt approved, CancellationToken cancellationToken)
    {
        if (approved.CategoryId is not { } categoryId || approved.ReceiptDate is not { } date)
        {
            return;
        }

        // Only this month: an alert is a nudge while there's still time to act, not news about a month that's over.
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (date.Year != today.Year || date.Month != today.Month)
        {
            return;
        }

        // This month's budget (archived categories have none from the month they were archived).
        var month = new DateOnly(date.Year, date.Month, 1);
        var budgets = await categories.BudgetsAsync(approved.UserId, month, cancellationToken);
        if (budgets.GetValueOrDefault(categoryId) is not { } budget)
        {
            return;
        }

        if (!await users.WantsBudgetAlertsAsync(approved.UserId, cancellationToken))
        {
            return;
        }

        var spending = await categories.SpendingAsync(approved.UserId, month, month.AddMonths(1), cancellationToken);
        var spent = spending.FirstOrDefault(s => s.CategoryId == categoryId)?.Spent ?? 0m;

        if (BudgetAlert.LevelFor(spent, budget) is not { } level)
        {
            return;
        }

        // Going straight past 100% sends only "exceeded"; a warning after that would be old news.
        var sent = await alerts.SentLevelsAsync(categoryId, month.Year, month.Month, cancellationToken);
        if (sent.Contains(level) || (level == BudgetAlertLevel.Warning && sent.Contains(BudgetAlertLevel.Exceeded)))
        {
            return;
        }

        var alert = new BudgetAlert
        {
            UserId = approved.UserId,
            CategoryId = categoryId,
            Year = month.Year,
            Month = month.Month,
            Level = level,
            Spent = spent,
            Budget = budget,
        };
        alerts.Add(alert);
        outbox.Add(EmailKind.BudgetAlert, approved.UserId, alert.Id);

        if (await alerts.SaveChangesAsync(cancellationToken))
        {
            outbox.Wake();
        }
    }
}
