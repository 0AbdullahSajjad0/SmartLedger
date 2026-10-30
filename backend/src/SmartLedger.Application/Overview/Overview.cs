using SmartLedger.Application.Categories;
using SmartLedger.Application.Common;
using SmartLedger.Application.Receipts;
using SmartLedger.Application.Reports;
using SmartLedger.Domain.Enums;

namespace SmartLedger.Application.Overview;

public sealed record OverviewDto(
    string Month,
    string BaseCurrency,
    decimal Spent,
    int ApprovedCount,
    decimal TotalBudget,
    decimal PreviousMonthSpent,
    int AwaitingReview,
    int Unreadable,
    int TotalReceipts,
    IReadOnlyList<CategorySpendingDto> Categories,
    IReadOnlyList<ReceiptSummaryDto> Recent,
    string? InsightHeadline);

public interface IOverviewService
{
    Task<Result<OverviewDto>> GetAsync(string? month, CancellationToken cancellationToken);
}

internal sealed class OverviewService(
    ICategoryRepository categories,
    IReceiptRepository receipts,
    IReportRepository reports,
    IUserDirectory users,
    TimeProvider timeProvider,
    ICurrentUser currentUser) : IOverviewService
{
    public const int RecentCount = 5;

    private Guid UserId => currentUser.UserId ?? throw new InvalidOperationException("No signed-in user.");

    public async Task<Result<OverviewDto>> GetAsync(string? month, CancellationToken cancellationToken)
    {
        var parsed = Months.Parse(month, DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));
        if (!parsed.IsSuccess)
        {
            return parsed.Error!;
        }
        var start = parsed.Value;
        var userId = UserId;

        var thisMonth = await categories.SpendingAsync(userId, start, start.AddMonths(1), cancellationToken);
        var lastMonth = await categories.SpendingAsync(userId, start.AddMonths(-1), start, cancellationToken);
        var userCategories = await categories.ListAsync(userId, cancellationToken);
        var budgets = await categories.BudgetsAsync(userId, start, cancellationToken);
        var counts = await receipts.CountByStatusAsync(userId, cancellationToken);
        var (recent, _) = await receipts.ListAsync(userId, status: null, new ReceiptFilter(), page: 1, pageSize: RecentCount, cancellationToken);
        var baseCurrency = await users.GetBaseCurrencyAsync(userId, cancellationToken) ?? "USD";
        var insight = await reports.GetInsightAsync(userId, start.Year, start.Month, cancellationToken);

        var spentByCategory = thisMonth.Where(s => s.CategoryId is not null).ToDictionary(s => s.CategoryId!.Value);
        var categoryRows = userCategories
            .Where(c => spentByCategory.ContainsKey(c.Id))
            .Select(c =>
            {
                var spent = spentByCategory[c.Id];
                return new CategorySpendingDto(c.Id, c.Name, c.Color, budgets.GetValueOrDefault(c.Id), c.IsArchived, spent.ReceiptCount, spent.Spent);
            })
            .OrderByDescending(c => c.Spent)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new OverviewDto(
            Months.Format(start),
            baseCurrency,
            thisMonth.Sum(s => s.Spent),
            thisMonth.Sum(s => s.ReceiptCount),
            budgets.Values.Sum(b => b ?? 0m),
            lastMonth.Sum(s => s.Spent),
            counts.GetValueOrDefault(ReceiptStatus.NeedsReview),
            counts.GetValueOrDefault(ReceiptStatus.Failed),
            counts.Values.Sum(),
            categoryRows,
            recent.Select(ReceiptService.ToSummary).ToList(),
            insight?.Headline);
    }
}
