using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SmartLedger.Application.Reports;
using SmartLedger.Infrastructure.Gemini;

namespace SmartLedger.Infrastructure.Reports;

/// <summary>
/// Writes the monthly analysis with Gemini. It sees only the month's totals and names (see <see cref="InsightRequest"/>),
/// must answer in a fixed shape, and what it writes is cleaned up and length-limited before it's kept.
/// </summary>
internal sealed partial class GeminiInsightWriter(GeminiApi gemini) : IInsightWriter
{
    public const int MaxParagraphs = 4;
    public const int MaxHeadlineLength = 300;
    public const int MaxParagraphLength = 700;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal const string System = """
        You write the monthly spending review in SmartLedger, an expense app for freelancers and small businesses.
        You are given one month's approved receipts as figures. Write like a careful bookkeeper: plain, specific, calm.

        Rules:
        - Use only the figures you are given. Never invent amounts, vendors, dates, categories or reasons.
          You may add, subtract and compare the given figures; round percentages to whole numbers.
        - Write money with the currency code and two decimals, e.g. "USD 1,640.00".
        - headline: one sentence, at most 25 words, with the single most useful takeaway. If any category broke its
          budget, the headline says so; never call a month "within budget" when one of its categories went over.
        - paragraphs: two to four short paragraphs of one to three sentences. Cover, in this order of importance:
          budgets that were broken or kept (with the difference), where most of the money went (categories, vendors,
          any unusually large receipt), and how the month compares with the previous one.
        - A budget that was broken is always worth saying, even in a quiet month.
        - previousMonth is null when nothing was approved that month: then make no comparison with it at all.
        - If there are only one or two receipts, keep to one or two paragraphs and say the month is too thin to read
          much into, after any broken budget.
        - No advice about taxes, investments, loans or legal matters. No judgement of the person's choices.
        - Plain text only: no markdown, bullet points, headings or emoji.
        """;

    internal static readonly object ResponseSchema = new
    {
        Type = "OBJECT",
        Properties = new
        {
            Headline = new { Type = "STRING", Description = "One sentence, at most 25 words." },
            Paragraphs = new
            {
                Type = "ARRAY",
                Items = new { Type = "STRING" },
                MinItems = 1,
                MaxItems = MaxParagraphs,
            },
        },
        Required = new[] { "headline", "paragraphs" },
        PropertyOrdering = new[] { "headline", "paragraphs" },
    };

    public async Task<WrittenInsight> WriteAsync(InsightRequest request, CancellationToken cancellationToken)
    {
        string text;
        try
        {
            // A little variety in wording on "Regenerate", but still anchored to the figures.
            text = await gemini.GenerateJsonAsync(System, [GeminiApi.Text(Figures(request))], ResponseSchema, temperature: 0.4, cancellationToken);
        }
        catch (GeminiUnavailableException ex)
        {
            throw new InsightUnavailableException(ex.Message, ex);
        }

        Answer? answer;
        try
        {
            answer = JsonSerializer.Deserialize<Answer>(text, Json);
        }
        catch (JsonException ex)
        {
            throw new InsightUnavailableException("The AI service's answer wasn't valid JSON.", ex);
        }

        var headline = Clean(answer?.Headline, MaxHeadlineLength);
        var paragraphs = (answer?.Paragraphs ?? [])
            .Select(p => Clean(p, MaxParagraphLength))
            .Where(p => p.Length > 0)
            .Take(MaxParagraphs)
            .ToList();

        if (headline.Length == 0 || paragraphs.Count == 0)
        {
            throw new InsightUnavailableException("The AI service's analysis was empty.");
        }

        return new WrittenInsight(headline, paragraphs, gemini.Model);
    }

    /// <summary>The month as the AI sees it: a short preamble and the figures as JSON.</summary>
    internal static string Figures(InsightRequest r)
    {
        var figures = new
        {
            month = r.MonthName,
            currency = r.BaseCurrency,
            spent = r.Spent,
            approvedReceipts = r.ReceiptCount,
            totalMonthlyBudget = r.TotalBudget > 0 ? r.TotalBudget : (decimal?)null,
            // Left out when empty, so the AI isn't tempted into "up from 0.00".
            previousMonth = r.PreviousMonthSpent > 0 ? new { name = r.PreviousMonthName, spent = r.PreviousMonthSpent } : null,
            categories = r.Categories.Select(c => new { name = c.Name, spent = c.Spent, budget = c.Budget, receipts = c.ReceiptCount }),
            topVendors = r.TopVendors.Select(v => new { vendor = v.Vendor, category = v.CategoryName, receipts = v.ReceiptCount, spent = v.Spent }),
            largestReceipts = r.LargestReceipts.Select(x => new
            {
                vendor = x.Vendor,
                date = x.Date.ToString("d MMMM", CultureInfo.InvariantCulture),
                category = x.Category,
                amount = x.Amount,
            }),
        };

        return $"Review {r.MonthName}. All amounts are in {r.BaseCurrency}. A budget of null means none is set.\n\n"
            + JsonSerializer.Serialize(figures, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Plain text: markdown and list markers removed, whitespace tidied, length capped at a word boundary.</summary>
    internal static string Clean(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var plain = Markdown().Replace(text, string.Empty);
        plain = ListMarker().Replace(plain, string.Empty);
        plain = Whitespace().Replace(plain, " ").Trim();

        if (plain.Length <= maxLength)
        {
            return plain;
        }

        var cut = plain[..maxLength];
        var lastSpace = cut.LastIndexOf(' ');
        return (lastSpace > maxLength / 2 ? cut[..lastSpace] : cut).TrimEnd(',', ';', ':') + "…";
    }

    private sealed record Answer(string? Headline, List<string>? Paragraphs);

    [GeneratedRegex(@"\*\*|__|`|^#+\s*", RegexOptions.Multiline)]
    private static partial Regex Markdown();

    [GeneratedRegex(@"^\s*(?:[-*•]|\d+[.)])\s+", RegexOptions.Multiline)]
    private static partial Regex ListMarker();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
