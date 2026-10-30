using System.ComponentModel.DataAnnotations;
using SmartLedger.Application.Common;
using SmartLedger.Domain.Entities;

namespace SmartLedger.Application.Categories;

/// <param name="MonthlyBudget">This month's budget. Null means none.</param>
public sealed record CategoryDto(Guid Id, string Name, string Color, decimal? MonthlyBudget, bool IsArchived);

/// <summary>One category's month: the approved receipts filed under it, against its budget.</summary>
/// <param name="MonthlyBudget">The budget the category had in that month, which may differ from today's.</param>
/// <param name="Spent">Sum of the receipts' base-currency totals.</param>
public sealed record CategorySpendingDto(
    Guid Id, string Name, string Color, decimal? MonthlyBudget, bool IsArchived, int ReceiptCount, decimal Spent);

/// <summary>The categories page: every category with what was spent in one month, plus the totals.</summary>
/// <param name="Month">"yyyy-MM".</param>
/// <param name="TotalBudget">Sum of the budgets in force that month.</param>
/// <param name="TotalSpent">Everything approved in the month, archived categories included.</param>
/// <param name="IsClosed">A past month: its budgets are shown as they were and can no longer be changed.</param>
public sealed record CategoryMonthDto(
    string Month,
    string BaseCurrency,
    decimal TotalBudget,
    decimal TotalSpent,
    IReadOnlyList<CategorySpendingDto> Categories,
    bool IsClosed);

/// <param name="MonthlyBudget">
/// In the user's base currency, from this month on; earlier months keep the budget they had. Null means no budget.
/// </param>
public sealed record CreateCategoryRequest(
    [Required, StringLength(Category.MaxNameLength, MinimumLength = 1)] string Name,
    [Required] string Color,
    [Range(typeof(decimal), "0", "999999999999")] decimal? MonthlyBudget);

/// <param name="MonthlyBudget">From this month on, as for a new category; earlier months keep theirs.</param>
/// <param name="IsArchived">
/// Archived categories leave the pickers and AI's choices, but stay on their receipts. Archiving ends the budget from
/// this month (the budget sent is ignored); unarchiving starts again with the budget sent.
/// </param>
public sealed record UpdateCategoryRequest(
    [Required, StringLength(Category.MaxNameLength, MinimumLength = 1)] string Name,
    [Required] string Color,
    [Range(typeof(decimal), "0", "999999999999")] decimal? MonthlyBudget,
    bool IsArchived);

/// <summary>
/// The colours a category can have: muted earth tones that sit with the design's espresso, cream and terra cotta.
/// A fixed set keeps charts on-brand and readable; the first six are the default categories' colours.
/// </summary>
public static class CategoryPalette
{
    public static readonly IReadOnlyList<string> Colors =
    [
        "#3D4A2F", // olive
        "#5B6B73", // slate
        "#7A5C45", // walnut
        "#B08A2E", // mustard
        "#8C8270", // taupe
        "#4A4038", // umber
        "#2F5D62", // deep teal
        "#6E4F6B", // plum
        "#556B5D", // sage
        "#A07D5C", // sand
    ];

    /// <summary>The palette's spelling of the colour (upper case), or null when it isn't one of them.</summary>
    public static string? Find(string color) =>
        Colors.FirstOrDefault(c => string.Equals(c, color.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>Approved receipts under one category in a period. CategoryId is null for receipts without one.</summary>
public sealed record CategorySpending(Guid? CategoryId, int ReceiptCount, decimal Spent);

public interface ICategoryRepository
{
    /// <summary>All of the user's categories, archived ones included, by name.</summary>
    Task<IReadOnlyList<Category>> ListAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The user's category, tracked for changes. Null when it isn't theirs.</summary>
    Task<Category?> GetForUpdateAsync(Guid userId, Guid categoryId, CancellationToken cancellationToken);

    void Add(Category category);

    /// <summary>Approved receipts dated from <paramref name="from"/> up to (not including) <paramref name="to"/>, by category.</summary>
    Task<IReadOnlyList<CategorySpending>> SpendingAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>
    /// Each of the user's categories' budget in <paramref name="month"/>, as it was then. Categories without one are
    /// left out (or null), so <c>GetValueOrDefault</c> gives "no budget".
    /// </summary>
    Task<IReadOnlyDictionary<Guid, decimal?>> BudgetsAsync(Guid userId, DateOnly month, CancellationToken cancellationToken);

    /// <summary>A category's whole budget history, tracked for changes.</summary>
    Task<List<CategoryBudget>> BudgetHistoryForUpdateAsync(Guid categoryId, CancellationToken cancellationToken);

    void AddBudget(CategoryBudget budget);

    void RemoveBudget(CategoryBudget budget);

    /// <returns>False when the database turned the change down because the user already has a category with that name.</returns>
    Task<bool> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Every category with its spending in a month.</summary>
    /// <param name="month">"yyyy-MM"; null for the current month.</param>
    Task<Result<CategoryMonthDto>> GetMonthAsync(string? month, CancellationToken cancellationToken);

    Task<Result<CategoryDto>> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken);

    Task<Result<CategoryDto>> UpdateAsync(Guid categoryId, UpdateCategoryRequest request, CancellationToken cancellationToken);
}
