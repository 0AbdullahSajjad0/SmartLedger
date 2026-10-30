using System.Text;

namespace SmartLedger.Application.Receipts;

public sealed record ReceiptFileType(string ContentType, string Extension);

public static class ReceiptFileTypes
{
    public const long MaxSizeBytes = 10 * 1024 * 1024;

    public const int MaxFilesPerUpload = 10;

    public const int SignatureLength = 12;

    public static readonly ReceiptFileType Jpeg = new("image/jpeg", ".jpg");
    public static readonly ReceiptFileType Png = new("image/png", ".png");
    public static readonly ReceiptFileType Webp = new("image/webp", ".webp");
    public static readonly ReceiptFileType Heic = new("image/heic", ".heic");
    public static readonly ReceiptFileType Pdf = new("application/pdf", ".pdf");

    private static readonly string[] HeicBrands = ["heic", "heix", "hevc", "hevx", "mif1", "msf1"];

    public static ReceiptFileType? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return Jpeg;
        }

        if (header.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return Png;
        }

        if (header.StartsWith("%PDF-"u8))
        {
            return Pdf;
        }

        if (header.Length >= 12 && header.StartsWith("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
        {
            return Webp;
        }

        if (header.Length >= 12 && header[4..8].SequenceEqual("ftyp"u8)
            && HeicBrands.Contains(Encoding.ASCII.GetString(header[8..12])))
        {
            return Heic;
        }

        return null;
    }
}
