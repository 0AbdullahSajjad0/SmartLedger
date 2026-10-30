using SmartLedger.Domain.Common;

namespace SmartLedger.Domain.Entities;

/// <summary>
/// A category's monthly budget from one month on, until a later entry replaces it. A month's budget is the entry with
/// the latest start on or before that month, so changing a budget never rewrites the months before the change:
/// a report for March shows March's budget, whatever it is today.
/// </summary>
public class CategoryBudget : Entity
{
    public Guid CategoryId { get; set; }

    /// <summary>The first day of the first month this amount applies to.</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>In the user's base currency. Null means "no budget" from this month (e.g. after archiving).</summary>
    public decimal? Amount { get; set; }

    /// <summary>The budget a category had in <paramref name="month"/>, from its history; null when it had none.</summary>
    public static decimal? For(IEnumerable<CategoryBudget> history, DateOnly month)
    {
        var start = FirstOfMonth(month);
        return history
            .Where(b => b.EffectiveFrom <= start)
            .MaxBy(b => b.EffectiveFrom)
            ?.Amount;
    }

    public static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);
}
