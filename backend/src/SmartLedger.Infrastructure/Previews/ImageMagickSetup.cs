using ImageMagick;
using ImageMagick.Configuration;

namespace SmartLedger.Infrastructure.Previews;

/// <summary>
/// Locks ImageMagick down before first use. It understands hundreds of formats, some of which can run commands or
/// read other files (the 2016 "ImageTragick" bugs); receipts only need four, so everything else is switched off.
/// </summary>
internal static class ImageMagickSetup
{
    /// <summary>Largest width or height ImageMagick will decode. A real photo is under 10,000 px.</summary>
    public const uint MaxDimension = 20_000;

    // Only the formats receipts arrive in, and JPEG for the preview. No external programs ("delegates"),
    // and no "@file" indirect reads.
    private const string Policy = """
        <policymap>
          <policy domain="delegate" rights="none" pattern="*" />
          <policy domain="coder" rights="none" pattern="*" />
          <policy domain="coder" rights="read | write" pattern="{JPEG,PNG,WEBP,HEIC,HEIF}" />
          <policy domain="path" rights="none" pattern="@*" />
        </policymap>
        """;

    private static readonly Lazy<bool> Initialized = new(() =>
    {
        var files = ConfigurationFiles.Default;
        files.Policy.Data = Policy;
        MagickNET.Initialize(files);

        ResourceLimits.Width = MaxDimension;
        ResourceLimits.Height = MaxDimension;
        // A 50-megapixel image needs 200 MB decoded; the API container has 512 MB in total.
        ResourceLimits.Memory = 256 * 1024 * 1024;
        ResourceLimits.Time = 60; // seconds per image, a guard against files crafted to decode forever
        return true;
    });

    /// <summary>Safe to call any number of times; only the first call does anything.</summary>
    public static void EnsureInitialized() => _ = Initialized.Value;
}
