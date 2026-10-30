using SmartLedger.Application.Common;

namespace SmartLedger.Application.Receipts;

public interface IReceiptService
{
    Task<Result<IReadOnlyList<ReceiptSummaryDto>>> UploadAsync(IReadOnlyList<UploadedFile> files, CancellationToken cancellationToken);

    Task<PagedResult<ReceiptSummaryDto>> ListAsync(ReceiptQuery query, CancellationToken cancellationToken);

    Task<Result<ReceiptDetailDto>> GetAsync(Guid receiptId, CancellationToken cancellationToken);

    Task<Result<ReceiptFile>> OpenFileAsync(Guid receiptId, CancellationToken cancellationToken);

    Task<Result<ReceiptFile>> OpenPreviewAsync(Guid receiptId, CancellationToken cancellationToken);

    Task<Result<ReceiptDetailDto>> UpdateAsync(Guid receiptId, UpdateReceiptRequest request, CancellationToken cancellationToken);

    Task<Result<ReceiptDetailDto>> ApproveAsync(Guid receiptId, ApproveReceiptRequest request, CancellationToken cancellationToken);

    Task<Result<ReceiptDetailDto>> RejectAsync(Guid receiptId, CancellationToken cancellationToken);

    Task<Result<ReceiptDetailDto>> ReopenAsync(Guid receiptId, CancellationToken cancellationToken);

    Task<Result<bool>> DeleteAsync(Guid receiptId, CancellationToken cancellationToken);
}
