using SmartLedger.Domain.Entities;
using SmartLedger.Domain.Enums;

namespace SmartLedger.UnitTests.Domain;

public class ReceiptTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly RateDate = new(2026, 10, 19);

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

        receipt.Approve("USD", 1.08345m, RateDate, amountCharged: null, Now);

        Assert.Equal(ReceiptStatus.Approved, receipt.Status);
        Assert.Equal("USD", receipt.BaseCurrency);
        Assert.Equal(1.08345m, receipt.ExchangeRate);
        Assert.Equal(RateDate, receipt.ExchangeRateDate);
        Assert.Equal(108.35m, receipt.BaseCurrencyTotal);
        Assert.Equal(Now, receipt.ApprovedAt);
    }

    [Fact]
    public void Approve_WithTheAmountCharged_UsesItInsteadOfTheCalculatedAmount()
    {
        var receipt = CreateExtractedReceipt(total: 2900m, currency: "PKR");

        receipt.Approve("USD", 0.0036m, RateDate, amountCharged: 10.61m, Now);

        Assert.Equal(10.61m, receipt.AmountCharged);
        Assert.Equal(10.61m, receipt.BaseCurrencyTotal);
    }

    [Fact]
    public void Approve_WhileStillProcessing_Throws()
    {
        var receipt = CreateReceipt();

        Assert.Throws<InvalidOperationException>(() => receipt.Approve("USD", 1m, RateDate, null, Now));
    }

    [Fact]
    public void Approve_WithoutTotal_Throws()
    {
        var receipt = CreateExtractedReceipt(total: 10m, currency: "USD");
        receipt.Total = null;

        Assert.False(receipt.IsReadyToApprove);
        Assert.Throws<InvalidOperationException>(() => receipt.Approve("USD", 1m, RateDate, null, Now));
    }

    [Fact]
    public void Approve_WithoutCategory_Throws()
    {
        var receipt = CreateExtractedReceipt(total: 10m, currency: "USD");
        receipt.CategoryId = null;

        Assert.Throws<InvalidOperationException>(() => receipt.Approve("USD", 1m, RateDate, null, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Approve_WithNonPositiveRate_Throws(decimal rate)
    {
        var receipt = CreateExtractedReceipt(total: 10m, currency: "USD");

        Assert.Throws<ArgumentOutOfRangeException>(() => receipt.Approve("USD", rate, RateDate, null, Now));
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
    public void EditingAReceiptAiCouldntRead_PutsItUpForReview()
    {
        var receipt = CreateReceipt();
        receipt.MarkFailed("Too blurry to read.");

        receipt.MarkEdited();

        Assert.Equal(ReceiptStatus.NeedsReview, receipt.Status);
        Assert.Null(receipt.FailureReason);
    }

    [Fact]
    public void AnApprovedReceipt_CantBeEdited()
    {
        var receipt = CreateApprovedReceipt();

        Assert.False(receipt.IsEditable);
        Assert.Throws<InvalidOperationException>(receipt.MarkEdited);
    }

    [Fact]
    public void Reject_AfterApproval_Throws()
    {
        var receipt = CreateApprovedReceipt();

        Assert.Throws<InvalidOperationException>(receipt.Reject);
    }

    [Fact]
    public void AReceiptAiCouldntRead_CanBeRejected()
    {
        var receipt = CreateReceipt();
        receipt.MarkFailed("This is a photo of a cat.");

        receipt.Reject();

        Assert.Equal(ReceiptStatus.Rejected, receipt.Status);
    }

    [Fact]
    public void Reopen_TakesBackTheApproval_AndClearsTheFrozenConversion()
    {
        var receipt = CreateApprovedReceipt();

        receipt.Reopen();

        Assert.Equal(ReceiptStatus.NeedsReview, receipt.Status);
        Assert.Null(receipt.BaseCurrency);
        Assert.Null(receipt.ExchangeRate);
        Assert.Null(receipt.ExchangeRateDate);
        Assert.Null(receipt.AmountCharged);
        Assert.Null(receipt.BaseCurrencyTotal);
        Assert.Null(receipt.ApprovedAt);
        Assert.Equal(126.45m, receipt.Total);
    }

    [Fact]
    public void Reopen_WorksForRejectedReceipts()
    {
        var receipt = CreateExtractedReceipt(total: 10m, currency: "USD");
        receipt.Reject();

        receipt.Reopen();

        Assert.Equal(ReceiptStatus.NeedsReview, receipt.Status);
    }

    [Fact]
    public void Reopen_WhileWaitingForReview_Throws()
    {
        var receipt = CreateExtractedReceipt(total: 10m, currency: "USD");

        Assert.Throws<InvalidOperationException>(receipt.Reopen);
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
        receipt.CategoryId = Guid.CreateVersion7();
        receipt.MarkExtracted();
        return receipt;
    }

    private static Receipt CreateApprovedReceipt()
    {
        var receipt = CreateExtractedReceipt(total: 126.45m, currency: "EUR");
        receipt.Approve("USD", 1.08m, RateDate, amountCharged: null, Now);
        return receipt;
    }
}
