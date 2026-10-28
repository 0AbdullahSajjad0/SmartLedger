using Microsoft.AspNetCore.Mvc;
using SmartLedger.Api.Common;
using SmartLedger.Application.ExchangeRates;

namespace SmartLedger.Api.Controllers;

[ApiController]
[Route("api/exchange-rates")]
public sealed class ExchangeRatesController(IExchangeRateService exchangeRates) : ControllerBase
{
    /// <summary>
    /// The rate from one currency to another on a day, e.g. <c>?from=PKR&amp;to=USD&amp;date=2026-10-15</c>, so the
    /// review screen can show the converted amount before approving. The day falls back to the nearest earlier one
    /// with rates (see <c>rateDate</c>).
    /// </summary>
    [HttpGet]
    [ProducesResponseType<ExchangeRateQuote>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(
        [FromQuery] string from, [FromQuery] string to, [FromQuery] DateOnly date, CancellationToken cancellationToken) =>
        (await exchangeRates.GetRateAsync(from, to, date, cancellationToken)).ToActionResult(this);
}
