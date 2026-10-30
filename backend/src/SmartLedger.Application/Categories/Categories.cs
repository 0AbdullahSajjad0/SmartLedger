using System.ComponentModel.DataAnnotations;
using SmartLedger.Application.Common;
using SmartLedger.Domain.Entities;

namespace SmartLedger.Application.Categories;

public sealed record CategoryDto(Guid Id, string Name, string Color, decimal? MonthlyBudget, bool IsArchived);

public sealed record CategorySpendingDto(
    Guid Id, string Name, string Color, decimal? MonthlyBudget, bool IsArchived, int ReceiptCount, decimal Spent);

public sealed record CategoryMonthDto(
    string Month,
    string BaseCurrency,
    decimal TotalBudget,
    decimal TotalSpent,
    IReadOnlyList<CategorySpendingDto> Categories,
    bool IsClosed);

public sealed record CreateCategoryRequest(
    [Required, StringLength(Category.MaxNameLength, MinimumLength = 1)] string Name,
    [Required] string Color,
    [Range(typeof(decimal), "0", "999999999999")] decimal? MonthlyBudget);

public sealed record UpdateCategoryRequest(
    [Required, StringLength(Category.MaxNameLength, MinimumLength = 1)] string Name,
    [Required] string Color,
    [Range(typeof(decimal), "0", "999999999999")] decimal? MonthlyBudget,
    bool IsArchived);

public static class CategoryPalette
{
    public static readonly IReadOnlyList<string> Colors =
    [
        "#3D4A2F",
        "#5B6B73",
        "#7A5C45",
        "#B08A2E",
        "#8C8270",
        "#4A4038",
        "#2F5D62",
        "#6E4F6B",
        "#556B5D",
        "#A07D5C",
    ];

    public static string? Find(string color) =>
        Colors.FirstOrDefault(c => string.Equals(c, color.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed record CategorySpending(Guid? CategoryId, int ReceiptCount, decimal Spent);

public interface ICategoryRepository
{
    Task<IReadOnlyList<Category>> ListAsync(Guid userId, CancellationToken cancellationToken);

    Task<Category?> GetForUpdateAsync(Guid userId, Guid categoryId, CancellationToken cancellationToken);

    void Add(Category category);

    Task<IReadOnlyList<CategorySpending>> SpendingAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, decimal?>> BudgetsAsync(Guid userId, DateOnly month, CancellationToken cancellationToken);

    Task<List<CategoryBudget>> BudgetHistoryForUpdateAsync(Guid categoryId, CancellationToken cancellationToken);

    void AddBudget(CategoryBudget budget);

    void RemoveBudget(CategoryBudget budget);

    Task<bool> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken cancellationToken);

    Task<Result<CategoryMonthDto>> GetMonthAsync(string? month, CancellationToken cancellationToken);

    Task<Result<CategoryDto>> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken);

    Task<Result<CategoryDto>> UpdateAsync(Guid categoryId, UpdateCategoryRequest request, CancellationToken cancellationToken);
}
