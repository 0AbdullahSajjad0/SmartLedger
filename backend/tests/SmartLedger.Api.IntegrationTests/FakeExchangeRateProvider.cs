using SmartLedger.Application.ExchangeRates;

namespace SmartLedger.Api.IntegrationTests;

/// <summary>
/// Stands in for currency-api in integration tests: the same rates every day from 2024 on (when the real history
/// starts), and nothing before. Test accounts use EUR, so 2,800 PKR converts to exactly 9.00 EUR.
/// </summary>
internal sealed class FakeExchangeRateProvider : IExchangeRateProvider
{
    public static readonly DateOnly HistoryStarts = new(2024, 1, 1);

    private static readonly Dictionary<string, decimal> Rates = new()
    {
        ["USD"] = 1m,
        ["EUR"] = 0.9m,
        ["PKR"] = 280m,
        ["GBP"] = 0.75m,
    };

    public Task<DailyRates?> GetUsdRatesAsync(DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult(date < HistoryStarts ? null : new DailyRates(date, Rates, "fake"));
}
