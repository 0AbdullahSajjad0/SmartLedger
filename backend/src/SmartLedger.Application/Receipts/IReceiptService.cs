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
}
