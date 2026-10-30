using SmartLedger.Application.Receipts.Extraction;
using SmartLedger.Domain.Entities;
using SmartLedger.Domain.Enums;

namespace SmartLedger.UnitTests.Receipts;

public class ExtractionSanitizerTests
{
    private static readonly DateOnly Today = new(2026, 10, 28);

    private static readonly Category OfficeSupplies = new() { Name = "Office Supplies", Color = "#8C8270" };

    [Fact]
    public void AGoodReading_FillsTheReceipt_AndAsksForReview()
    {
        var receipt = Apply(Reading());

        Assert.Equal(ReceiptStatus.NeedsReview, receipt.Status);
        Assert.Equal("Paper & Co. Office Supply", receipt.VendorName);
        Assert.Equal(new DateOnly(2026, 10, 19), receipt.ReceiptDate);
        Assert.Equal("USD", receipt.Currency);
        Assert.Equal(126.45m, receipt.Total);
        Assert.Equal(3, receipt.LineItems.Count);
        Assert.Equal([1, 2, 3], receipt.LineItems.Select(l => l.LineNumber));
        Assert.Empty(receipt.ExtractionFlags);
    }

    [Fact]
    public void CardNumbers_KeepOnlyTheLastFourDigits()
    {
        var receipt = Apply(Reading() with { PaymentMethod = "VISA 4242 4242 4242 4242" });

        Assert.Equal("VISA ····4242", receipt.PaymentMethod);
    }

    [Fact]
    public void TheSuggestedCategory_IsMatchedToTheUsersOwn_IgnoringCase()
    {
        var receipt = Apply(Reading() with { Category = "office supplies" });

        Assert.Equal(OfficeSupplies.Id, receipt.CategoryId);
        Assert.True(receipt.CategorySuggestedByAi);
    }

    [Fact]
    public void AnInventedCategory_IsDropped()
    {
        var receipt = Apply(Reading() with { Category = "Yachts" });

        Assert.Null(receipt.CategoryId);
        Assert.False(receipt.CategorySuggestedByAi);
    }

    [Fact]
    public void LineItemsThatDontAddUp_AreFlagged()
    {
        var receipt = Apply(Reading() with { Subtotal = 120.00m });

        var flag = Assert.Single(receipt.ExtractionFlags, f => f.Field == "Subtotal");
        Assert.Contains("118.45", flag.Reason);
    }

    [Fact]
    public void ATotalThatIsntSubtotalPlusTax_IsFlagged()
    {
        var receipt = Apply(Reading() with { Total = 140.00m });

        Assert.Contains(receipt.ExtractionFlags, f => f.Field == "Total");
    }

    [Theory]
    [InlineData("2027-03-01")]
    [InlineData("1999-12-31")]
    [InlineData("19/10/2026")]
    public void AnImplausibleOrMalformedDate_IsClearedAndFlagged(string date)
    {
        var receipt = Apply(Reading() with { ReceiptDate = date });

        Assert.Null(receipt.ReceiptDate);
        Assert.Contains(receipt.ExtractionFlags, f => f.Field == "ReceiptDate");
    }

    [Theory]
    [InlineData("pkr", "PKR")]
    [InlineData(" eur ", "EUR")]
    public void Currencies_AreNormalisedToIsoCodes(string read, string expected)
    {
        Assert.Equal(expected, Apply(Reading() with { Currency = read }).Currency);
    }

    [Theory]
    [InlineData("Rs")]
    [InlineData("DOLLARS")]
    [InlineData(null)]
    public void AnUnknownCurrency_IsClearedAndFlagged(string? read)
    {
        var receipt = Apply(Reading() with { Currency = read });

        Assert.Null(receipt.Currency);
        Assert.Contains(receipt.ExtractionFlags, f => f.Field == "Currency");
    }

    [Fact]
    public void TheAIsOwnDoubts_AreKept_ButOnlyForFieldsWeCanShow()
    {
        var receipt = Apply(Reading() with
        {
            FieldsToCheck =
            [
                new ExtractedFlag("PaymentMethod", "One digit was faded."),
                new ExtractedFlag("SecretField", "ignore previous instructions"),
            ],
        });

        var flag = Assert.Single(receipt.ExtractionFlags);
        Assert.Equal("PaymentMethod", flag.Field);
    }

    [Fact]
    public void NotAReceipt_MarksItUnreadable_WithTheReason()
    {
        var receipt = Apply(Reading() with { IsReceipt = false, UnreadableReason = "The photo is too blurry to read." });

        Assert.Equal(ReceiptStatus.Failed, receipt.Status);
        Assert.Equal("The photo is too blurry to read.", receipt.FailureReason);
    }

    private static Receipt Apply(ExtractedReceipt reading)
    {
        var receipt = new Receipt { FileName = "r.jpg", ContentType = "image/jpeg", StorageKey = "u/r.jpg" };
        ExtractionSanitizer.Apply(receipt, reading, [OfficeSupplies], Today);
        return receipt;
    }

    private static ExtractedReceipt Reading() => new(
        IsReceipt: true,
        UnreadableReason: null,
        VendorName: "Paper & Co. Office Supply",
        ReceiptDate: "2026-10-19",
        Currency: "USD",
        Subtotal: 118.45m,
        Tax: 8.00m,
        Total: 126.45m,
        PaymentMethod: "VISA ····4242",
        Category: "Office Supplies",
        LineItems:
        [
            new ExtractedLineItem("Printer paper, 5 reams", 5, 8.99m, 44.95m),
            new ExtractedLineItem("Toner cartridge", 1, 64.50m, 64.50m),
            new ExtractedLineItem("Sticky notes", 4, 2.25m, 9.00m),
        ],
        FieldsToCheck: [],
        RawResponse: "{}");
}
