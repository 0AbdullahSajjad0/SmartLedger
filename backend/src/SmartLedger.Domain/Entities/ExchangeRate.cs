using SmartLedger.Domain.Common;

namespace SmartLedger.Domain.Entities;

/// <summary>
/// Value of one currency in US dollars on a given date. Every conversion goes through USD:
/// amount × RateToUsd(from) ÷ RateToUsd(to).
/// </summary>
public class ExchangeRate : Entity
{
    /// <summary>ISO 4217 code, e.g. "EUR".</summary>
    public required string Currency { get; set; }

    /// <summary>How many US dollars one unit of <see cref="Currency"/> is worth.</summary>
    public decimal RateToUsd { get; set; }

    /// <summary>The rate applies from this date until a newer row exists.</summary>
    public DateOnly EffectiveDate { get; set; }

    /// <summary>Where the rate came from, e.g. "Manual" or a provider name.</summary>
    public required string Source { get; set; }
}
