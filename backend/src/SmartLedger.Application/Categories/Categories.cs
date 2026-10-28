using SmartLedger.Application.Common;
using SmartLedger.Domain.Entities;

namespace SmartLedger.Application.Categories;

public sealed record CategoryDto(Guid Id, string Name, string Color, decimal? MonthlyBudget, bool IsArchived);

public interface ICategoryRepository
{
    /// <summary>All of the user's categories, archived ones included, by name.</summary>
    Task<IReadOnlyList<Category>> ListAsync(Guid userId, CancellationToken cancellationToken);
}

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken cancellationToken);
}

internal sealed class CategoryService(ICategoryRepository repository, ICurrentUser currentUser) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new InvalidOperationException("No signed-in user.");
        var categories = await repository.ListAsync(userId, cancellationToken);

        return categories
            .Select(c => new CategoryDto(c.Id, c.Name, c.Color, c.MonthlyBudget, c.IsArchived))
            .ToList();
    }
}
