using ImageMagick;
using SmartLedger.Infrastructure.Previews;

namespace SmartLedger.Api.IntegrationTests;

/// <summary>Real, tiny receipt files: the background worker decodes every upload, so made-up bytes would fail.</summary>
internal static class TestFiles
{
    /// <summary>The fake AI treats a photo of exactly this width as "not a receipt".</summary>
    public const uint NotAReceiptWidth = 77;

    static TestFiles() =>
        // Same locked-down ImageMagick as the app, so these files are made the way the app will read them.
        ImageMagickSetup.EnsureInitialized();

    public static byte[] Jpeg(uint width = 120, uint height = 60) => Image(width, height, MagickFormat.Jpeg);

    public static byte[] NotAReceiptJpeg() => Jpeg(NotAReceiptWidth, 40);

    public static byte[] Pdf() => ReceiptPreviewSelfCheck.SamplePdf();

    /// <summary>Made on Windows with its HEIF encoder: 400 × 200 stored, marked "rotate 90°", with a GPS location.</summary>
    public static byte[] RotatedHeicWithGps() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Samples", "rotated-with-gps.heic"));

    /// <summary>White, with a black block in the top-left corner (a fifth of each side) so rotation can be checked.</summary>
    public static MagickImage ImageWithMarker(uint width, uint height) =>
        FromPixels(width, height, (x, y) => x < width / 5 && y < height / 5 ? (byte)0 : (byte)255, alpha: 255);

    /// <summary>Fully transparent, like the background of a screenshot.</summary>
    public static MagickImage TransparentImage(uint width, uint height) => FromPixels(width, height, (_, _) => 0, alpha: 0);

    // Built from raw pixels: the app's ImageMagick policy blocks the "xc:" blank-canvas format, as it isn't a receipt format.
    private static MagickImage FromPixels(uint width, uint height, Func<uint, uint, byte> grey, byte alpha)
    {
        var pixels = new byte[width * height * 4];
        for (uint y = 0; y < height; y++)
        {
            for (uint x = 0; x < width; x++)
            {
                var i = ((y * width) + x) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = grey(x, y);
                pixels[i + 3] = alpha;
            }
        }

        var image = new MagickImage();
        image.ReadPixels(pixels, new PixelReadSettings(width, height, StorageType.Char, PixelMapping.RGBA));
        return image;
    }

    private static byte[] Image(uint width, uint height, MagickFormat format)
    {
        using var image = ImageWithMarker(width, height);
        return image.ToByteArray(format);
    }
}
