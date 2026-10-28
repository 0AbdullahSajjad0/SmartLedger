using SmartLedger.Application.Common;
using SmartLedger.Application.Common.Storage;
using SmartLedger.Application.Receipts.Extraction;
using SmartLedger.Domain.Entities;

namespace SmartLedger.Application.Receipts;

internal sealed class ReceiptService(
    IReceiptRepository repository,
    IFileStorage storage,
    IReceiptExtractionQueue extractionQueue,
    ICurrentUser currentUser) : IReceiptService
{
    private const int MaxPageSize = 50;

    private static readonly Error NotFound = Error.NotFound("receipts.not_found", "Receipt not found.");

    // Every endpoint behind this service requires sign-in, so a missing user is a bug, not a user error.
    private Guid UserId => currentUser.UserId ?? throw new InvalidOperationException("No signed-in user.");

    public async Task<Result<IReadOnlyList<ReceiptSummaryDto>>> UploadAsync(
        IReadOnlyList<UploadedFile> files, CancellationToken cancellationToken)
    {
        var userId = UserId;

        if (files.Count == 0)
        {
            return Error.Validation("receipts.no_files", "Choose at least one file.");
        }

        if (files.Count > ReceiptFileTypes.MaxFilesPerUpload)
        {
            return Error.Validation("receipts.too_many_files", $"Upload at most {ReceiptFileTypes.MaxFilesPerUpload} files at a time.");
        }

        // Check every file before storing any, so a bad file doesn't leave half a batch behind.
        var accepted = new List<(UploadedFile File, ReceiptFileType Type)>();
        foreach (var file in files)
        {
            var check = await IdentifyAsync(file, cancellationToken);
            if (!check.IsSuccess)
            {
                return check.Error!;
            }
            accepted.Add((file, check.Value));
        }

        var receipts = new List<Receipt>();
        try
        {
            foreach (var (file, type) in accepted)
            {
                var receiptId = Guid.CreateVersion7();
                var receipt = new Receipt
                {
                    Id = receiptId,
                    UserId = userId,
                    FileName = Path.GetFileName(file.FileName),
                    ContentType = type.ContentType,
                    SizeBytes = file.Length,
                    // Keyed by user and receipt id: never by the user's file name, which could contain anything.
                    StorageKey = $"{userId}/{receiptId}{type.Extension}",
                };

                await using var content = file.OpenReadStream();
                await storage.SaveAsync(receipt.StorageKey, content, type.ContentType, cancellationToken);
                receipts.Add(receipt);
            }

            await repository.AddAsync(userId, receipts, cancellationToken);
        }
        catch
        {
            // Don't leave orphaned files when the batch fails part-way.
            foreach (var receipt in receipts)
            {
                await storage.DeleteAsync(receipt.StorageKey, CancellationToken.None);
            }
            throw;
        }

        // Reading happens in the background, so the upload answers immediately.
        foreach (var receipt in receipts)
        {
            extractionQueue.Enqueue(receipt.Id);
        }

        return receipts.Select(ToSummary).ToList();
    }

    public async Task<PagedResult<ReceiptSummaryDto>> ListAsync(ReceiptQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var (items, totalCount) = await repository.ListAsync(UserId, query.Status, page, pageSize, cancellationToken);

        return new PagedResult<ReceiptSummaryDto>(items.Select(ToSummary).ToList(), page, pageSize, totalCount);
    }

    public async Task<Result<ReceiptDetailDto>> GetAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        var receipt = await repository.GetAsync(UserId, receiptId, cancellationToken);
        return receipt is null ? NotFound : ToDetail(receipt);
    }

    public async Task<Result<ReceiptFile>> OpenFileAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        var receipt = await repository.GetAsync(UserId, receiptId, cancellationToken);
        if (receipt is null)
        {
            return NotFound;
        }

        var file = await storage.OpenReadAsync(receipt.StorageKey, cancellationToken);
        if (file is null)
        {
            return NotFound;
        }

        return new ReceiptFile(file.Content, receipt.ContentType, receipt.Number, Path.GetExtension(receipt.StorageKey));
    }

    private static async Task<Result<ReceiptFileType>> IdentifyAsync(UploadedFile file, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(file.FileName);

        if (file.Length == 0)
        {
            return Error.Validation("receipts.empty_file", $"\"{name}\" is empty.");
        }

        if (file.Length > ReceiptFileTypes.MaxSizeBytes)
        {
            return Error.Validation("receipts.file_too_large", $"\"{name}\" is larger than 10 MB.");
        }

        var header = new byte[ReceiptFileTypes.SignatureLength];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);

        return ReceiptFileTypes.Detect(header.AsSpan(0, read)) is { } type
            ? type
            : Error.Validation("receipts.unsupported_type", $"\"{name}\" isn't a JPG, PNG, WebP, HEIC or PDF file.");
    }

    private static ReceiptSummaryDto ToSummary(Receipt r) => new(
        r.Id, r.Number, r.Status, r.VendorName, r.ReceiptDate, r.Currency, r.Total, r.BaseCurrencyTotal,
        r.CategoryId, r.FileName, r.CreatedAt);

    private static ReceiptDetailDto ToDetail(Receipt r) => new(
        r.Id, r.Number, r.Status, r.VendorName, r.ReceiptDate, r.Currency, r.Subtotal, r.Tax, r.Total,
        r.PaymentMethod, r.CategoryId, r.CategorySuggestedByAi, r.Notes, r.FailureReason,
        r.FileName, r.ContentType, r.SizeBytes,
        r.LineItems.OrderBy(l => l.LineNumber)
            .Select(l => new LineItemDto(l.LineNumber, l.Description, l.Quantity, l.UnitPrice, l.Amount))
            .ToList(),
        r.ExtractionFlags.Select(f => new ExtractionFlagDto(f.Field, f.Reason)).ToList(),
        r.BaseCurrency, r.BaseCurrencyTotal, r.ApprovedAt, r.CreatedAt);
}
