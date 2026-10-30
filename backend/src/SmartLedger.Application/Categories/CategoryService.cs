using SmartLedger.Application.Common;
using SmartLedger.Domain.Entities;

namespace SmartLedger.Application.Categories;

internal sealed class CategoryService(
    ICategoryRepository repository,
    IUserDirectory users,
    TimeProvider timeProvider,
    ICurrentUser currentUser) : ICategoryService
{
    public const int MaxCategories = 50;

    private static readonly Error NotFound = Error.NotFound("categories.not_found", "Category not found.");

    private Guid UserId => currentUser.UserId ?? throw new InvalidOperationException("No signed-in user.");

    private DateOnly CurrentMonth => CategoryBudget.FirstOfMonth(DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));

    public async Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken cancellationToken)
    {
        var categories = await repository.ListAsync(UserId, cancellationToken);
        var budgets = await repository.BudgetsAsync(UserId, CurrentMonth, cancellationToken);
        return categories.Select(c => ToDto(c, budgets.GetValueOrDefault(c.Id))).ToList();
    }

    public async Task<Result<CategoryMonthDto>> GetMonthAsync(string? month, CancellationToken cancellationToken)
    {
        var parsed = Months.Parse(month, DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));
        if (!parsed.IsSuccess)
        {
            return parsed.Error!;
        }
        var start = parsed.Value;

        var categories = await repository.ListAsync(UserId, cancellationToken);
        var budgets = await repository.BudgetsAsync(UserId, start, cancellationToken);
        var spending = await repository.SpendingAsync(UserId, start, start.AddMonths(1), cancellationToken);
        var byCategory = spending.Where(s => s.CategoryId is not null).ToDictionary(s => s.CategoryId!.Value);

        var rows = categories
            .Select(c =>
            {
                byCategory.TryGetValue(c.Id, out var spent);
                return new CategorySpendingDto(
                    c.Id, c.Name, c.Color, budgets.GetValueOrDefault(c.Id), c.IsArchived, spent?.ReceiptCount ?? 0, spent?.Spent ?? 0m);
            })
            .OrderBy(c => c.IsArchived)
            .ThenByDescending(c => c.Spent)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new CategoryMonthDto(
            Months.Format(start),
            await users.GetBaseCurrencyAsync(UserId, cancellationToken) ?? "USD",
            budgets.Values.Sum(b => b ?? 0m),
            spending.Sum(s => s.Spent),
            rows,
            IsClosed: start < CurrentMonth);
    }

    public async Task<Result<CategoryDto>> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var existing = await repository.ListAsync(UserId, cancellationToken);
        if (existing.Count >= MaxCategories)
        {
            return Error.Validation("categories.too_many", $"You can have up to {MaxCategories} categories. Archive or rename one instead.");
        }

        var checkedFields = Check(request.Name, request.Color, existing, categoryId: null);
        if (!checkedFields.IsSuccess)
        {
            return checkedFields.Error!;
        }

        var category = new Category
        {
            UserId = UserId,
            Name = checkedFields.Value.Name,
            Color = checkedFields.Value.Color,
        };
        repository.Add(category);
        var budget = SetBudget(category.Id, history: [], Money(request.MonthlyBudget));

        return await repository.SaveChangesAsync(cancellationToken) ? ToDto(category, budget) : DuplicateName(category.Name);
    }

    public async Task<Result<CategoryDto>> UpdateAsync(
        Guid categoryId, UpdateCategoryRequest request, CancellationToken cancellationToken)
    {
        var category = await repository.GetForUpdateAsync(UserId, categoryId, cancellationToken);
        if (category is null)
        {
            return NotFound;
        }

        var existing = await repository.ListAsync(UserId, cancellationToken);
        var checkedFields = Check(request.Name, request.Color, existing, categoryId);
        if (!checkedFields.IsSuccess)
        {
            return checkedFields.Error!;
        }

        if (request.IsArchived && !category.IsArchived && existing.Count(c => !c.IsArchived) <= 1)
        {
            return Error.Validation("categories.last_active", "Keep at least one active category: AI files receipts under them.");
        }

        category.Name = checkedFields.Value.Name;
        category.Color = checkedFields.Value.Color;
        category.IsArchived = request.IsArchived;

        var history = await repository.BudgetHistoryForUpdateAsync(category.Id, cancellationToken);
        var budget = SetBudget(category.Id, history, request.IsArchived ? null : Money(request.MonthlyBudget));

        return await repository.SaveChangesAsync(cancellationToken) ? ToDto(category, budget) : DuplicateName(category.Name);
    }

    private decimal? SetBudget(Guid categoryId, List<CategoryBudget> history, decimal? amount)
    {
        var month = CurrentMonth;
        var thisMonth = history.SingleOrDefault(b => b.EffectiveFrom == month);
        var before = CategoryBudget.For(history.Where(b => b.EffectiveFrom < month), month);

        if (amount == before)
        {
            if (thisMonth is not null)
            {
                repository.RemoveBudget(thisMonth);
            }
        }
        else if (thisMonth is not null)
        {
            thisMonth.Amount = amount;
        }
        else
        {
            repository.AddBudget(new CategoryBudget { CategoryId = categoryId, EffectiveFrom = month, Amount = amount });
        }

        return amount;
    }

    private static Result<(string Name, string Color)> Check(
        string name, string color, IReadOnlyList<Category> existing, Guid? categoryId)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return Error.Validation("categories.empty_name", "Give the category a name.");
        }

        var paletteColor = CategoryPalette.Find(color);
        if (paletteColor is null)
        {
            return Error.Validation("categories.unknown_color", "Choose one of the palette's colours.");
        }

        var clash = existing.FirstOrDefault(c => c.Id != categoryId && string.Equals(c.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (clash is not null)
        {
            return clash.IsArchived
                ? Error.Conflict("categories.duplicate_name", $"You have an archived category called \"{clash.Name}\". Unarchive it instead.")
                : DuplicateName(clash.Name);
        }

        return (trimmed, paletteColor);
    }

    private static Error DuplicateName(string name) =>
        Error.Conflict("categories.duplicate_name", $"You already have a category called \"{name}\".");

    private static decimal? Money(decimal? value) =>
        value is null ? null : Math.Round(value.Value, 2, MidpointRounding.AwayFromZero);

    private static CategoryDto ToDto(Category c, decimal? budget) => new(c.Id, c.Name, c.Color, budget, c.IsArchived);
}
