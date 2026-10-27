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
    IReadOnlyList<LineItemDto> LineItems,
    IReadOnlyList<ExtractionFlagDto> ExtractionFlags,
    string? BaseCurrency,
    decimal? BaseCurrencyTotal,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt);

/// <summary>An original uploaded file, ready to stream back to its owner.</summary>
/// <param name="Content">The caller disposes it.</param>
/// <param name="ContentType">The type detected at upload, not whatever the storage reports.</param>
/// <param name="Extension">E.g. ".jpg", for naming the download.</param>
public sealed record ReceiptFile(Stream Content, string ContentType, int ReceiptNumber, string Extension);

/// <param name="Status">Null for every status.</param>
/// <param name="Page">Starts at 1.</param>
public sealed record ReceiptQuery(ReceiptStatus? Status, int Page = 1, int PageSize = 25);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
