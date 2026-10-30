using SmartLedger.Domain.Entities;

namespace SmartLedger.UnitTests.Domain;

public class CategoryBudgetTests
{
    private static readonly CategoryBudget[] History =
    [
        new() { EffectiveFrom = new DateOnly(2026, 3, 1), Amount = 100m },
        new() { EffectiveFrom = new DateOnly(2026, 7, 1), Amount = 250m },
        new() { EffectiveFrom = new DateOnly(2026, 9, 1), Amount = null },
    ];

    [Theory]
    [InlineData("2026-02-15", null)]
    [InlineData("2026-03-01", "100")]
    [InlineData("2026-06-30", "100")]
    [InlineData("2026-07-10", "250")]
    [InlineData("2026-08-31", "250")]
    [InlineData("2026-09-01", null)]
    [InlineData("2027-01-01", null)]
    public void For_IsTheLatestEntryOnOrBeforeTheMonth(string date, string? expected)
    {
        var budget = CategoryBudget.For(History, DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(expected is null ? null : decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), budget);
    }
}
