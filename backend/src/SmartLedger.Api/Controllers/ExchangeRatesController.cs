using Microsoft.AspNetCore.Mvc;
using SmartLedger.Api.Common;
using SmartLedger.Application.ExchangeRates;

namespace SmartLedger.Api.Controllers;

[ApiController]
[Route("api/exchange-rates")]
public sealed class ExchangeRatesController(IExchangeRateService exchangeRates) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ExchangeRateQuote>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(
        [FromQuery] string from, [FromQuery] string to, [FromQuery] DateOnly date, CancellationToken cancellationToken) =>
        (await exchangeRates.GetRateAsync(from, to, date, cancellationToken)).ToActionResult(this);
}
