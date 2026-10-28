using System.Runtime.Versioning;
using ImageMagick;
using PDFtoImage;
using PDFtoImage.Exceptions;
using SkiaSharp;
using SmartLedger.Application.Receipts;
using SmartLedger.Application.Receipts.Previews;

namespace SmartLedger.Infrastructure.Previews;

/// <summary>
/// Photos are decoded and re-encoded with ImageMagick, so only pixels we wrote ourselves are stored and sent on:
/// nothing hidden in the original file survives. PDFs get their first page rendered with PDFium.
/// </summary>
internal sealed class ReceiptPreviewRenderer : IReceiptPreviewRenderer
{
    /// <summary>Longest side of a preview. Keeps small print legible on long receipts, at a fraction of the size.</summary>
    public const uint MaxEdge = 3072;

    /// <summary>Photos with more pixels than this (50 MP) are refused before decoding, so a small file can't claim to be enormous.</summary>
    public const ulong MaxPixels = 50_000_000;

    private const uint JpegQuality = 85;
    private const int PdfDpi = 200;

    private const string Damaged = "This file couldn't be opened. It may be damaged; try exporting or photographing it again.";

    public async Task<byte[]> RenderAsync(Stream original, string contentType, CancellationToken cancellationToken)
    {
        // At most 10 MB (checked at upload), so reading it into memory is fine and lets both libraries seek freely.
        using var buffer = new MemoryStream();
        await original.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        if (contentType == ReceiptFileTypes.Pdf.ContentType)
        {
            // PDFium ships native builds for these three only.
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

        // The detected format is stated, never guessed by ImageMagick from the contents.
        var settings = new MagickReadSettings { Format = format, FrameIndex = 0, FrameCount = 1 };

        try
        {
            // Read the size from the header first, without decoding any pixels.
            var info = new MagickImageInfo(bytes, settings);
            if ((ulong)info.Width * info.Height > MaxPixels)
            {
                throw new UnreadableReceiptFileException("This photo has too many pixels to process. Please upload a smaller copy.");
            }

            using var image = new MagickImage(bytes, settings);

            // Phones store photos sideways plus an "orientation" tag; apply it, since the tag is about to be removed.
            image.AutoOrient();

            // Convert colours to standard sRGB while the colour profile is still there (iPhones use Display P3).
            if (image.ColorSpace == ColorSpace.CMYK)
            {
                image.TransformColorSpace(image.GetColorProfile() ?? ColorProfiles.USWebCoatedSWOP, ColorProfiles.SRGB);
            }
            else if (image.GetColorProfile() is not null)
            {
                image.TransformColorSpace(ColorProfiles.SRGB);
            }

            // JPEG has no transparency: put transparent screenshots on white, like paper.
            if (image.HasAlpha)
            {
                image.BackgroundColor = MagickColors.White;
                image.Alpha(AlphaAction.Remove);
            }

            image.Resize(new MagickGeometry(MaxEdge, MaxEdge) { Greater = true }); // only ever shrinks

            // Removes EXIF (GPS location, camera, date taken), XMP, colour profiles and comments.
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
            // Page size is in points (1/72 inch). Render at print quality, but never past the preview size limit.
            var page = Conversion.GetPageSize(pdf, page: 0);
            var longestInches = Math.Max(page.Width, page.Height) / 72f;
            var dpi = (int)Math.Clamp(MaxEdge / longestInches, 1, PdfDpi);

            using var bitmap = Conversion.ToImage(pdf, page: 0, options: new RenderOptions(
                Dpi: dpi,
                WithAnnotations: true, // stamps such as "PAID" are often annotations
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
