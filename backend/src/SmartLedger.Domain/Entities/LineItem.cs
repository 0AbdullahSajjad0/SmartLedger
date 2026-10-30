using SmartLedger.Domain.Common;

namespace SmartLedger.Domain.Entities;

public class LineItem : Entity
{
    public Guid ReceiptId { get; set; }

    public int LineNumber { get; set; }

    public required string Description { get; set; }

    public decimal Quantity { get; set; } = 1;

    public decimal UnitPrice { get; set; }

    public decimal Amount { get; set; }
}
