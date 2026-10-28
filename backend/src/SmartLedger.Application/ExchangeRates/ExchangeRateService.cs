using Microsoft.Extensions.Logging;
using SmartLedger.Application.Common;

namespace SmartLedger.Application.ExchangeRates;

internal sealed class ExchangeRateService(
    IExchangeRateRepository repository,
    IExchangeRateProvider provider,
    TimeProvider timeProvider,
    ILogger<ExchangeRateService> logger) : IExchangeRateService
{
    /// <summary>How many earlier days to try when a day has no rates.</summary>
    public const int DaysToLookBack = 7;

    /// <summary>Decimal places kept in a quoted rate: enough for weak currencies (1 IRR ≈ 0.0000238 USD).</summary>
    public const int RateDecimals = 10;

    public async Task<Result<ExchangeRateQuote>> GetRateAsync(
        string from, string to, DateOnly date, CancellationToken cancellationToken)
    {
        from = from.Trim().ToUpperInvariant();
        to = to.Trim().ToUpperInvariant();

        foreach (var code in new[] { from, to })
        {
            if (!Currencies.IsKnown(code))
            {
                return Error.Validation("exchange_rates.unknown_currency", $"\"{code}\" isn't a currency code we know.");
            }
        }

        if (from == to)
        {
            return new ExchangeRateQuote(from, to, 1m, date);
        }

        // Rates for future days don't exist; a receipt dated "tomorrow" (time zones) uses today's.
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var start = date > today ? today : date;
        string[] wanted = [from, to];

        for (var back = 0; back <= DaysToLookBack; back++)
        {
            var day = start.AddDays(-back);

            var stored = await repository.GetAsync(day, wanted, cancellationToken);
            if (stored.Count == wanted.Length)
            {
                return Quote(from, to, stored, day);
            }

            DailyRates? fetched;
            try
            {
                fetched = await provider.GetUsdRatesAsync(day, cancellationToken);
            }
            catch (ExchangeRatesUnavailableException ex)
            {
                logger.LogWarning(ex, "Exchange rates for {Date} couldn't be fetched", day);
                return Error.Unavailable("exchange_rates.unavailable",
                    "Exchange rates can't be fetched right now. Try again in a minute, or enter the amount charged.");
            }

            if (fetched is null)
            {
                continue; // Nothing published for that day; try the day before.
            }

            await repository.SaveAsync(fetched, cancellationToken);

            var missing = wanted.FirstOrDefault(code => !fetched.UnitsPerUsd.ContainsKey(code));
            if (missing is not null)
            {
                return Error.Validation("exchange_rates.no_rate",
                    $"There's no exchange rate for {missing}. Enter the amount charged instead.");
            }

            return Quote(from, to, fetched.UnitsPerUsd, fetched.Date);
        }

        return Error.Validation("exchange_rates.not_available",
            $"No exchange rate is available for {date:dd MMM yyyy}. Enter the amount charged instead.");
    }

    // Through the dollar: 1 PKR = 1/277.02 USD, so PKR → EUR is EUR-per-USD ÷ PKR-per-USD.
    private static ExchangeRateQuote Quote(string from, string to, IReadOnlyDictionary<string, decimal> unitsPerUsd, DateOnly day) =>
        new(from, to, Math.Round(unitsPerUsd[to] / unitsPerUsd[from], RateDecimals, MidpointRounding.AwayFromZero), day);
}
