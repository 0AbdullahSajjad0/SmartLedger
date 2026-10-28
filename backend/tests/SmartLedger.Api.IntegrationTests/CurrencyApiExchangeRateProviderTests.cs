using System.Net;
using System.Text;
using SmartLedger.Application.ExchangeRates;
using SmartLedger.Infrastructure.ExchangeRates;

namespace SmartLedger.Api.IntegrationTests;

/// <summary>The currency-api client against a fake CDN: which files it asks for, and how it reads them.</summary>
public class CurrencyApiExchangeRateProviderTests
{
    private static readonly DateOnly Day = new(2026, 10, 15);

    // The real file's shape, cut down: currencies, plus a cryptocurrency and gold, which must be left out.
    private const string RatesJson = """
        {"date":"2026-10-15","usd":{"aed":3.6725,"aud":1.52,"btc":0.0000088,"cad":1.37,"cny":7.1,"eur":0.92,
         "gbp":0.75,"inr":84.1,"jpy":149.5,"pkr":277.01907539,"sar":3.75,"usd":1,"xau":0.00037}}
        """;

    [Fact]
    public async Task ReadsTheDaysRates_KeepingOnlyRealCurrencies()
    {
        var cdn = new FakeCdn(("jsdelivr", HttpStatusCode.OK, RatesJson));

        var rates = await Provider(cdn).GetUsdRatesAsync(Day, CancellationToken.None);

        Assert.Equal(Day, rates!.Date);
        Assert.Equal(277.019075m, rates.UnitsPerUsd["PKR"]); // six decimal places, as stored
        Assert.Equal(1m, rates.UnitsPerUsd["USD"]);
        Assert.False(rates.UnitsPerUsd.ContainsKey("BTC"));
        Assert.False(rates.UnitsPerUsd.ContainsKey("XAU"));
        Assert.Contains("@2026-10-15/", Assert.Single(cdn.Requested));
    }

    [Fact]
    public async Task ADayThatWasntPublished_IsNull_WithoutAskingTheMirror()
    {
        var cdn = new FakeCdn(("jsdelivr", HttpStatusCode.NotFound, "Not found"));

        var rates = await Provider(cdn).GetUsdRatesAsync(Day, CancellationToken.None);

        Assert.Null(rates);
        Assert.Single(cdn.Requested);
    }

    [Fact]
    public async Task WhenTheCdnIsDown_TheMirrorIsUsed()
    {
        var cdn = new FakeCdn(
            ("jsdelivr", HttpStatusCode.ServiceUnavailable, "Down"),
            ("pages.dev", HttpStatusCode.OK, RatesJson));

        var rates = await Provider(cdn).GetUsdRatesAsync(Day, CancellationToken.None);

        Assert.Equal(277.019075m, rates!.UnitsPerUsd["PKR"]);
        Assert.Contains("2026-10-15.currency-api.pages.dev", cdn.Requested[1]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"date":"2026-10-15","eur":{"usd":1.08}}""")]
    [InlineData("""{"date":"2026-10-15","usd":{"eur":0.92}}""")]
    public async Task ANonsenseAnswerFromBoth_IsReportedAsUnavailable(string body)
    {
        var cdn = new FakeCdn(("jsdelivr", HttpStatusCode.OK, body), ("pages.dev", HttpStatusCode.OK, body));

        await Assert.ThrowsAsync<ExchangeRatesUnavailableException>(
            () => Provider(cdn).GetUsdRatesAsync(Day, CancellationToken.None));
    }

    private static CurrencyApiExchangeRateProvider Provider(FakeCdn cdn) => new(new HttpClient(cdn));

    /// <summary>Answers by the first rule whose text appears in the URL; anything else can't be reached.</summary>
    private sealed class FakeCdn(params (string UrlContains, HttpStatusCode Status, string Body)[] rules) : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);

            var rule = rules.FirstOrDefault(r => url.Contains(r.UrlContains, StringComparison.Ordinal));
            if (rule.UrlContains is null)
            {
                throw new HttpRequestException($"No route to {url}.");
            }

            return Task.FromResult(new HttpResponseMessage(rule.Status)
            {
                Content = new StringContent(rule.Body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
