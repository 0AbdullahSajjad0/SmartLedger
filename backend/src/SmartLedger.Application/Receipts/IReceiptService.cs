using SmartLedger.Application.Common;

namespace SmartLedger.Application.Receipts;

/// <summary>Receipts of the signed-in user. Every method works only on that user's receipts.</summary>
public interface IReceiptService
{
    /// <summary>Stores the files and creates one receipt per file, waiting for AI extraction.</summary>
    Task<Result<IReadOnlyList<ReceiptSummaryDto>>> UploadAsync(IReadOnlyList<UploadedFile> files, CancellationToken cancellationToken);

    Task<PagedResult<ReceiptSummaryDto>> ListAsync(ReceiptQuery query, CancellationToken cancellationToken);

    Task<Result<ReceiptDetailDto>> GetAsync(Guid receiptId, CancellationToken cancellationToken);

    /// <summary>The original uploaded file, exactly as received (photos include any location data).</summary>
    Task<Result<ReceiptFile>> OpenFileAsync(Guid receiptId, CancellationToken cancellationToken);

    /// <summary>The cleaned JPEG preview. Not found while it's still being made, or for receipts from before previews.</summary>
    Task<Result<ReceiptFile>> OpenPreviewAsync(Guid receiptId, CancellationToken cancellationToken);

    /// <summary>Saves the user's corrections. Only while waiting for review, or after AI couldn't read the receipt.</summary>
    Task<Result<ReceiptDetailDto>> UpdateAsync(Guid receiptId, UpdateReceiptRequest request, CancellationToken cancellationToken);

    /// <summary>Converts the total to the user's base currency at the receipt date's rate and freezes it.</summary>
    Task<Result<ReceiptDetailDto>> ApproveAsync(Guid receiptId, ApproveReceiptRequest request, CancellationToken cancellationToken);

    Task<Result<ReceiptDetailDto>> RejectAsync(Guid receiptId, CancellationToken cancellationToken);

    /// <summary>Takes back an approval or rejection, so the receipt can be corrected and decided again.</summary>
    Task<Result<ReceiptDetailDto>> ReopenAsync(Guid receiptId, CancellationToken cancellationToken);
}
