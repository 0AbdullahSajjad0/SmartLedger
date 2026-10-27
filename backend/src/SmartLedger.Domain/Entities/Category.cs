using SmartLedger.Domain.Common;

namespace SmartLedger.Domain.Entities;

public class Category : Entity
{
    public Guid UserId { get; set; }

    public required string Name { get; set; }

    /// <summary>Hex color shown next to the category, e.g. "#3D4A2F".</summary>
    public required string Color { get; set; }

    /// <summary>Applies to every month, in the user's base currency. Null means the category has no budget.</summary>
    public decimal? MonthlyBudget { get; set; }

    /// <summary>Archived categories are hidden from pickers but stay on existing receipts.</summary>
    public bool IsArchived { get; set; }
}
