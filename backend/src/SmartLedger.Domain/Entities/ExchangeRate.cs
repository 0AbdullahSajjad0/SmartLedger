using SmartLedger.Domain.Common;

namespace SmartLedger.Domain.Entities;

/// <summary>
/// How much of one currency a US dollar bought on a given date. Every conversion goes through USD:
/// amount ÷ UnitsPerUsd(from) × UnitsPerUsd(to).
/// </summary>
/// <remarks>
/// Stored as units per dollar (PKR 277.019075) rather than dollars per unit (0.00360986): weak currencies such as
/// the Iranian rial would otherwise keep only two or three significant digits.
/// </remarks>
public class ExchangeRate : Entity
{
    /// <summary>ISO 4217 code, e.g. "EUR".</summary>
    public required string Currency { get; set; }

    /// <summary>How many units of <see cref="Currency"/> one US dollar bought.</summary>
    public decimal UnitsPerUsd { get; set; }

    /// <summary>The day the rate is for.</summary>
    public DateOnly EffectiveDate { get; set; }

    /// <summary>Where the rate came from, e.g. "currency-api".</summary>
    public required string Source { get; set; }
}
