using System.Globalization;

namespace SmartLedger.Application.Common;

/// <summary>Calendar months as the API takes them: "yyyy-MM", e.g. "2026-10".</summary>
public static class Months
{
    private static readonly DateOnly Earliest = new(2000, 1, 1);

    /// <summary>
    /// The first day of the month asked for, or of the current month when none is given. Months before 2000 or after
    /// next month aren't accepted: there's nothing to report there.
    /// </summary>
    public static Result<DateOnly> Parse(string? month, DateOnly today)
    {
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        if (month is null)
        {
            return thisMonth;
        }

        if (!DateOnly.TryParseExact($"{month}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || start < Earliest || start > thisMonth.AddMonths(1))
        {
            return Error.Validation("months.invalid", "Choose a month like 2026-10, from 2000 up to next month.");
        }

        return start;
    }

    public static string Format(DateOnly start) => start.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
