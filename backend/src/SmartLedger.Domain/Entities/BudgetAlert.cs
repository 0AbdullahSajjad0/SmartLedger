using SmartLedger.Domain.Common;
using SmartLedger.Domain.Enums;

namespace SmartLedger.Domain.Entities;

public class BudgetAlert : Entity
{
    public const decimal WarningShare = 0.8m;

    public Guid UserId { get; set; }

    public Guid CategoryId { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    public BudgetAlertLevel Level { get; set; }

    public decimal Spent { get; set; }

    public decimal Budget { get; set; }

    public static BudgetAlertLevel? LevelFor(decimal spent, decimal budget) =>
        budget <= 0 ? null
        : spent > budget ? BudgetAlertLevel.Exceeded
        : spent >= budget * WarningShare ? BudgetAlertLevel.Warning
        : null;
}
