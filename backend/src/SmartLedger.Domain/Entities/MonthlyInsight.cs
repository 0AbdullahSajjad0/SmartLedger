using SmartLedger.Domain.Common;

namespace SmartLedger.Domain.Entities;

public class MonthlyInsight : Entity
{
    public Guid UserId { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    public required string Headline { get; set; }

    public required string Body { get; set; }

    public int ReceiptCount { get; set; }

    public decimal TotalSpent { get; set; }

    public required string Model { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }
}
