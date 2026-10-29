using System.ComponentModel.DataAnnotations;
using SmartLedger.Application.Receipts.Extraction;
using SmartLedger.Domain.Enums;

namespace SmartLedger.Application.Receipts;

/// <summary>An uploaded file, independent of ASP.NET's IFormFile so this layer doesn't depend on the web framework.</summary>
/// <param name="OpenReadStream">Opens a new stream over the file each time it's called.</param>
public sealed record UploadedFile(string FileName, long Length, Func<Stream> OpenReadStream);

/// <summary>One row in the receipts ledger.</summary>
public sealed record ReceiptSummaryDto(
    Guid Id,
    int Number,
    ReceiptStatus Status,
    string? VendorName,
    DateOnly? ReceiptDate,
    string? Currency,
    decimal? Total,
    decimal? BaseCurrencyTotal,
    Guid? CategoryId,
    string FileName,
    DateTimeOffset CreatedAt);

public sealed record LineItemDto(int LineNumber, string Description, decimal Quantity, decimal UnitPrice, decimal Amount);

public sealed record ExtractionFlagDto(string Field, string Reason);

/// <summary>Everything the review screen shows.</summary>
/// <param name="HasPreview">A cleaned JPEG is available at <c>/preview</c>; the original stays at <c>/file</c>.</param>
public sealed record ReceiptDetailDto(
    Guid Id,
    int Number,
    ReceiptStatus Status,
    string? VendorName,
    DateOnly? ReceiptDate,
    string? Currency,
    decimal? Subtotal,
    decimal? Tax,
    decimal? Total,
    string? PaymentMethod,
    Guid? CategoryId,
    bool CategorySuggestedByAi,
    string? Notes,
    string? FailureReason,
    string FileName,
    string ContentType,
    long SizeBytes,
    bool HasPreview,
    IReadOnlyList<LineItemDto> LineItems,
    IReadOnlyList<ExtractionFlagDto> ExtractionFlags,
    string? BaseCurrency,
    decimal? ExchangeRate,
    DateOnly? ExchangeRateDate,
    decimal? AmountCharged,
    decimal? BaseCurrencyTotal,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt);

/// <summary>
/// The review form: the receipt's details as the user corrected them. Replaces what's stored, line items included,
/// so a removed line is simply left out. Every field may be empty while editing; approving needs vendor, date,
/// currency, total and category.
/// </summary>
public sealed record UpdateReceiptRequest(
    [StringLength(200)] string? VendorName,
    DateOnly? ReceiptDate,
    [RegularExpression("^[A-Za-z]{3}$", ErrorMessage = "Use a three-letter currency code, e.g. PKR.")] string? Currency,
    [Range(typeof(decimal), "0", "999999999999")] decimal? Subtotal,
    [Range(typeof(decimal), "0", "999999999999")] decimal? Tax,
    [Range(typeof(decimal), "0", "999999999999")] decimal? Total,
    [StringLength(60)] string? PaymentMethod,
    Guid? CategoryId,
    [StringLength(500)] string? Notes,
    [Required, MaxLength(ExtractionSanitizer.MaxLineItems)] IReadOnlyList<LineItemRequest> LineItems);

/// <param name="Amount">May be negative, for discounts and refunds printed as lines.</param>
public sealed record LineItemRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Description,
    [Range(typeof(decimal), "0.001", "999999999")] decimal Quantity,
    [Range(typeof(decimal), "-999999999999", "999999999999")] decimal UnitPrice,
    [Range(typeof(decimal), "-999999999999", "999999999999")] decimal Amount);

/// <param name="AmountCharged">
/// What the bank actually charged, in the user's base currency, from their statement. When given, it's used as the
/// receipt's value instead of converting with the day's rate (banks add their own margin and fees).
/// </param>
public sealed record ApproveReceiptRequest(
    [Range(typeof(decimal), "0.01", "999999999999")] decimal? AmountCharged);

/// <summary>A receipt's original upload or its preview, ready to stream back to its owner.</summary>
/// <param name="Content">The caller disposes it.</param>
/// <param name="ContentType">The type detected at upload (or JPEG for a preview), not whatever the storage reports.</param>
/// <param name="DownloadName">E.g. "receipt-0042.heic": by entry number, never by the uploader's file name.</param>
public sealed record ReceiptFile(Stream Content, string ContentType, string DownloadName);

/// <param name="Status">Null for every status.</param>
/// <param name="Page">Starts at 1.</param>
public sealed record ReceiptQuery(ReceiptStatus? Status, int Page = 1, int PageSize = 25, ReceiptFilter? Filter = null);

/// <summary>Narrows the ledger. Every part is optional; together they all have to match.</summary>
/// <param name="Search">Part of the vendor's name, or of the file name while a receipt is still unread; any case.</param>
/// <param name="From">Receipt date on or after this day. Receipts without a date don't match a date filter.</param>
/// <param name="To">Receipt date on or before this day.</param>
public sealed record ReceiptFilter(string? Search = null, Guid? CategoryId = null, DateOnly? From = null, DateOnly? To = null);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
