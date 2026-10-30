using Microsoft.AspNetCore.Mvc;
using SmartLedger.Api.Common;
using SmartLedger.Application.Overview;

namespace SmartLedger.Api.Controllers;

[ApiController]
[Route("api/overview")]
public sealed class OverviewController(IOverviewService overview) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<OverviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get([FromQuery] string? month, CancellationToken cancellationToken) =>
        (await overview.GetAsync(month, cancellationToken)).ToActionResult(this);
}
