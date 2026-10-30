using System.ComponentModel.DataAnnotations;
using SmartLedger.Application.Receipts.Extraction;
using SmartLedger.Domain.Enums;

namespace SmartLedger.Application.Receipts;

public sealed record UploadedFile(string FileName, long Length, Func<Stream> OpenReadStream);

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

public sealed record LineItemRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Description,
    [Range(typeof(decimal), "0.001", "999999999")] decimal Quantity,
    [Range(typeof(decimal), "-999999999999", "999999999999")] decimal UnitPrice,
    [Range(typeof(decimal), "-999999999999", "999999999999")] decimal Amount);

public sealed record ApproveReceiptRequest(
    [Range(typeof(decimal), "0.01", "999999999999")] decimal? AmountCharged);

public sealed record ReceiptFile(Stream Content, string ContentType, string DownloadName);

public sealed record ReceiptQuery(ReceiptStatus? Status, int Page = 1, int PageSize = 25, ReceiptFilter? Filter = null);

public sealed record ReceiptFilter(string? Search = null, Guid? CategoryId = null, DateOnly? From = null, DateOnly? To = null);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
