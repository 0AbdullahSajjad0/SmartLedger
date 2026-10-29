using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using SmartLedger.Api.Auth;
using SmartLedger.Api.Common;
using SmartLedger.Api.Security;
using SmartLedger.Application.Common;
using SmartLedger.Application.Receipts;
using SmartLedger.Domain.Enums;

namespace SmartLedger.Api.Controllers;

[ApiController]
[Route("api/receipts")]
public sealed class ReceiptsController(IReceiptService receiptService) : ControllerBase
{
    // The whole request: up to 10 files of 10 MB each, plus room for the multipart framing.
    // Kestrel stops reading beyond this. Other endpoints keep ASP.NET's 30 MB default.
    private const long MaxRequestBytes = (ReceiptFileTypes.MaxFilesPerUpload * ReceiptFileTypes.MaxSizeBytes) + (1024 * 1024);

    /// <summary>Uploads 1–10 receipt files (form field "files"). Each becomes a receipt waiting for AI extraction.</summary>
    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    // Each multipart section (one file): an oversized file is cut off while it's arriving, not after.
    [RequestFormLimits(MultipartBodyLengthLimit = ReceiptFileTypes.MaxSizeBytes)]
    [EnableRateLimiting(RateLimitPolicies.Uploads)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<IReadOnlyList<ReceiptSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Upload()
    {
        // The form is read here rather than model-bound, so a file over the limit gets a clear 413
        // instead of MVC's generic validation error. No action parameters at all (not even a
        // CancellationToken): any parameter makes MVC read the form first, before this code runs.
        var cancellationToken = HttpContext.RequestAborted;
        if (!Request.HasFormContentType)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Send the files as multipart/form-data.");
        }

        IFormCollection form;
        try
        {
            form = await Request.ReadFormAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge })
        {
            // A body under the per-file limit can't have broken it, so that failure is a malformed form instead.
            return Request.ContentLength is null or > ReceiptFileTypes.MaxSizeBytes
                ? Problem(
                    statusCode: StatusCodes.Status413PayloadTooLarge,
                    title: $"Each file can be at most 10 MB, and up to {ReceiptFileTypes.MaxFilesPerUpload} files per upload.")
                : Problem(statusCode: StatusCodes.Status400BadRequest, title: "The upload couldn't be read. Choose the files again.");
        }

        var uploads = form.Files.GetFiles("files")
            .Select(f => new UploadedFile(f.FileName, f.Length, f.OpenReadStream))
            .ToList();

        return (await receiptService.UploadAsync(uploads, cancellationToken)).ToActionResult(this);
    }

    /// <summary>The ledger, newest first, optionally narrowed by status, vendor search, category and receipt dates.</summary>
    /// <param name="search">Part of the vendor's name (or of the file name while a receipt is unread), any case.</param>
    /// <param name="from">Receipt date on or after, e.g. 2026-10-01.</param>
    /// <param name="to">Receipt date on or before.</param>
    [HttpGet]
    [ProducesResponseType<PagedResult<ReceiptSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<ReceiptSummaryDto>> List(
        [FromQuery] ReceiptStatus? status,
        [FromQuery] string? search,
        [FromQuery] Guid? categoryId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        await receiptService.ListAsync(
            new ReceiptQuery(status, page, pageSize, new ReceiptFilter(search, categoryId, from, to)), cancellationToken);

    /// <summary>
    /// Deletes the receipt permanently: its record, line items, original upload and preview. Approved receipts leave
    /// the reports. Refused (409) while AI is still reading it.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await receiptService.DeleteAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ReceiptDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        (await receiptService.GetAsync(id, cancellationToken)).ToActionResult(this);

    /// <summary>Saves the reviewed details, line items included (lines left out are removed).</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<ReceiptDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, UpdateReceiptRequest request, CancellationToken cancellationToken) =>
        (await receiptService.UpdateAsync(id, request, cancellationToken)).ToActionResult(this);

    /// <summary>
    /// Approves the receipt: its total is converted to your base currency at the receipt date's rate (or set to the
    /// amount charged, if given) and frozen.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType<ReceiptDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Approve(Guid id, ApproveReceiptRequest request, CancellationToken cancellationToken) =>
        (await receiptService.ApproveAsync(id, request, cancellationToken)).ToActionResult(this);

    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType<ReceiptDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(Guid id, CancellationToken cancellationToken) =>
        (await receiptService.RejectAsync(id, cancellationToken)).ToActionResult(this);

    /// <summary>Takes back an approval or rejection; an approved receipt loses its frozen conversion.</summary>
    [HttpPost("{id:guid}/reopen")]
    [ProducesResponseType<ReceiptDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reopen(Guid id, CancellationToken cancellationToken) =>
        (await receiptService.ReopenAsync(id, cancellationToken)).ToActionResult(this);

    /// <summary>The original upload, exactly as received (photos keep their metadata, including any GPS location).</summary>
    [HttpGet("{id:guid}/file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFile(Guid id, CancellationToken cancellationToken) =>
        ServeFile(await receiptService.OpenFileAsync(id, cancellationToken));

    /// <summary>The cleaned JPEG: upright, size-limited, no metadata. 404 until it's ready (see <c>hasPreview</c>).</summary>
    [HttpGet("{id:guid}/preview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPreview(Guid id, CancellationToken cancellationToken) =>
        ServeFile(await receiptService.OpenPreviewAsync(id, cancellationToken));

    private IActionResult ServeFile(Result<ReceiptFile> result)
    {
        if (!result.IsSuccess)
        {
            return result.ToActionResult(this);
        }

        var file = result.Value;

        // Private: the response belongs to one signed-in user, so shared caches must not keep it.
        Response.Headers.CacheControl = "private, max-age=300";
        Response.Headers.ContentSecurityPolicy = SecurityHeaders.UploadedFileContentSecurityPolicy;

        // Inline so images display.
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline")
        {
            FileName = file.DownloadName,
        }.ToString();

        return File(file.Content, file.ContentType);
    }
}
