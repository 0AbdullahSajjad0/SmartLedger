namespace SmartLedger.Domain.Enums;

public enum BudgetAlertLevel
{
    /// <summary>Spending reached 80% of the category's monthly budget.</summary>
    Warning = 80,

    /// <summary>Spending went past the whole budget.</summary>
    Exceeded = 100,
}
