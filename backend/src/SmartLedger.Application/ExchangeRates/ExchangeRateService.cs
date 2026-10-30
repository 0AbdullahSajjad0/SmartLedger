using Microsoft.Extensions.Logging;
using SmartLedger.Application.Common;

namespace SmartLedger.Application.ExchangeRates;

internal sealed class ExchangeRateService(
    IExchangeRateRepository repository,
    IExchangeRateProvider provider,
    TimeProvider timeProvider,
    ILogger<ExchangeRateService> logger) : IExchangeRateService
{
    public const int DaysToLookBack = 7;

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
                continue;
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

    private static ExchangeRateQuote Quote(string from, string to, IReadOnlyDictionary<string, decimal> unitsPerUsd, DateOnly day) =>
        new(from, to, Math.Round(unitsPerUsd[to] / unitsPerUsd[from], RateDecimals, MidpointRounding.AwayFromZero), day);
}
