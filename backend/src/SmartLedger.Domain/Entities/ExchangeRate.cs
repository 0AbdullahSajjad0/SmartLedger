using SmartLedger.Domain.Common;

namespace SmartLedger.Domain.Entities;

public class ExchangeRate : Entity
{
    public required string Currency { get; set; }

    public decimal UnitsPerUsd { get; set; }

    public DateOnly EffectiveDate { get; set; }

    public required string Source { get; set; }
}
