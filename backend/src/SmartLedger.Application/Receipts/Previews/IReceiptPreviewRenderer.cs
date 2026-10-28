namespace SmartLedger.Application.Receipts.Previews;

/// <summary>
/// Makes the cleaned copy of an uploaded receipt that the app shows and, for photos, sends to AI: a JPEG
/// turned upright, limited in size, with all metadata removed (camera details, GPS location). The original
/// upload is kept untouched for the owner to download.
/// </summary>
public interface IReceiptPreviewRenderer
{
    /// <param name="original">The uploaded file. Read to the end, not disposed.</param>
    /// <param name="contentType">The type detected from the file's bytes at upload.</param>
    /// <returns>The preview as JPEG bytes.</returns>
    /// <exception cref="UnreadableReceiptFileException">The file is damaged, too large in pixels, or password-protected.</exception>
    Task<byte[]> RenderAsync(Stream original, string contentType, CancellationToken cancellationToken);
}

/// <summary>The upload can't be turned into an image. The message is safe to show the user.</summary>
public sealed class UnreadableReceiptFileException(string message, Exception? innerException = null)
    : Exception(message, innerException);
