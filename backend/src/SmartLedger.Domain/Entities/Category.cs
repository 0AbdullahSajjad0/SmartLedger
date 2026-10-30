using SmartLedger.Domain.Common;

namespace SmartLedger.Domain.Entities;

public class Category : Entity
{
    public const int MaxNameLength = 60;

    public Guid UserId { get; set; }

    public required string Name { get; set; }

    /// <summary>Hex color shown next to the category, e.g. "#3D4A2F".</summary>
    public required string Color { get; set; }

    /// <summary>
    /// Archived categories are hidden from pickers but stay on existing receipts. Their budget ends the month they're
    /// archived; months before keep it. Budgets live in <see cref="CategoryBudget"/>, one entry per change.
    /// </summary>
    public bool IsArchived { get; set; }
}
