using System.Buffers.Binary;
using ImageMagick;
using SmartLedger.Application.Receipts.Previews;
using SmartLedger.Infrastructure.Previews;

namespace SmartLedger.Api.IntegrationTests;

/// <summary>The real ImageMagick and PDFium, on real files. Runs on Windows locally and Linux in CI.</summary>
public class ReceiptPreviewRendererTests
{
    private readonly ReceiptPreviewRenderer renderer = new();

    // The app's security policy must be in place before these tests make their own images with ImageMagick.
    static ReceiptPreviewRendererTests() => ImageMagickSetup.EnsureInitialized();

    [Fact]
    public async Task AnIphoneStylePhoto_BecomesAnUprightJpeg_WithoutItsGpsLocation()
    {
        var heic = TestFiles.RotatedHeicWithGps();
        using (var original = new MagickImage(heic))
        {
            Assert.NotNull(original.GetExifProfile()?.GetValue(ExifTag.GPSLatitude)); // the sample really has one
        }

        using var preview = await RenderAsync(heic, "image/heic");

        Assert.Equal(MagickFormat.Jpeg, preview.Format);
        Assert.Equal((200u, 400u), (preview.Width, preview.Height));
        Assert.True(IsDark(preview, 190, 10), "the corner block should be top right after turning 90° clockwise");
        Assert.Empty(preview.ProfileNames);
    }

    [Fact]
    public async Task ASidewaysJpeg_IsTurnedUpright_AndLosesAllMetadata()
    {
        using var image = TestFiles.ImageWithMarker(400, 200);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Orientation, (ushort)6); // "rotate 90° clockwise to display"
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.GPSLatitude, [new Rational(31, 1), new Rational(31, 1), new Rational(0, 1)]);
        exif.SetValue(ExifTag.Model, "Phone");
        image.SetProfile(exif);
        image.Orientation = Orientation.RightTop; // ImageMagick writes this property into the EXIF when saving
        var jpeg = image.ToByteArray(MagickFormat.Jpeg);
        using (var original = new MagickImage(jpeg))
        {
            Assert.Equal(Orientation.RightTop, original.Orientation); // the test photo really is sideways
        }

        using var preview = await RenderAsync(jpeg, "image/jpeg");

        Assert.Equal((200u, 400u), (preview.Width, preview.Height));
        Assert.True(IsDark(preview, 190, 10));
        Assert.Null(preview.GetExifProfile());
        Assert.Empty(preview.ProfileNames);
    }

    [Fact]
    public async Task ALargePhoto_IsShrunkToTheMaximumEdge_KeepingItsShape()
    {
        using var image = TestFiles.ImageWithMarker(6000, 1500);

        using var preview = await RenderAsync(image.ToByteArray(MagickFormat.Png), "image/png");

        Assert.Equal((ReceiptPreviewRenderer.MaxEdge, 768u), (preview.Width, preview.Height));
    }

    [Fact]
    public async Task ASmallPhoto_IsNeverEnlarged()
    {
        using var preview = await RenderAsync(TestFiles.Jpeg(120, 60), "image/jpeg");

        Assert.Equal((120u, 60u), (preview.Width, preview.Height));
    }

    [Fact]
    public async Task ATransparentScreenshot_IsPutOnWhite()
    {
        using var image = TestFiles.TransparentImage(50, 50);

        using var preview = await RenderAsync(image.ToByteArray(MagickFormat.WebP), "image/webp");

        var pixel = preview.GetPixels().GetPixel(25, 25).ToColor()!;
        Assert.True(pixel.R > 250 && pixel.G > 250 && pixel.B > 250, $"expected white, got {pixel}");
    }

    [Fact]
    public async Task APdf_GetsItsFirstPageAsAJpeg()
    {
        using var preview = await RenderAsync(TestFiles.Pdf(), "application/pdf");

        // 226 × 400 points (an 80 mm till receipt) at 200 dpi. The block is drawn near the top left.
        Assert.Equal(MagickFormat.Jpeg, preview.Format);
        Assert.Equal((627u, 1111u), (preview.Width, preview.Height));
        Assert.True(IsDark(preview, 160, 110));
        Assert.False(IsDark(preview, 500, 900));
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("application/pdf")]
    public async Task ADamagedFile_IsReportedAsUnreadable(string contentType)
    {
        byte[] damaged = contentType == "application/pdf"
            ? [.. "%PDF-1.7\n"u8, .. new byte[64]]
            : [0xFF, 0xD8, 0xFF, 0xE0, .. new byte[64]];

        var error = await Assert.ThrowsAsync<UnreadableReceiptFileException>(() => RenderAsync(damaged, contentType));

        Assert.Contains("couldn't be opened", error.Message);
    }

    [Fact]
    public async Task AFileClaimingToBeHuge_IsRefusedBeforeDecoding()
    {
        // A "decompression bomb": a few bytes whose header promises 10,000 × 10,000 pixels (400 MB decoded).
        var png = WithPngSize(SmallPng(), 10_000, 10_000);

        var error = await Assert.ThrowsAsync<UnreadableReceiptFileException>(() => RenderAsync(png, "image/png"));

        Assert.Contains("too many pixels", error.Message);
    }

    [Fact]
    public void FormatsReceiptsDontUse_AreSwitchedOff()
    {
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'/>"u8.ToArray();

        Assert.Throws<MagickPolicyErrorException>(() => new MagickImage(svg, new MagickReadSettings { Format = MagickFormat.Svg }));
    }

    private async Task<MagickImage> RenderAsync(byte[] file, string contentType)
    {
        using var stream = new MemoryStream(file);
        return new MagickImage(await renderer.RenderAsync(stream, contentType, CancellationToken.None));
    }

    private static bool IsDark(MagickImage image, int x, int y)
    {
        var pixel = image.GetPixels().GetPixel(x, y).ToColor()!;
        return pixel.R < 60 && pixel.G < 60 && pixel.B < 60;
    }

    private static byte[] SmallPng()
    {
        using var image = TestFiles.ImageWithMarker(10, 10);
        return image.ToByteArray(MagickFormat.Png);
    }

    /// <summary>Rewrites a PNG's declared size (bytes 16–23 of the IHDR chunk) and fixes the chunk's checksum.</summary>
    private static byte[] WithPngSize(byte[] png, uint width, uint height)
    {
        var bytes = (byte[])png.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), height);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(29), Crc32(bytes.AsSpan(12, 17))); // "IHDR" + 13 data bytes
        return bytes;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }
        return ~crc;
    }
}
