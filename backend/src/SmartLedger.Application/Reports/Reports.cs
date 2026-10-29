using SmartLedger.Application.Categories;
using SmartLedger.Application.Common;
using SmartLedger.Domain.Entities;

namespace SmartLedger.Application.Reports;

/// <summary>
/// A month in review: the figures, the six-month trend, where the money went, budgets against actual spending, and
/// AI's written analysis if one has been written. Approved receipts only, by receipt date, in the base currency.
/// </summary>
/// <param name="Month">"yyyy-MM".</param>
/// <param name="Trend">Six months ending with this one, oldest first.</param>
/// <param name="TopVendors">Up to five, biggest spend first.</param>
/// <param name="Categories">Every active category, and archived ones with spending: for budget against actual.</param>
/// <param name="Insight">Null until the user asks AI to write it.</param>
public sealed record MonthReportDto(
    string Month,
    string BaseCurrency,
    decimal Spent,
    int ApprovedCount,
    decimal PreviousMonthSpent,
    decimal TotalBudget,
    IReadOnlyList<MonthTotalDto> Trend,
    IReadOnlyList<VendorSpendingDto> TopVendors,
    IReadOnlyList<CategorySpendingDto> Categories,
    InsightDto? Insight);

/// <param name="Month">"yyyy-MM".</param>
public sealed record MonthTotalDto(string Month, decimal Spent);

/// <param name="CategoryName">The category most of the vendor's spending was filed under.</param>
public sealed record VendorSpendingDto(string Vendor, string? CategoryName, int ReceiptCount, decimal Spent);

/// <summary>AI's analysis of a month.</summary>
/// <param name="IsOutOfDate">Receipts were approved or reopened since it was written, so it no longer matches the figures.</param>
public sealed record InsightDto(
    string Headline, IReadOnlyList<string> Paragraphs, int ReceiptCount, DateTimeOffset GeneratedAt, bool IsOutOfDate);

/// <summary>A file to download, e.g. the month's receipts as CSV.</summary>
public sealed record ReportFile(byte[] Content, string ContentType, string FileName);

public interface IReportService
{
    /// <param name="month">"yyyy-MM"; null for the current month.</param>
    Task<Result<MonthReportDto>> GetAsync(string? month, CancellationToken cancellationToken);

    /// <summary>Asks AI to write (or rewrite) the month's analysis, and keeps it.</summary>
    Task<Result<InsightDto>> WriteInsightAsync(string? month, CancellationToken cancellationToken);

    /// <summary>The month's approved receipts as a CSV file, for a spreadsheet or an accountant.</summary>
    Task<Result<ReportFile>> ExportCsvAsync(string? month, CancellationToken cancellationToken);
}

/// <summary>Approved receipts' base-currency total in one calendar month.</summary>
public sealed record MonthSpending(int Year, int Month, decimal Spent);

public interface IReportRepository
{
    /// <summary>Approved receipts dated from <paramref name="from"/> up to (not including) <paramref name="to"/>, by month.</summary>
    Task<IReadOnlyList<MonthSpending>> MonthlyTotalsAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>The approved receipts themselves (without line items), oldest first.</summary>
    Task<IReadOnlyList<Receipt>> ApprovedAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>The month's analysis, tracked for changes. Null when none has been written.</summary>
    Task<MonthlyInsight?> GetInsightAsync(Guid userId, int year, int month, CancellationToken cancellationToken);

    void AddInsight(MonthlyInsight insight);

    /// <returns>False when another request stored the same month's analysis first.</returns>
    Task<bool> SaveChangesAsync(CancellationToken cancellationToken);
}

// ---- Writing the analysis ----

/// <summary>Everything the AI may see about a month: totals and names only, never notes, card numbers or images.</summary>
/// <param name="MonthName">"October 2026".</param>
/// <param name="PreviousMonthName">"September 2026".</param>
public sealed record InsightRequest(
    string MonthName,
    string BaseCurrency,
    decimal Spent,
    int ReceiptCount,
    decimal TotalBudget,
    string PreviousMonthName,
    decimal PreviousMonthSpent,
    IReadOnlyList<InsightCategory> Categories,
    IReadOnlyList<VendorSpendingDto> TopVendors,
    IReadOnlyList<InsightReceipt> LargestReceipts);

public sealed record InsightCategory(string Name, decimal Spent, decimal? Budget, int ReceiptCount);

public sealed record InsightReceipt(string Vendor, DateOnly Date, string? Category, decimal Amount);

/// <summary>What the AI wrote, already checked: a one-sentence headline and a few short paragraphs.</summary>
public sealed record WrittenInsight(string Headline, IReadOnlyList<string> Paragraphs, string Model);

/// <summary>Writes the monthly analysis. Gemini in production, a fake in tests.</summary>
public interface IInsightWriter
{
    /// <exception cref="InsightUnavailableException">The AI service couldn't be used right now.</exception>
    Task<WrittenInsight> WriteAsync(InsightRequest request, CancellationToken cancellationToken);
}

/// <summary>The AI service failed (rate limit, outage, missing key, an answer we couldn't use). Trying later may work.</summary>
public sealed class InsightUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
