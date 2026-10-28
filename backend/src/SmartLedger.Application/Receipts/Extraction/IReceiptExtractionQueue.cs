namespace SmartLedger.Application.Receipts.Extraction;

/// <summary>Hands uploaded receipts to the background worker that reads them.</summary>
public interface IReceiptExtractionQueue
{
    void Enqueue(Guid receiptId);
}
