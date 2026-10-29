using Microsoft.AspNetCore.Mvc;
using SmartLedger.Api.Common;
using SmartLedger.Application.Overview;

namespace SmartLedger.Api.Controllers;

[ApiController]
[Route("api/overview")]
public sealed class OverviewController(IOverviewService overview) : ControllerBase
{
    /// <summary>The monthly statement: what was spent, by category, against last month, and what's waiting.</summary>
    /// <param name="month">"yyyy-MM", e.g. 2026-10. Leave out for the current month.</param>
    [HttpGet]
    [ProducesResponseType<OverviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get([FromQuery] string? month, CancellationToken cancellationToken) =>
        (await overview.GetAsync(month, cancellationToken)).ToActionResult(this);
}
