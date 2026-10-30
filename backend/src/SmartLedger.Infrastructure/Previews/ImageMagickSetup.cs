using ImageMagick;
using ImageMagick.Configuration;

namespace SmartLedger.Infrastructure.Previews;

internal static class ImageMagickSetup
{
    public const uint MaxDimension = 20_000;

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
        ResourceLimits.Memory = 256 * 1024 * 1024;
        ResourceLimits.Time = 60;
        return true;
    });

    public static void EnsureInitialized() => _ = Initialized.Value;
}
