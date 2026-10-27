using SmartLedger.Domain.Common;

namespace SmartLedger.Domain.Entities;

/// <summary>AI-written summary of one user's spending for one month.</summary>
public class MonthlyInsight : Entity
{
    public Guid UserId { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    /// <summary>One-sentence summary shown as the report's lede.</summary>
    public required string Headline { get; set; }

    /// <summary>The full analysis text.</summary>
    public required string Body { get; set; }

    /// <summary>How many approved receipts the analysis was based on.</summary>
    public int ReceiptCount { get; set; }

    /// <summary>The AI model that wrote it.</summary>
    public required string Model { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }
}
