using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using SmartLedger.Api.Auth;
using SmartLedger.Api.Common;
using SmartLedger.Api.Security;
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

    [HttpGet]
    [ProducesResponseType<PagedResult<ReceiptSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<ReceiptSummaryDto>> List(
        [FromQuery] ReceiptStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        await receiptService.ListAsync(new ReceiptQuery(status, page, pageSize), cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ReceiptDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        (await receiptService.GetAsync(id, cancellationToken)).ToActionResult(this);

    /// <summary>The original uploaded image or PDF.</summary>
    [HttpGet("{id:guid}/file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFile(Guid id, CancellationToken cancellationToken)
    {
        var result = await receiptService.OpenFileAsync(id, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.ToActionResult(this);
        }

        var file = result.Value;

        // Private: the response belongs to one signed-in user, so shared caches must not keep it.
        Response.Headers.CacheControl = "private, max-age=300";
        Response.Headers.ContentSecurityPolicy = SecurityHeaders.UploadedFileContentSecurityPolicy;

        // Inline so images display; named by entry number, never by the uploader's original file name.
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline")
        {
            FileName = $"receipt-{file.ReceiptNumber:0000}{file.Extension}",
        }.ToString();

        return File(file.Content, file.ContentType);
    }
}
