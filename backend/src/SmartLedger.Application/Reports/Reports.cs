using SmartLedger.Application.Categories;
using SmartLedger.Application.Common;
using SmartLedger.Domain.Entities;

namespace SmartLedger.Application.Reports;

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

public sealed record MonthTotalDto(string Month, decimal Spent);

public sealed record VendorSpendingDto(string Vendor, string? CategoryName, int ReceiptCount, decimal Spent);

public sealed record InsightDto(
    string Headline, IReadOnlyList<string> Paragraphs, int ReceiptCount, DateTimeOffset GeneratedAt, bool IsOutOfDate);

public sealed record ReportFile(byte[] Content, string ContentType, string FileName);

public interface IReportService
{
    Task<Result<MonthReportDto>> GetAsync(string? month, CancellationToken cancellationToken);

    Task<Result<InsightDto>> WriteInsightAsync(string? month, CancellationToken cancellationToken);

    Task<Result<ReportFile>> ExportCsvAsync(string? month, CancellationToken cancellationToken);
}

public sealed record MonthSpending(int Year, int Month, decimal Spent);

public interface IReportRepository
{
    Task<IReadOnlyList<MonthSpending>> MonthlyTotalsAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    Task<IReadOnlyList<Receipt>> ApprovedAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    Task<MonthlyInsight?> GetInsightAsync(Guid userId, int year, int month, CancellationToken cancellationToken);

    void AddInsight(MonthlyInsight insight);

    Task<bool> SaveChangesAsync(CancellationToken cancellationToken);
}

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

public sealed record WrittenInsight(string Headline, IReadOnlyList<string> Paragraphs, string Model);

public interface IInsightWriter
{
    Task<WrittenInsight> WriteAsync(InsightRequest request, CancellationToken cancellationToken);
}

public sealed class InsightUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
