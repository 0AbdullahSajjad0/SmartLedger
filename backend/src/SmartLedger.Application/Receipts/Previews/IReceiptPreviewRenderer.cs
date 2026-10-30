namespace SmartLedger.Application.Receipts.Previews;

public interface IReceiptPreviewRenderer
{
    Task<byte[]> RenderAsync(Stream original, string contentType, CancellationToken cancellationToken);
}

public sealed class UnreadableReceiptFileException(string message, Exception? innerException = null)
    : Exception(message, innerException);
