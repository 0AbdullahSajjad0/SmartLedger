using SmartLedger.Application.Common;

namespace SmartLedger.Application.ExchangeRates;

/// <summary>How to convert one currency into another on a given day.</summary>
/// <param name="Rate">Multiply an amount in <paramref name="From"/> by this to get <paramref name="To"/>.</param>
/// <param name="RateDate">The day the rate is for: the day asked for, or the nearest earlier day with rates.</param>
public sealed record ExchangeRateQuote(string From, string To, decimal Rate, DateOnly RateDate);

/// <summary>One day of rates from an outside source, as units of each currency per US dollar.</summary>
/// <param name="UnitsPerUsd">Keyed by upper-case ISO 4217 code, e.g. "PKR" → 277.02.</param>
public sealed record DailyRates(DateOnly Date, IReadOnlyDictionary<string, decimal> UnitsPerUsd, string Source);

/// <summary>Where daily rates come from. The free currency-api in production, a fake in tests.</summary>
public interface IExchangeRateProvider
{
    /// <returns>Null when the source has no rates for that day: not published yet, or before its history starts.</returns>
    /// <exception cref="ExchangeRatesUnavailableException">The source couldn't be reached.</exception>
    Task<DailyRates?> GetUsdRatesAsync(DateOnly date, CancellationToken cancellationToken);
}

/// <summary>Rates already fetched, so each day is downloaded once.</summary>
public interface IExchangeRateRepository
{
    /// <summary>The stored rates for that day, for whichever of the currencies have one.</summary>
    Task<IReadOnlyDictionary<string, decimal>> GetAsync(
        DateOnly date, IReadOnlyCollection<string> currencies, CancellationToken cancellationToken);

    /// <summary>Stores a day's rates. Rows that already exist (another request got there first) are left alone.</summary>
    Task SaveAsync(DailyRates rates, CancellationToken cancellationToken);
}

public interface IExchangeRateService
{
    /// <summary>
    /// The rate from one currency to another on a day. Uses stored rates when there are some, and otherwise fetches
    /// and stores them. A day without rates (today's aren't published until tomorrow) falls back to the nearest
    /// earlier day, up to a week back.
    /// </summary>
    Task<Result<ExchangeRateQuote>> GetRateAsync(string from, string to, DateOnly date, CancellationToken cancellationToken);
}

/// <summary>The rate source is down or answered with nonsense. Trying again later may work.</summary>
public sealed class ExchangeRatesUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
