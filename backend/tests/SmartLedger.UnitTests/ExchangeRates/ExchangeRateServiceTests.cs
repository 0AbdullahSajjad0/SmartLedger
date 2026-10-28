using Microsoft.Extensions.Logging.Abstractions;
using SmartLedger.Application.Common;
using SmartLedger.Application.ExchangeRates;

namespace SmartLedger.UnitTests.ExchangeRates;

public class ExchangeRateServiceTests
{
    private static readonly DateOnly Today = new(2026, 10, 28);
    private static readonly DateOnly ReceiptDay = new(2026, 10, 15);

    private readonly FakeSource source = new();
    private readonly InMemoryRates stored = new();

    [Fact]
    public async Task ConvertsThroughTheDollar()
    {
        source.Publish(ReceiptDay, ("PKR", 280m), ("EUR", 0.9m));

        var quote = await Service().GetRateAsync("PKR", "EUR", ReceiptDay, CancellationToken.None);

        // 1 PKR = 1/280 USD = 0.9/280 EUR.
        Assert.True(quote.IsSuccess);
        Assert.Equal(0.0032142857m, quote.Value.Rate);
        Assert.Equal(ReceiptDay, quote.Value.RateDate);
    }

    [Fact]
    public async Task FetchesEachDayOnce_ThenUsesTheStoredRates()
    {
        source.Publish(ReceiptDay, ("PKR", 280m), ("EUR", 0.9m), ("GBP", 0.75m));
        var service = Service();

        await service.GetRateAsync("PKR", "EUR", ReceiptDay, CancellationToken.None);
        var second = await service.GetRateAsync("GBP", "PKR", ReceiptDay, CancellationToken.None);

        Assert.Equal(1, source.Requests);
        Assert.Equal(373.3333333333m, second.Value.Rate);
    }

    [Fact]
    public async Task ADayWithoutRates_UsesTheNearestEarlierDay()
    {
        // Today's rates aren't published until tomorrow; yesterday's are.
        source.Publish(Today.AddDays(-1), ("PKR", 277m), ("EUR", 0.92m));

        var quote = await Service().GetRateAsync("PKR", "EUR", Today, CancellationToken.None);

        Assert.True(quote.IsSuccess);
        Assert.Equal(Today.AddDays(-1), quote.Value.RateDate);
    }

    [Fact]
    public async Task AFutureDate_UsesTodaysRates()
    {
        source.Publish(Today, ("PKR", 277m), ("EUR", 0.92m));

        var quote = await Service().GetRateAsync("PKR", "EUR", Today.AddDays(1), CancellationToken.None);

        Assert.Equal(Today, quote.Value.RateDate);
    }

    [Fact]
    public async Task TheSameCurrency_IsOneToOne_WithoutAskingTheSource()
    {
        var quote = await Service().GetRateAsync("eur", "EUR", ReceiptDay, CancellationToken.None);

        Assert.Equal(1m, quote.Value.Rate);
        Assert.Equal(0, source.Requests);
    }

    [Fact]
    public async Task NoRatesWithinAWeek_AsksForTheAmountCharged()
    {
        var quote = await Service().GetRateAsync("PKR", "EUR", new DateOnly(2019, 1, 1), CancellationToken.None);

        Assert.False(quote.IsSuccess);
        Assert.Contains("Enter the amount charged", quote.Error!.Message);
        Assert.Equal(ExchangeRateService.DaysToLookBack + 1, source.Requests);
    }

    [Fact]
    public async Task ACurrencyTheSourceDoesntCover_AsksForTheAmountCharged()
    {
        source.Publish(ReceiptDay, ("EUR", 0.9m));

        var quote = await Service().GetRateAsync("PKR", "EUR", ReceiptDay, CancellationToken.None);

        Assert.Equal("exchange_rates.no_rate", quote.Error!.Code);
        Assert.Contains("PKR", quote.Error.Message);
    }

    [Fact]
    public async Task AnUnknownCurrencyCode_IsRejected()
    {
        var quote = await Service().GetRateAsync("XYZ", "EUR", ReceiptDay, CancellationToken.None);

        Assert.Equal(ErrorType.Validation, quote.Error!.Type);
        Assert.Equal(0, source.Requests);
    }

    [Fact]
    public async Task ASourceOutage_IsReportedAsUnavailable()
    {
        source.Down = true;

        var quote = await Service().GetRateAsync("PKR", "EUR", ReceiptDay, CancellationToken.None);

        Assert.Equal(ErrorType.Unavailable, quote.Error!.Type);
    }

    private ExchangeRateService Service() =>
        new(stored, source, new FixedTime(Today), NullLogger<ExchangeRateService>.Instance);

    private sealed class FakeSource : IExchangeRateProvider
    {
        private readonly Dictionary<DateOnly, Dictionary<string, decimal>> days = [];

        public int Requests { get; private set; }

        public bool Down { get; set; }

        public void Publish(DateOnly day, params (string Code, decimal UnitsPerUsd)[] rates) =>
            days[day] = rates.Append(("USD", 1m)).ToDictionary(r => r.Item1, r => r.Item2);

        public Task<DailyRates?> GetUsdRatesAsync(DateOnly date, CancellationToken cancellationToken)
        {
            Requests++;
            if (Down)
            {
                throw new ExchangeRatesUnavailableException("Down for the test.");
            }
            return Task.FromResult(days.TryGetValue(date, out var rates) ? new DailyRates(date, rates, "fake") : null);
        }
    }

    private sealed class InMemoryRates : IExchangeRateRepository
    {
        private readonly Dictionary<DateOnly, IReadOnlyDictionary<string, decimal>> days = [];

        public Task<IReadOnlyDictionary<string, decimal>> GetAsync(
            DateOnly date, IReadOnlyCollection<string> currencies, CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<string, decimal> found = days.TryGetValue(date, out var rates)
                ? rates.Where(r => currencies.Contains(r.Key)).ToDictionary()
                : new Dictionary<string, decimal>();
            return Task.FromResult(found);
        }

        public Task SaveAsync(DailyRates rates, CancellationToken cancellationToken)
        {
            days[rates.Date] = rates.UnitsPerUsd;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTime(DateOnly today) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
    }
}
