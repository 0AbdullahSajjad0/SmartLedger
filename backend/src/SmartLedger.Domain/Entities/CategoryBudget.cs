using SmartLedger.Domain.Common;

namespace SmartLedger.Domain.Entities;

public class CategoryBudget : Entity
{
    public Guid CategoryId { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public decimal? Amount { get; set; }

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
