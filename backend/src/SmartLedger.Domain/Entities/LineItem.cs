using SmartLedger.Domain.Common;

namespace SmartLedger.Domain.Entities;

/// <summary>One line on a receipt, in the receipt's own currency.</summary>
public class LineItem : Entity
{
    public Guid ReceiptId { get; set; }

    /// <summary>Position on the receipt, starting at 1.</summary>
    public int LineNumber { get; set; }

    public required string Description { get; set; }

    public decimal Quantity { get; set; } = 1;

    public decimal UnitPrice { get; set; }

    public decimal Amount { get; set; }
}
