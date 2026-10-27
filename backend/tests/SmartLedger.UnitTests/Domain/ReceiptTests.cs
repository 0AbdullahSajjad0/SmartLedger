using SmartLedger.Domain.Entities;
using SmartLedger.Domain.Enums;

namespace SmartLedger.UnitTests.Domain;

public class ReceiptTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewReceipt_IsProcessing()
    {
        var receipt = CreateReceipt();

        Assert.Equal(ReceiptStatus.Processing, receipt.Status);
    }

    [Fact]
    public void Approve_ConvertsTotalToBaseCurrency_AndRoundsToCents()
    {
        var receipt = CreateExtractedReceipt(total: 100.00m, currency: "EUR");

        receipt.Approve("USD", 1.08345m, Now);

        Assert.Equal(ReceiptStatus.Approved, receipt.Status);
        Assert.Equal("USD", receipt.BaseCurrency);
        Assert.Equal(1.08345m, receipt.ExchangeRate);
        Assert.Equal(108.35m, receipt.BaseCurrencyTotal);
        Assert.Equal(Now, receipt.ApprovedAt);
    }

    [Fact]
    public void Approve_WhileStillProcessing_Throws()
    {
        var receipt = CreateReceipt();

        Assert.Throws<InvalidOperationException>(() => receipt.Approve("USD", 1m, Now));
    }

    [Fact]
    public void Approve_WithoutTotal_Throws()
    {
        var receipt = CreateExtractedReceipt(total: 10m, currency: "USD");
        receipt.Total = null;

        Assert.Throws<InvalidOperationException>(() => receipt.Approve("USD", 1m, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Approve_WithNonPositiveRate_Throws(decimal rate)
    {
        var receipt = CreateExtractedReceipt(total: 10m, currency: "USD");

        Assert.Throws<ArgumentOutOfRangeException>(() => receipt.Approve("USD", rate, Now));
    }

    [Fact]
    public void MarkFailed_RecordsTheReason()
    {
        var receipt = CreateReceipt();

        receipt.MarkFailed("Too blurry to read.");

        Assert.Equal(ReceiptStatus.Failed, receipt.Status);
        Assert.Equal("Too blurry to read.", receipt.FailureReason);
    }

    [Fact]
    public void Reject_AfterApproval_Throws()
    {
        var receipt = CreateExtractedReceipt(total: 10m, currency: "USD");
        receipt.Approve("USD", 1m, Now);

        Assert.Throws<InvalidOperationException>(receipt.Reject);
    }

    private static Receipt CreateReceipt() => new()
    {
        FileName = "receipt.jpg",
        ContentType = "image/jpeg",
        StorageKey = "test/receipt.jpg",
    };

    private static Receipt CreateExtractedReceipt(decimal total, string currency)
    {
        var receipt = CreateReceipt();
        receipt.VendorName = "Test Vendor";
        receipt.ReceiptDate = new DateOnly(2026, 10, 19);
        receipt.Currency = currency;
        receipt.Total = total;
        receipt.MarkExtracted();
        return receipt;
    }
}
