using SmartLedger.Application.Common;

namespace SmartLedger.Application.ExchangeRates;

public sealed record ExchangeRateQuote(string From, string To, decimal Rate, DateOnly RateDate);

public sealed record DailyRates(DateOnly Date, IReadOnlyDictionary<string, decimal> UnitsPerUsd, string Source);

public interface IExchangeRateProvider
{
    Task<DailyRates?> GetUsdRatesAsync(DateOnly date, CancellationToken cancellationToken);
}

public interface IExchangeRateRepository
{
    Task<IReadOnlyDictionary<string, decimal>> GetAsync(
        DateOnly date, IReadOnlyCollection<string> currencies, CancellationToken cancellationToken);

    Task SaveAsync(DailyRates rates, CancellationToken cancellationToken);
}

public interface IExchangeRateService
{
    Task<Result<ExchangeRateQuote>> GetRateAsync(string from, string to, DateOnly date, CancellationToken cancellationToken);
}

public sealed class ExchangeRatesUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
