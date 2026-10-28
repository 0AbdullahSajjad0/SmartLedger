using System.Globalization;

namespace SmartLedger.Application.Common;

/// <summary>ISO 4217 currency codes in use today, as .NET knows them from its regional data.</summary>
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

    /// <summary>True for an upper-case code such as "PKR"; false for anything else, including null.</summary>
    public static bool IsKnown(string? code) => code is not null && IsoCodes.Value.Contains(code);
}
