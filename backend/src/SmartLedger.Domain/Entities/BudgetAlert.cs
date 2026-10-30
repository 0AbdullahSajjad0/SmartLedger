using SmartLedger.Domain.Common;
using SmartLedger.Domain.Enums;

namespace SmartLedger.Domain.Entities;

/// <summary>
/// A category's spending crossed a budget level in a month, and the user was told by email. Each level is sent once
/// per category and month (a unique index enforces it), however many receipts are approved after it.
/// </summary>
public class BudgetAlert : Entity
{
    /// <summary>Spending at or above this share of the budget triggers a warning.</summary>
    public const decimal WarningShare = 0.8m;

    public Guid UserId { get; set; }

    public Guid CategoryId { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    public BudgetAlertLevel Level { get; set; }

    /// <summary>What had been spent when the level was crossed, in the user's base currency.</summary>
    public decimal Spent { get; set; }

    /// <summary>The monthly budget at that moment.</summary>
    public decimal Budget { get; set; }

    /// <summary>
    /// The level <paramref name="spent"/> has reached against <paramref name="budget"/>, if any. Exactly on budget is
    /// still a warning: "exceeded" means more was spent than planned, as on the Categories page.
    /// </summary>
    public static BudgetAlertLevel? LevelFor(decimal spent, decimal budget) =>
        budget <= 0 ? null
        : spent > budget ? BudgetAlertLevel.Exceeded
        : spent >= budget * WarningShare ? BudgetAlertLevel.Warning
        : null;
}
