using SmartLedger.Application.Receipts.Extraction;

namespace SmartLedger.Api.IntegrationTests;

/// <summary>
/// Stands in for Gemini in integration tests: decides what it "read" from a marker in the uploaded bytes,
/// so tests are fast, free and deterministic.
/// </summary>
internal sealed class FakeReceiptExtractor : IReceiptExtractor
{
    public const string NotAReceiptMarker = "NOT-A-RECEIPT";

    public async Task<ExtractedReceipt> ExtractAsync(ExtractionRequest request, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Content);
        var content = await reader.ReadToEndAsync(cancellationToken);

        if (content.Contains(NotAReceiptMarker, StringComparison.Ordinal))
        {
            return new ExtractedReceipt(false, "This is a photo of a cat, not a receipt.",
                null, null, null, null, null, null, null, null, [], [], "{}");
        }

        return new ExtractedReceipt(
            IsReceipt: true,
            UnreadableReason: null,
            VendorName: "Paper & Co. Office Supply",
            ReceiptDate: "2026-10-19",
            Currency: "usd",
            Subtotal: 118.45m,
            Tax: 8.00m,
            Total: 126.45m,
            PaymentMethod: "VISA 4242 4242 4242 4242",
            Category: "office supplies",
            LineItems:
            [
                new ExtractedLineItem("Printer paper, 5 reams", 5, 8.99m, 44.95m),
                new ExtractedLineItem("Toner cartridge", 1, 64.50m, 64.50m),
                new ExtractedLineItem("Sticky notes", 4, 2.25m, 9.00m),
            ],
            FieldsToCheck: [new ExtractedFlag("PaymentMethod", "One digit of the card number was faded.")],
            RawResponse: """{"fake":true}""");
    }
}
