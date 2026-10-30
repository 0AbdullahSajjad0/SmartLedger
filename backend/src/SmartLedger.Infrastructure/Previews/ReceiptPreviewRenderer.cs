using System.Runtime.Versioning;
using ImageMagick;
using PDFtoImage;
using PDFtoImage.Exceptions;
using SkiaSharp;
using SmartLedger.Application.Receipts;
using SmartLedger.Application.Receipts.Previews;

namespace SmartLedger.Infrastructure.Previews;

internal sealed class ReceiptPreviewRenderer : IReceiptPreviewRenderer
{
    public const uint MaxEdge = 3072;

    public const ulong MaxPixels = 50_000_000;

    private const uint JpegQuality = 85;
    private const int PdfDpi = 200;

    private const string Damaged = "This file couldn't be opened. It may be damaged; try exporting or photographing it again.";

    public async Task<byte[]> RenderAsync(Stream original, string contentType, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await original.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        if (contentType == ReceiptFileTypes.Pdf.ContentType)
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                return RenderPdfFirstPage(bytes);
            }
            throw new PlatformNotSupportedException("PDF previews need Windows, Linux or macOS.");
        }

        var format = contentType switch
        {
            "image/jpeg" => MagickFormat.Jpeg,
            "image/png" => MagickFormat.Png,
            "image/webp" => MagickFormat.WebP,
            "image/heic" => MagickFormat.Heic,
            _ => throw new ArgumentException($"Unsupported content type {contentType}.", nameof(contentType)),
        };
        return RenderPhoto(bytes, format);
    }

    private static byte[] RenderPhoto(byte[] bytes, MagickFormat format)
    {
        ImageMagickSetup.EnsureInitialized();

        var settings = new MagickReadSettings { Format = format, FrameIndex = 0, FrameCount = 1 };

        try
        {
            var info = new MagickImageInfo(bytes, settings);
            if ((ulong)info.Width * info.Height > MaxPixels)
            {
                throw new UnreadableReceiptFileException("This photo has too many pixels to process. Please upload a smaller copy.");
            }

            using var image = new MagickImage(bytes, settings);

            image.AutoOrient();

            if (image.ColorSpace == ColorSpace.CMYK)
            {
                image.TransformColorSpace(image.GetColorProfile() ?? ColorProfiles.USWebCoatedSWOP, ColorProfiles.SRGB);
            }
            else if (image.GetColorProfile() is not null)
            {
                image.TransformColorSpace(ColorProfiles.SRGB);
            }

            if (image.HasAlpha)
            {
                image.BackgroundColor = MagickColors.White;
                image.Alpha(AlphaAction.Remove);
            }

            image.Resize(new MagickGeometry(MaxEdge, MaxEdge) { Greater = true });

            image.Strip();

            image.Quality = JpegQuality;
            return image.ToByteArray(MagickFormat.Jpeg);
        }
        catch (MagickResourceLimitErrorException ex)
        {
            throw new UnreadableReceiptFileException("This photo is too large to process. Please upload a smaller copy.", ex);
        }
        catch (MagickException ex)
        {
            throw new UnreadableReceiptFileException(Damaged, ex);
        }
    }

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static byte[] RenderPdfFirstPage(byte[] pdf)
    {
        try
        {
            var page = Conversion.GetPageSize(pdf, page: 0);
            var longestInches = Math.Max(page.Width, page.Height) / 72f;
            var dpi = (int)Math.Clamp(MaxEdge / longestInches, 1, PdfDpi);

            using var bitmap = Conversion.ToImage(pdf, page: 0, options: new RenderOptions(
                Dpi: dpi,
                WithAnnotations: true,
                BackgroundColor: SKColors.White));
            using var jpeg = bitmap.Encode(SKEncodedImageFormat.Jpeg, (int)JpegQuality);
            return jpeg.ToArray();
        }
        catch (PdfPasswordProtectedException ex)
        {
            throw new UnreadableReceiptFileException("This PDF is password-protected. Remove the password and upload it again.", ex);
        }
        catch (PdfException ex)
        {
            throw new UnreadableReceiptFileException(Damaged, ex);
        }
    }
}
