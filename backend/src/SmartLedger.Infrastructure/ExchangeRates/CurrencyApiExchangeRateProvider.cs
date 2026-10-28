using System.Globalization;
using System.Net;
using System.Text.Json;
using SmartLedger.Application.Common;
using SmartLedger.Application.ExchangeRates;

namespace SmartLedger.Infrastructure.ExchangeRates;

/// <summary>
/// Daily rates from the free, open "currency-api" (github.com/fawazahmed0/exchange-api): no key, 150+ currencies
/// including PKR, history by date. Published as static files on jsDelivr's CDN, with a Cloudflare mirror as backup.
/// </summary>
internal sealed class CurrencyApiExchangeRateProvider(HttpClient http) : IExchangeRateProvider
{
    public const string SourceName = "currency-api";

    /// <summary>Tried in order. Both serve identical files, so a "not found" from the first is the answer.</summary>
    internal static readonly string[] SourceUrls =
    [
        "https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@{date}/v1/currencies/usd.min.json",
        "https://{date}.currency-api.pages.dev/v1/currencies/usd.min.json",
    ];

    public async Task<DailyRates?> GetUsdRatesAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Exception? lastFailure = null;

        foreach (var template in SourceUrls)
        {
            try
            {
                using var response = await http.GetAsync(template.Replace("{date}", day), cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null; // Not published for that day (yet), or before the history starts.
                }

                response.EnsureSuccessStatusCode();
                await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var json = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
                return Parse(json.RootElement, date);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException
                || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                lastFailure = ex; // Try the mirror.
            }
        }

        throw new ExchangeRatesUnavailableException($"No rate source answered for {day}.", lastFailure);
    }

    /// <summary>Reads <c>{ "date": "2026-10-15", "usd": { "pkr": 277.02, "eur": 0.92, "btc": 0.0000088, … } }</c>.</summary>
    internal static DailyRates Parse(JsonElement root, DateOnly requested)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("usd", out var usd) || usd.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The rate file has no \"usd\" rates.");
        }

        var date = root.TryGetProperty("date", out var dateText)
            && DateOnly.TryParseExact(dateText.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : requested;

        // Only real currencies: the file also lists cryptocurrencies and metals.
        var rates = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var rate in usd.EnumerateObject())
        {
            var code = rate.Name.ToUpperInvariant();
            if (Currencies.IsKnown(code) && rate.Value.ValueKind == JsonValueKind.Number
                && rate.Value.TryGetDecimal(out var units) && units > 0)
            {
                rates[code] = Math.Round(units, 6, MidpointRounding.AwayFromZero);
            }
        }

        if (rates.Count < 10)
        {
            throw new InvalidDataException($"The rate file has only {rates.Count} usable currencies.");
        }

        rates["USD"] = 1m;
        return new DailyRates(date, rates, SourceName);
    }
}
