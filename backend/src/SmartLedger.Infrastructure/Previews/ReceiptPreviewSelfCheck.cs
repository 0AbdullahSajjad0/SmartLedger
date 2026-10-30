using System.Text;
using ImageMagick;
using SmartLedger.Application.Receipts;

namespace SmartLedger.Infrastructure.Previews;

public static class ReceiptPreviewSelfCheck
{
    public const string CommandLineFlag = "--check-image-processing";

    internal static byte[] SamplePdf()
    {
        const string content = "0 0 0 rg 20 340 80 40 re f";
        return Encoding.ASCII.GetBytes(
            "%PDF-1.4\n" +
            "1 0 obj <</Type /Catalog /Pages 2 0 R>> endobj\n" +
            "2 0 obj <</Type /Pages /Kids [3 0 R] /Count 1>> endobj\n" +
            "3 0 obj <</Type /Page /Parent 2 0 R /MediaBox [0 0 226 400] /Contents 4 0 R>> endobj\n" +
            $"4 0 obj <</Length {content.Length}>> stream\n{content}\nendstream endobj\n" +
            "trailer <</Root 1 0 R>>\n%%EOF\n");
    }

    public static async Task<bool> RunAsync(TextWriter output)
    {
        var renderer = new ReceiptPreviewRenderer();
        var ok = true;

        await using (var heic = typeof(ReceiptPreviewSelfCheck).Assembly
            .GetManifestResourceStream("SmartLedger.Infrastructure.Previews.self-check.heic")!)
        {
            ok &= await CheckAsync("HEIC", () => renderer.RenderAsync(heic, ReceiptFileTypes.Heic.ContentType, CancellationToken.None),
                expectedWidth: 200, expectedHeight: 400);
        }

        using (var pdf = new MemoryStream(SamplePdf()))
        {
            ok &= await CheckAsync("PDF", () => renderer.RenderAsync(pdf, ReceiptFileTypes.Pdf.ContentType, CancellationToken.None),
                expectedWidth: 627, expectedHeight: 1111);
        }

        output.WriteLine(ok ? "Image processing works." : "Image processing is broken.");
        return ok;

        async Task<bool> CheckAsync(string name, Func<Task<byte[]>> render, uint expectedWidth, uint expectedHeight)
        {
            try
            {
                var jpeg = await render();
                var info = new MagickImageInfo(jpeg);
                var passed = info.Format == MagickFormat.Jpeg && info.Width == expectedWidth && info.Height == expectedHeight;
                output.WriteLine($"{name}: {(passed ? "ok" : "unexpected result")}, {info.Format} {info.Width}x{info.Height}, {jpeg.Length} bytes");
                return passed;
            }
            catch (Exception ex)
            {
                output.WriteLine($"{name}: failed. {ex}");
                return false;
            }
        }
    }
}
