using SmartLedger.Domain.Entities;

namespace SmartLedger.Infrastructure.Identity;

/// <summary>The categories every new account starts with. Users can rename, archive or budget them later.</summary>
internal static class DefaultCategories
{
    public static IEnumerable<Category> For(Guid userId)
    {
        (string Name, string Color)[] defaults =
        [
            ("Travel", "#3D4A2F"),
            ("Software & Subscriptions", "#5B6B73"),
            ("Lodging", "#7A5C45"),
            ("Food & Dining", "#B08A2E"),
            ("Office Supplies", "#8C8270"),
            ("Utilities", "#4A4038"),
        ];

        return defaults.Select(d => new Category { UserId = userId, Name = d.Name, Color = d.Color });
    }
}
