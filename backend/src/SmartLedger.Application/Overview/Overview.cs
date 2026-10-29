using SmartLedger.Application.Categories;
using SmartLedger.Application.Common;
using SmartLedger.Application.Receipts;
using SmartLedger.Domain.Enums;

namespace SmartLedger.Application.Overview;

/// <summary>
/// The monthly statement on the Overview page, in one request. Figures that are only arithmetic on these (average
/// receipt, change on last month, categories over budget, shares) are worked out by the page.
/// </summary>
/// <param name="Month">"yyyy-MM".</param>
/// <param name="Spent">Approved receipts dated in the month, in the base currency.</param>
/// <param name="ApprovedCount">How many receipts make up <paramref name="Spent"/>.</param>
/// <param name="TotalBudget">Sum of the active categories' monthly budgets; zero when none are set.</param>
/// <param name="PreviousMonthSpent">The same for the month before, for the change.</param>
/// <param name="AwaitingReview">Read by AI and waiting for the user, from any month.</param>
/// <param name="Unreadable">AI couldn't read them; they need filling in or rejecting.</param>
/// <param name="TotalReceipts">Every receipt the user has, in any status: zero means a brand-new account.</param>
/// <param name="Categories">Categories with spending in the month, biggest first, with their budgets.</param>
/// <param name="Recent">The newest entries in the ledger, in any status.</param>
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
    IReadOnlyList<ReceiptSummaryDto> Recent);

public interface IOverviewService
{
    /// <param name="month">"yyyy-MM"; null for the current month.</param>
    Task<Result<OverviewDto>> GetAsync(string? month, CancellationToken cancellationToken);
}

internal sealed class OverviewService(
    ICategoryRepository categories,
    IReceiptRepository receipts,
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

        // One DbContext per request can't run queries side by side, so these go one after another; each is a
        // small grouped or paged query.
        var thisMonth = await categories.SpendingAsync(userId, start, start.AddMonths(1), cancellationToken);
        var lastMonth = await categories.SpendingAsync(userId, start.AddMonths(-1), start, cancellationToken);
        var userCategories = await categories.ListAsync(userId, cancellationToken);
        var counts = await receipts.CountByStatusAsync(userId, cancellationToken);
        var (recent, _) = await receipts.ListAsync(userId, status: null, page: 1, pageSize: RecentCount, cancellationToken);
        var baseCurrency = await users.GetBaseCurrencyAsync(userId, cancellationToken) ?? "USD";

        var spentByCategory = thisMonth.Where(s => s.CategoryId is not null).ToDictionary(s => s.CategoryId!.Value);
        var categoryRows = userCategories
            .Where(c => spentByCategory.ContainsKey(c.Id))
            .Select(c =>
            {
                var spent = spentByCategory[c.Id];
                return new CategorySpendingDto(c.Id, c.Name, c.Color, c.MonthlyBudget, c.IsArchived, spent.ReceiptCount, spent.Spent);
            })
            .OrderByDescending(c => c.Spent)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new OverviewDto(
            Months.Format(start),
            baseCurrency,
            thisMonth.Sum(s => s.Spent),
            thisMonth.Sum(s => s.ReceiptCount),
            userCategories.Where(c => !c.IsArchived).Sum(c => c.MonthlyBudget ?? 0m),
            lastMonth.Sum(s => s.Spent),
            counts.GetValueOrDefault(ReceiptStatus.NeedsReview),
            counts.GetValueOrDefault(ReceiptStatus.Failed),
            counts.Values.Sum(),
            categoryRows,
            recent.Select(ReceiptService.ToSummary).ToList());
    }
}
