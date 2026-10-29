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
    /// <summary>
    /// A month in review: totals, six-month trend, top vendors, budget against actual, and AI's analysis if written.
    /// Approved receipts only, by receipt date, in the base currency.
    /// </summary>
    /// <param name="month">"yyyy-MM", e.g. 2026-10. Leave out for the current month.</param>
    [HttpGet]
    [ProducesResponseType<MonthReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get([FromQuery] string? month, CancellationToken cancellationToken) =>
        (await reports.GetAsync(month, cancellationToken)).ToActionResult(this);

    /// <summary>
    /// Asks AI to write (or rewrite) the month's analysis from its totals. Limited per user, since each is a call to
    /// the AI service.
    /// </summary>
    [HttpPost("insight")]
    [EnableRateLimiting(RateLimitPolicies.Insights)]
    [ProducesResponseType<InsightDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> WriteInsight([FromQuery] string? month, CancellationToken cancellationToken) =>
        (await reports.WriteInsightAsync(month, cancellationToken)).ToActionResult(this);

    /// <summary>The month's approved receipts as CSV, ready for a spreadsheet or an accountant.</summary>
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

        // Private: one user's receipts, never kept by a shared cache.
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileName = result.Value.FileName,
        }.ToString();
        return File(result.Value.Content, $"{result.Value.ContentType}; charset=utf-8");
    }
}
