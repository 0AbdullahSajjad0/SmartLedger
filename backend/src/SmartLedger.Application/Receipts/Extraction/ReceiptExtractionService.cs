using Microsoft.Extensions.Logging;
using SmartLedger.Application.Categories;
using SmartLedger.Application.Common;
using SmartLedger.Application.Common.Storage;
using SmartLedger.Application.Receipts.Previews;
using SmartLedger.Domain.Entities;
using SmartLedger.Domain.Enums;

namespace SmartLedger.Application.Receipts.Extraction;

public interface IReceiptExtractionService
{
    Task ProcessAsync(Guid receiptId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ListPendingAsync(CancellationToken cancellationToken);
}

internal sealed class ReceiptExtractionService(
    IReceiptRepository receipts,
    ICategoryRepository categories,
    IUserDirectory users,
    IFileStorage storage,
    IReceiptPreviewRenderer previewRenderer,
    IReceiptExtractor extractor,
    TimeProvider timeProvider,
    ILogger<ReceiptExtractionService> logger) : IReceiptExtractionService
{
    public const int MaxAttempts = 3;

    private const string MissingFile = "The original file is missing. Please upload it again.";

    public async Task ProcessAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        var receipt = await receipts.GetForUpdateAsync(receiptId, cancellationToken);
        if (receipt is null || receipt.Status != ReceiptStatus.Processing)
        {
            return;
        }

        var preview = await EnsurePreviewAsync(receipt, cancellationToken);
        if (preview is null)
        {
            return;
        }

        var isPdf = receipt.ContentType == ReceiptFileTypes.Pdf.ContentType;
        var content = isPdf
            ? (await storage.OpenReadAsync(receipt.StorageKey, cancellationToken))?.Content
            : new MemoryStream(preview);
        if (content is null)
        {
            receipt.MarkFailed(MissingFile);
            await receipts.SaveChangesAsync(cancellationToken);
            return;
        }

        var userCategories = await categories.ListAsync(receipt.UserId, cancellationToken);
        var baseCurrency = await users.GetBaseCurrencyAsync(receipt.UserId, cancellationToken) ?? "USD";

        ExtractedReceipt result;
        try
        {
            await using (content)
            {
                result = await extractor.ExtractAsync(
                    new ExtractionRequest(
                        content,
                        isPdf ? receipt.ContentType : ReceiptFileTypes.Jpeg.ContentType,
                        userCategories.Where(c => !c.IsArchived).Select(c => c.Name).ToList(),
                        baseCurrency),
                    cancellationToken);
            }
        }
        catch (ReceiptExtractionUnavailableException ex)
        {
            receipt.ExtractionAttempts++;
            logger.LogWarning(ex, "AI extraction unavailable for receipt {ReceiptId} (attempt {Attempt} of {Max})",
                receipt.Id, receipt.ExtractionAttempts, MaxAttempts);

            if (receipt.ExtractionAttempts >= MaxAttempts)
            {
                receipt.MarkFailed("AI couldn't read this receipt right now. Please upload it again later.");
            }

            await receipts.SaveChangesAsync(cancellationToken);
            return;
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        ExtractionSanitizer.Apply(receipt, result, userCategories, today);
        await receipts.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Receipt {ReceiptId} read by AI: {Status}, {FlagCount} field(s) to check",
            receipt.Id, receipt.Status, receipt.ExtractionFlags.Count);
    }

    public Task<IReadOnlyList<Guid>> ListPendingAsync(CancellationToken cancellationToken) =>
        receipts.ListIdsWithStatusAsync(ReceiptStatus.Processing, cancellationToken);

    private async Task<byte[]?> EnsurePreviewAsync(Receipt receipt, CancellationToken cancellationToken)
    {
        if (receipt.PreviewStorageKey is not null
            && await storage.OpenReadAsync(receipt.PreviewStorageKey, cancellationToken) is { } existing)
        {
            await using (existing.Content)
            {
                using var copy = new MemoryStream();
                await existing.Content.CopyToAsync(copy, cancellationToken);
                return copy.ToArray();
            }
        }

        var original = await storage.OpenReadAsync(receipt.StorageKey, cancellationToken);
        if (original is null)
        {
            receipt.MarkFailed(MissingFile);
            await receipts.SaveChangesAsync(cancellationToken);
            return null;
        }

        byte[] preview;
        try
        {
            await using (original.Content)
            {
                preview = await previewRenderer.RenderAsync(original.Content, receipt.ContentType, cancellationToken);
            }
        }
        catch (UnreadableReceiptFileException ex)
        {
            logger.LogInformation(ex, "Receipt {ReceiptId} couldn't be opened as {ContentType}", receipt.Id, receipt.ContentType);
            receipt.MarkFailed(ex.Message);
            await receipts.SaveChangesAsync(cancellationToken);
            return null;
        }

        var key = $"{receipt.UserId}/{receipt.Id}.preview{ReceiptFileTypes.Jpeg.Extension}";
        using (var stream = new MemoryStream(preview))
        {
            await storage.SaveAsync(key, stream, ReceiptFileTypes.Jpeg.ContentType, cancellationToken);
        }
        receipt.PreviewStorageKey = key;
        await receipts.SaveChangesAsync(cancellationToken);

        return preview;
    }
}
