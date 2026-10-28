using System.Globalization;
using System.Text.RegularExpressions;
using SmartLedger.Application.Common;
using SmartLedger.Domain.Entities;

namespace SmartLedger.Application.Receipts.Extraction;

/// <summary>
/// Turns what the AI returned into a receipt we're willing to show. Nothing is trusted as-is: text is trimmed
/// and capped, dates and currencies are validated, card numbers are cut to the last four digits, and totals are
/// checked against the line items. Anything doubtful becomes a "please confirm" flag for the review screen.
/// </summary>
public static partial class ExtractionSanitizer
{
    public const int MaxLineItems = 100;

    /// <summary>Field names the review screen knows how to point at.</summary>
    public static readonly IReadOnlySet<string> KnownFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "VendorName", "ReceiptDate", "Currency", "Subtotal", "Tax", "Total", "PaymentMethod", "Category", "LineItems",
    };

    /// <summary>Earlier dates are almost certainly misread. Shared with manual editing.</summary>
    public static readonly DateOnly EarliestPlausibleDate = new(2000, 1, 1);

    public static void Apply(Receipt receipt, ExtractedReceipt result, IReadOnlyList<Category> categories, DateOnly today)
    {
        receipt.RawExtraction = result.RawResponse;

        if (!result.IsReceipt)
        {
            receipt.MarkFailed(Truncate(result.UnreadableReason, 300) ?? "This doesn't look like a receipt or invoice.");
            return;
        }

        var flags = new List<ExtractionFlag>();
        void Flag(string field, string reason)
        {
            if (!flags.Any(f => f.Field == field))
            {
                flags.Add(new ExtractionFlag(field, reason));
            }
        }

        // The AI's own doubts first, limited to fields we can show.
        foreach (var flag in result.FieldsToCheck)
        {
            if (flag.Field is { } field && KnownFields.Contains(field) && !string.IsNullOrWhiteSpace(flag.Reason))
            {
                Flag(field, Truncate(flag.Reason, 200)!);
            }
        }

        receipt.VendorName = Truncate(result.VendorName, 200);
        if (receipt.VendorName is null)
        {
            Flag("VendorName", "The vendor's name couldn't be read.");
        }

        receipt.ReceiptDate = ParseDate(result.ReceiptDate, today, out var dateProblem);
        if (dateProblem is not null)
        {
            Flag("ReceiptDate", dateProblem);
        }

        var currency = result.Currency?.Trim().ToUpperInvariant();
        if (Currencies.IsKnown(currency))
        {
            receipt.Currency = currency;
        }
        else
        {
            receipt.Currency = null;
            Flag("Currency", "The currency couldn't be identified. Choose it from the list.");
        }

        receipt.Subtotal = Money(result.Subtotal);
        receipt.Tax = Money(result.Tax);
        receipt.Total = Money(result.Total);
        if (receipt.Total is null)
        {
            Flag("Total", "The total couldn't be read.");
        }

        receipt.PaymentMethod = MaskCardNumbers(Truncate(result.PaymentMethod, 60));

        var category = categories.FirstOrDefault(c =>
            !c.IsArchived && string.Equals(c.Name, result.Category?.Trim(), StringComparison.OrdinalIgnoreCase));
        receipt.CategoryId = category?.Id;
        receipt.CategorySuggestedByAi = category is not null;

        receipt.LineItems = result.LineItems
            .Where(line => line.Amount is not null)
            .Take(MaxLineItems)
            .Select((line, index) =>
            {
                var quantity = line.Quantity is > 0 ? Math.Round(line.Quantity.Value, 3) : 1m;
                var amount = Money(line.Amount)!.Value;
                return new LineItem
                {
                    ReceiptId = receipt.Id,
                    LineNumber = index + 1,
                    Description = Truncate(line.Description, 200) ?? "Item",
                    Quantity = quantity,
                    UnitPrice = Money(line.UnitPrice) ?? Math.Round(amount / quantity, 2, MidpointRounding.AwayFromZero),
                    Amount = amount,
                };
            })
            .ToList();

        CheckArithmetic(receipt, Flag);

        receipt.ExtractionFlags = flags;
        receipt.MarkExtracted();
    }

    /// <summary>Card and account numbers keep only their last four digits, whatever the AI returned.</summary>
    public static string? MaskCardNumbers(string? text) =>
        text is null ? null : LongDigitRun().Replace(text, match =>
        {
            var digits = new string(match.Value.Where(char.IsDigit).ToArray());
            return digits.Length > 4 ? $"····{digits[^4..]}" : match.Value;
        });

    private static void CheckArithmetic(Receipt receipt, Action<string, string> flag)
    {
        const decimal tolerance = 0.01m;

        if (receipt.LineItems.Count > 0 && receipt.Subtotal is { } subtotal)
        {
            var lines = receipt.LineItems.Sum(l => l.Amount);
            if (Math.Abs(lines - subtotal) > tolerance)
            {
                flag("Subtotal", $"The line items add up to {lines:0.00}, not {subtotal:0.00}. A discount or a misread line?");
            }
        }

        if (receipt.Subtotal is { } sub && receipt.Tax is { } tax && receipt.Total is { } total
            && Math.Abs(sub + tax - total) > tolerance)
        {
            flag("Total", $"Subtotal plus tax is {sub + tax:0.00}, but the total reads {total:0.00}. A tip or service charge?");
        }
    }

    private static DateOnly? ParseDate(string? text, DateOnly today, out string? problem)
    {
        problem = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            problem = "The date couldn't be read.";
            return null;
        }

        if (!DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            problem = "The date couldn't be read.";
            return null;
        }

        // A day of slack for time zones; anything further ahead, or very old, is almost certainly misread.
        if (date > today.AddDays(1) || date < EarliestPlausibleDate)
        {
            problem = $"The date reads as {date:dd MMM yyyy}, which looks wrong.";
            return null;
        }

        return date;
    }

    private static decimal? Money(decimal? value) =>
        value is null ? null : Math.Round(value.Value, 2, MidpointRounding.AwayFromZero);

    private static string? Truncate(string? text, int maxLength)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    // Five or more digits, possibly broken up by spaces or dashes (e.g. "4242 4242 4242 4242").
    [GeneratedRegex(@"\d(?:[ \-]?\d){4,}")]
    private static partial Regex LongDigitRun();
}
