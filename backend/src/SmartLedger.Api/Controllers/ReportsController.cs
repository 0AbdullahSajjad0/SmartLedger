using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using SmartLedger.Api.Auth;
using SmartLedger.Api.Common;
using SmartLedger.Application.Reports;

namespace SmartLedger.Api.Controllers;

[ApiController]
[Route("api/reports")]
public sealed class ReportsController(IReportService reports) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<MonthReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get([FromQuery] string? month, CancellationToken cancellationToken) =>
        (await reports.GetAsync(month, cancellationToken)).ToActionResult(this);

    [HttpPost("insight")]
    [EnableRateLimiting(RateLimitPolicies.Insights)]
    [ProducesResponseType<InsightDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> WriteInsight([FromQuery] string? month, CancellationToken cancellationToken) =>
        (await reports.WriteInsightAsync(month, cancellationToken)).ToActionResult(this);

    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileContentResult))]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Export([FromQuery] string? month, CancellationToken cancellationToken)
    {
        var result = await reports.ExportCsvAsync(month, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.ToActionResult(this);
        }

        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileName = result.Value.FileName,
        }.ToString();
        return File(result.Value.Content, $"{result.Value.ContentType}; charset=utf-8");
    }
}
