using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using SmartLedger.Application.Categories;
using SmartLedger.Application.Common;
using SmartLedger.Domain.Entities;

namespace SmartLedger.Application.Reports;

internal sealed class ReportService(
    ICategoryRepository categories,
    IReportRepository reports,
    IUserDirectory users,
    IInsightWriter insightWriter,
    TimeProvider timeProvider,
    ICurrentUser currentUser,
    ILogger<ReportService> logger) : IReportService
{
    public const int TrendMonths = 6;
    public const int TopVendorCount = 5;
    public const int LargestReceiptCount = 5;

    private Guid UserId => currentUser.UserId ?? throw new InvalidOperationException("No signed-in user.");

    public async Task<Result<MonthReportDto>> GetAsync(string? month, CancellationToken cancellationToken)
    {
        var parsed = ParseMonth(month);
        if (!parsed.IsSuccess)
        {
            return parsed.Error!;
        }

        var data = await LoadAsync(parsed.Value, cancellationToken);
        var trend = await TrendAsync(parsed.Value, cancellationToken);
        var insight = await reports.GetInsightAsync(UserId, parsed.Value.Year, parsed.Value.Month, cancellationToken);

        return new MonthReportDto(
            Months.Format(data.Start),
            data.BaseCurrency,
            data.Spent,
            data.Approved.Count,
            data.PreviousMonthSpent,
            data.TotalBudget,
            trend,
            data.TopVendors,
            data.Categories,
            insight is null ? null : ToDto(insight, data));
    }

    public async Task<Result<InsightDto>> WriteInsightAsync(string? month, CancellationToken cancellationToken)
    {
        var parsed = ParseMonth(month);
        if (!parsed.IsSuccess)
        {
            return parsed.Error!;
        }

        var data = await LoadAsync(parsed.Value, cancellationToken);
        if (data.Approved.Count == 0)
        {
            return Error.Validation("reports.nothing_to_analyse",
                $"Approve some receipts dated in {MonthName(data.Start)} first: the analysis is written from them.");
        }

        WrittenInsight written;
        try
        {
            written = await insightWriter.WriteAsync(BuildRequest(data), cancellationToken);
        }
        catch (InsightUnavailableException ex)
        {
            logger.LogWarning(ex, "The monthly analysis couldn't be written");
            return Error.Unavailable("reports.ai_unavailable", "AI couldn't write the analysis just now. Try again in a minute.");
        }

        var insight = await reports.GetInsightAsync(UserId, data.Start.Year, data.Start.Month, cancellationToken);
        if (insight is null)
        {
            insight = new MonthlyInsight
            {
                UserId = UserId,
                Year = data.Start.Year,
                Month = data.Start.Month,
                Headline = written.Headline,
                Body = string.Empty,
                Model = written.Model,
            };
            reports.AddInsight(insight);
        }

        insight.Headline = written.Headline;
        insight.Body = string.Join("\n\n", written.Paragraphs);
        insight.Model = written.Model;
        insight.ReceiptCount = data.Approved.Count;
        insight.TotalSpent = data.Spent;
        insight.GeneratedAt = timeProvider.GetUtcNow();

        if (!await reports.SaveChangesAsync(cancellationToken))
        {
            return Error.Conflict("reports.busy", "The analysis was just written in another tab. Refresh to see it.");
        }

        return ToDto(insight, data);
    }

    public async Task<Result<ReportFile>> ExportCsvAsync(string? month, CancellationToken cancellationToken)
    {
        var parsed = ParseMonth(month);
        if (!parsed.IsSuccess)
        {
            return parsed.Error!;
        }

        var start = parsed.Value;
        var approved = await reports.ApprovedAsync(UserId, start, start.AddMonths(1), cancellationToken);
        var names = (await categories.ListAsync(UserId, cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
        var baseCurrency = await users.GetBaseCurrencyAsync(UserId, cancellationToken) ?? "USD";

        var csv = new StringBuilder();
        Csv.Row(csv, "Entry", "Date", "Vendor", "Category", "Currency", "Total", "Exchange rate",
            $"Total ({baseCurrency})", "Amount charged", "Paid with", "Notes");
        foreach (var r in approved)
        {
            Csv.Row(csv,
                r.Number.ToString("0000", CultureInfo.InvariantCulture),
                r.ReceiptDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.VendorName,
                r.CategoryId is { } id && names.TryGetValue(id, out var name) ? name : null,
                r.Currency,
                Csv.Number(r.Total),
                Csv.Number(r.ExchangeRate, "0.##########"),
                Csv.Number(r.BaseCurrencyTotal),
                Csv.Number(r.AmountCharged),
                r.PaymentMethod,
                r.Notes);
        }

        // UTF-8 with a byte-order mark, so Excel shows "Café" and "····4242" correctly instead of guessing the encoding.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return new ReportFile(bytes, "text/csv", $"smartledger-{Months.Format(start)}.csv");
    }

    /// <summary>What a month's report and its analysis are built from.</summary>
    private sealed record MonthData(
        DateOnly Start,
        string BaseCurrency,
        IReadOnlyList<Receipt> Approved,
        decimal Spent,
        decimal PreviousMonthSpent,
        decimal TotalBudget,
        IReadOnlyList<CategorySpendingDto> Categories,
        IReadOnlyList<VendorSpendingDto> TopVendors,
        IReadOnlyDictionary<Guid, string> CategoryNames);

    private Result<DateOnly> ParseMonth(string? month) =>
        Months.Parse(month, DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));

    private async Task<MonthData> LoadAsync(DateOnly start, CancellationToken cancellationToken)
    {
        var userId = UserId;
        var userCategories = await categories.ListAsync(userId, cancellationToken);
        var approved = await reports.ApprovedAsync(userId, start, start.AddMonths(1), cancellationToken);
        var previous = await categories.SpendingAsync(userId, start.AddMonths(-1), start, cancellationToken);
        var baseCurrency = await users.GetBaseCurrencyAsync(userId, cancellationToken) ?? "USD";

        var names = userCategories.ToDictionary(c => c.Id, c => c.Name);
        var spentByCategory = approved
            .Where(r => r.CategoryId is not null)
            .GroupBy(r => r.CategoryId!.Value)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Spent: g.Sum(r => r.BaseCurrencyTotal ?? 0m)));

        // Budget against actual: every active category (a budget with nothing spent is still news), plus archived
        // ones that have spending this month.
        var categoryRows = userCategories
            .Where(c => !c.IsArchived || spentByCategory.ContainsKey(c.Id))
            .Select(c =>
            {
                var (count, spent) = spentByCategory.GetValueOrDefault(c.Id);
                return new CategorySpendingDto(c.Id, c.Name, c.Color, c.MonthlyBudget, c.IsArchived, count, spent);
            })
            .OrderByDescending(c => c.Spent)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new MonthData(
            start,
            baseCurrency,
            approved,
            approved.Sum(r => r.BaseCurrencyTotal ?? 0m),
            previous.Sum(s => s.Spent),
            userCategories.Where(c => !c.IsArchived).Sum(c => c.MonthlyBudget ?? 0m),
            categoryRows,
            TopVendors(approved, names),
            names);
    }

    private async Task<IReadOnlyList<MonthTotalDto>> TrendAsync(DateOnly start, CancellationToken cancellationToken)
    {
        var first = start.AddMonths(-(TrendMonths - 1));
        var totals = await reports.MonthlyTotalsAsync(UserId, first, start.AddMonths(1), cancellationToken);

        // Every month appears, with zero when nothing was approved, so the chart has no gaps.
        return Enumerable.Range(0, TrendMonths)
            .Select(i => first.AddMonths(i))
            .Select(m => new MonthTotalDto(
                Months.Format(m),
                totals.FirstOrDefault(t => t.Year == m.Year && t.Month == m.Month)?.Spent ?? 0m))
            .ToList();
    }

    /// <summary>Vendors by spend. The same shop spelled "STARBUCKS" and "Starbucks" counts once.</summary>
    private static List<VendorSpendingDto> TopVendors(IReadOnlyList<Receipt> approved, IReadOnlyDictionary<Guid, string> categoryNames) =>
        approved
            .Where(r => r.VendorName is not null)
            .GroupBy(r => r.VendorName!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                // The category most of this vendor's money went under.
                var mainCategory = g
                    .Where(r => r.CategoryId is not null)
                    .GroupBy(r => r.CategoryId!.Value)
                    .OrderByDescending(c => c.Sum(r => r.BaseCurrencyTotal ?? 0m))
                    .Select(c => categoryNames.GetValueOrDefault(c.Key))
                    .FirstOrDefault();
                return new VendorSpendingDto(g.First().VendorName!.Trim(), mainCategory, g.Count(), g.Sum(r => r.BaseCurrencyTotal ?? 0m));
            })
            .OrderByDescending(v => v.Spent)
            .ThenBy(v => v.Vendor, StringComparer.CurrentCultureIgnoreCase)
            .Take(TopVendorCount)
            .ToList();

    private static InsightRequest BuildRequest(MonthData data) => new(
        MonthName(data.Start),
        data.BaseCurrency,
        data.Spent,
        data.Approved.Count,
        data.TotalBudget,
        MonthName(data.Start.AddMonths(-1)),
        data.PreviousMonthSpent,
        data.Categories
            .Where(c => c.Spent > 0 || c.MonthlyBudget is not null)
            .Select(c => new InsightCategory(c.Name, c.Spent, c.MonthlyBudget, c.ReceiptCount))
            .ToList(),
        data.TopVendors,
        data.Approved
            .OrderByDescending(r => r.BaseCurrencyTotal ?? 0m)
            .Take(LargestReceiptCount)
            .Select(r => new InsightReceipt(
                r.VendorName ?? "Unknown vendor",
                r.ReceiptDate!.Value,
                r.CategoryId is { } id ? data.CategoryNames.GetValueOrDefault(id) : null,
                r.BaseCurrencyTotal ?? 0m))
            .ToList());

    private static InsightDto ToDto(MonthlyInsight insight, MonthData data) => new(
        insight.Headline,
        insight.Body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        insight.ReceiptCount,
        insight.GeneratedAt,
        IsOutOfDate: insight.ReceiptCount != data.Approved.Count || insight.TotalSpent != data.Spent);

    private static string MonthName(DateOnly month) => month.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
}

/// <summary>
/// RFC 4180 CSV, safe to open in Excel. Text that starts like a formula (=, +, -, @) gets an apostrophe in front, so a
/// vendor named <c>=HYPERLINK(…)</c> on a receipt can't run as a formula on the accountant's computer.
/// </summary>
internal static class Csv
{
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r'];

    public static void Row(StringBuilder csv, params string?[] cells)
    {
        csv.Append(string.Join(',', cells.Select(Cell))).Append("\r\n");
    }

    public static string? Number(decimal? value, string format = "0.00") =>
        value?.ToString(format, CultureInfo.InvariantCulture);

    private static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var text = FormulaStarts.Contains(value[0]) && !IsNumber(value) ? "'" + value : value;
        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }

    // Our own numbers (e.g. "-5.00" for a refund) are written as numbers; only text is guarded.
    private static bool IsNumber(string value) =>
        decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _);
}
