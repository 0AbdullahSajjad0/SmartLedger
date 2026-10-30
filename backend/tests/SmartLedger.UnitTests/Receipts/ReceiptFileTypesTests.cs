using SmartLedger.Application.Receipts;

namespace SmartLedger.UnitTests.Receipts;

public class ReceiptFileTypesTests
{
    public static TheoryData<byte[], string> KnownFormats => new()
    {
        { [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0], "image/jpeg" },
        { [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0], "image/png" },
        { [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8], "image/webp" },
        { [0, 0, 0, 0x18, .. "ftypheic"u8], "image/heic" },
        { [0, 0, 0, 0x18, .. "ftypmif1"u8], "image/heic" },
        { [.. "%PDF-1.7"u8, 0, 0, 0, 0], "application/pdf" },
    };

    [Theory]
    [MemberData(nameof(KnownFormats))]
    public void Detect_RecognisesSupportedFormatsByTheirFirstBytes(byte[] header, string expectedContentType)
    {
        Assert.Equal(expectedContentType, ReceiptFileTypes.Detect(header)?.ContentType);
    }

    [Theory]
    [InlineData("<html><body>")]
    [InlineData("MZ\u0090\u0000")]
    [InlineData("PK\u0003\u0004")]
    [InlineData("")]
    public void Detect_RejectsEverythingElse(string content)
    {
        Assert.Null(ReceiptFileTypes.Detect(System.Text.Encoding.Latin1.GetBytes(content)));
    }
}
