using Microsoft.Extensions.Logging;
using SmartLedger.Application.Categories;
using SmartLedger.Application.Common;
using SmartLedger.Application.Common.Storage;
using SmartLedger.Domain.Enums;

namespace SmartLedger.Application.Receipts.Extraction;

public interface IReceiptExtractionService
{
    /// <summary>Reads one receipt with AI. Does nothing unless the receipt is still waiting to be read.</summary>
    Task ProcessAsync(Guid receiptId, CancellationToken cancellationToken);

    /// <summary>Receipts still waiting to be read, e.g. after a restart.</summary>
    Task<IReadOnlyList<Guid>> ListPendingAsync(CancellationToken cancellationToken);
}

internal sealed class ReceiptExtractionService(
    IReceiptRepository receipts,
    ICategoryRepository categories,
    IUserDirectory users,
    IFileStorage storage,
    IReceiptExtractor extractor,
    TimeProvider timeProvider,
    ILogger<ReceiptExtractionService> logger) : IReceiptExtractionService
{
    /// <summary>After this many failed attempts to reach the AI service, the receipt is marked unreadable.</summary>
    public const int MaxAttempts = 3;

    public async Task ProcessAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        var receipt = await receipts.GetForUpdateAsync(receiptId, cancellationToken);
        if (receipt is null || receipt.Status != ReceiptStatus.Processing)
        {
            return;
        }

        var file = await storage.OpenReadAsync(receipt.StorageKey, cancellationToken);
        if (file is null)
        {
            receipt.MarkFailed("The original file is missing. Please upload it again.");
            await receipts.SaveChangesAsync(cancellationToken);
            return;
        }

        var userCategories = await categories.ListAsync(receipt.UserId, cancellationToken);
        var baseCurrency = await users.GetBaseCurrencyAsync(receipt.UserId, cancellationToken) ?? "USD";

        ExtractedReceipt result;
        try
        {
            await using (file.Content)
            {
                result = await extractor.ExtractAsync(
                    new ExtractionRequest(
                        file.Content,
                        receipt.ContentType,
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
}
