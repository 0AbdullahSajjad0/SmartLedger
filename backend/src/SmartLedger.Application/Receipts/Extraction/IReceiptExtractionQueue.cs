namespace SmartLedger.Application.Receipts.Extraction;

public interface IReceiptExtractionQueue
{
    void Enqueue(Guid receiptId);
}
