using System.Globalization;

namespace SmartLedger.Application.Common;

public static class Currencies
{
    private static readonly Lazy<HashSet<string>> IsoCodes = new(() =>
        CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Select(culture =>
            {
                try { return new RegionInfo(culture.Name).ISOCurrencySymbol; }
                catch (ArgumentException) { return null; }
            })
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal));

    public static bool IsKnown(string? code) => code is not null && IsoCodes.Value.Contains(code);
}
