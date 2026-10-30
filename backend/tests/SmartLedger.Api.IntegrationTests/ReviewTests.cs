using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using SmartLedger.Application.Categories;
using SmartLedger.Application.ExchangeRates;
using SmartLedger.Application.Receipts;
using SmartLedger.Domain.Enums;

namespace SmartLedger.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ReviewTests(SmartLedgerApiFactory factory)
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = TestReceipts.Json;

    [Fact]
    public async Task Editing_SavesTheCorrections_AndAnswersTheNoteOnTheChangedField()
    {
        var (client, receipt) = await ReceiptWaitingForReviewAsync();
        var edit = EditOf(receipt) with
        {
            PaymentMethod = "VISA 4242 4242 4242 1111",
            LineItems = [new LineItemRequest("Printer paper, 5 reams", 5, 8.99m, 44.95m), new LineItemRequest("Stapler", 1, 12.5m, 12.5m)],
        };

        var saved = await PutAsync(client, receipt.Id, edit);

        Assert.Equal(ReceiptStatus.NeedsReview, saved.Status);
        Assert.Equal("VISA ····1111", saved.PaymentMethod);
        Assert.Empty(saved.ExtractionFlags);
        Assert.Equal(["Printer paper, 5 reams", "Stapler"], saved.LineItems.Select(l => l.Description));
        Assert.Equal([1, 2], saved.LineItems.Select(l => l.LineNumber));
        Assert.True(saved.CategorySuggestedByAi);
    }

    [Fact]
    public async Task SavingWithoutChanges_KeepsAisNotes()
    {
        var (client, receipt) = await ReceiptWaitingForReviewAsync();

        var saved = await PutAsync(client, receipt.Id, EditOf(receipt));

        Assert.Equal("PaymentMethod", Assert.Single(saved.ExtractionFlags).Field);
    }

    [Fact]
    public async Task Editing_WithSomeoneElsesCategory_IsRejected()
    {
        var (client, receipt) = await ReceiptWaitingForReviewAsync();
        var stranger = await SignedInClientAsync();
        var theirs = (await stranger.GetFromJsonAsync<List<CategoryDto>>("/api/categories", Json))!.First();

        var response = await client.PutAsJsonAsync($"/api/receipts/{receipt.Id}", EditOf(receipt) with { CategoryId = theirs.Id }, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("XYZ", 1)]
    [InlineData("EUR", 0)]
    public async Task Editing_WithImpossibleValues_IsRejected(string currency, decimal quantity)
    {
        var (client, receipt) = await ReceiptWaitingForReviewAsync();
        var edit = EditOf(receipt) with { Currency = currency, LineItems = [new LineItemRequest("Paper", quantity, 1m, 1m)] };

        var response = await client.PutAsJsonAsync($"/api/receipts/{receipt.Id}", edit, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Approving_ConvertsAtTheReceiptDatesRate_AndFreezesTheAmount()
    {
        var (client, receipt) = await ReceiptWaitingForReviewAsync();
        await PutAsync(client, receipt.Id, EditOf(receipt) with { Currency = "PKR", Subtotal = null, Tax = null, Total = 2800m });

        var approved = await PostAsync(client, receipt.Id, "approve", new ApproveReceiptRequest(null));
        var ledger = await client.GetFromJsonAsync<PagedResult<ReceiptSummaryDto>>("/api/receipts?status=Approved", Json);

        Assert.Equal(ReceiptStatus.Approved, approved.Status);
        Assert.Equal("EUR", approved.BaseCurrency);
        Assert.Equal(0.0032142857m, approved.ExchangeRate);
        Assert.Equal(receipt.ReceiptDate, approved.ExchangeRateDate);
        Assert.Equal(9.00m, approved.BaseCurrencyTotal);
        Assert.Null(approved.AmountCharged);
        Assert.Equal(9.00m, Assert.Single(ledger!.Items).BaseCurrencyTotal);
    }

    [Fact]
    public async Task Approving_WithTheAmountCharged_UsesItInsteadOfTheRate()
    {
        var (client, receipt) = await ReceiptWaitingForReviewAsync();

        var approved = await PostAsync(client, receipt.Id, "approve", new ApproveReceiptRequest(118.20m));

        Assert.Equal(118.20m, approved.AmountCharged);
        Assert.Equal(118.20m, approved.BaseCurrencyTotal);
    }

    [Fact]
    public async Task Approving_WithoutACategory_SaysWhatsMissing()
    {
        var (client, receipt) = await ReceiptWaitingForReviewAsync();
        await PutAsync(client, receipt.Id, EditOf(receipt) with { CategoryId = null });

        var response = await client.PostAsJsonAsync($"/api/receipts/{receipt.Id}/approve", new ApproveReceiptRequest(null), Json);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Add the category before approving.", problem!.Title);
    }

    [Fact]
    public async Task AnOldReceiptWithoutRates_CanBeApprovedWithTheAmountCharged()
    {
        var (client, receipt) = await ReceiptWaitingForReviewAsync();
        await PutAsync(client, receipt.Id, EditOf(receipt) with { ReceiptDate = new DateOnly(2019, 5, 1) });

        var withoutAmount = await client.PostAsJsonAsync($"/api/receipts/{receipt.Id}/approve", new ApproveReceiptRequest(null), Json);
        var problem = await withoutAmount.Content.ReadFromJsonAsync<ProblemDetails>(Json);
        var withAmount = await PostAsync(client, receipt.Id, "approve", new ApproveReceiptRequest(117m));

        Assert.Equal(HttpStatusCode.BadRequest, withoutAmount.StatusCode);
        Assert.Contains("Enter the amount charged", problem!.Title);
        Assert.Equal(117m, withAmount.BaseCurrencyTotal);
    }

    [Fact]
    public async Task AnApprovedReceipt_IsLocked_UntilReopened()
    {
        var (client, receipt) = await ReceiptWaitingForReviewAsync();
        await PostAsync(client, receipt.Id, "approve", new ApproveReceiptRequest(null));

        var editWhileApproved = await client.PutAsJsonAsync($"/api/receipts/{receipt.Id}", EditOf(receipt), Json);
        var reopened = await PostAsync(client, receipt.Id, "reopen");
        var editAfterReopening = await client.PutAsJsonAsync($"/api/receipts/{receipt.Id}", EditOf(receipt), Json);

        Assert.Equal(HttpStatusCode.Conflict, editWhileApproved.StatusCode);
        Assert.Equal(ReceiptStatus.NeedsReview, reopened.Status);
        Assert.Null(reopened.BaseCurrencyTotal);
        Assert.Null(reopened.ExchangeRate);
        Assert.Equal(HttpStatusCode.OK, editAfterReopening.StatusCode);
    }

    [Fact]
    public async Task ARejectedReceipt_CanBeReopened()
    {
        var (client, receipt) = await ReceiptWaitingForReviewAsync();

        var rejected = await PostAsync(client, receipt.Id, "reject");
        var reopened = await PostAsync(client, receipt.Id, "reopen");

        Assert.Equal(ReceiptStatus.Rejected, rejected.Status);
        Assert.Equal(ReceiptStatus.NeedsReview, reopened.Status);
    }

    [Fact]
    public async Task AReceiptAiCouldntRead_CanBeFilledInByHand_AndApproved()
    {
        var client = await SignedInClientAsync();
        var uploaded = await UploadAsync(client, TestFiles.NotAReceiptJpeg());
        var failed = await TestReceipts.WaitUntilReadAsync(client, uploaded.Id);
        var category = (await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", Json))!.First();

        var filledIn = await PutAsync(client, failed.Id, new UpdateReceiptRequest(
            "Corner Deli", new DateOnly(2026, 10, 20), "GBP", null, null, 7.5m, "CASH", category.Id, null, []));
        var approved = await PostAsync(client, failed.Id, "approve", new ApproveReceiptRequest(null));

        Assert.Equal(ReceiptStatus.Failed, failed.Status);
        Assert.Equal(ReceiptStatus.NeedsReview, filledIn.Status);
        Assert.Null(filledIn.FailureReason);
        Assert.Equal(9.00m, approved.BaseCurrencyTotal);
    }

    [Fact]
    public async Task SomeoneElsesReceipt_CantBeChangedOrDecided()
    {
        var (_, receipt) = await ReceiptWaitingForReviewAsync();
        var stranger = await SignedInClientAsync();

        HttpResponseMessage[] responses =
        [
            await stranger.PutAsJsonAsync($"/api/receipts/{receipt.Id}", EditOf(receipt), Json),
            await stranger.PostAsJsonAsync($"/api/receipts/{receipt.Id}/approve", new ApproveReceiptRequest(null), Json),
            await stranger.PostAsync($"/api/receipts/{receipt.Id}/reject", null),
            await stranger.PostAsync($"/api/receipts/{receipt.Id}/reopen", null),
        ];

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
    }

    [Fact]
    public async Task TheRateCanBeLookedUp_BeforeApproving()
    {
        var client = await SignedInClientAsync();

        var quote = await client.GetFromJsonAsync<ExchangeRateQuote>(
            "/api/exchange-rates?from=PKR&to=EUR&date=2026-10-15", Json);

        Assert.Equal(0.0032142857m, quote!.Rate);
        Assert.Equal(new DateOnly(2026, 10, 15), quote.RateDate);
    }

    private static UpdateReceiptRequest EditOf(ReceiptDetailDto r) => new(
        r.VendorName, r.ReceiptDate, r.Currency, r.Subtotal, r.Tax, r.Total, r.PaymentMethod, r.CategoryId, r.Notes,
        r.LineItems.Select(l => new LineItemRequest(l.Description, l.Quantity, l.UnitPrice, l.Amount)).ToList());

    private async Task<(HttpClient Client, ReceiptDetailDto Receipt)> ReceiptWaitingForReviewAsync()
    {
        var client = await SignedInClientAsync();
        var uploaded = await UploadAsync(client, TestFiles.Jpeg());
        var receipt = await TestReceipts.WaitUntilReadAsync(client, uploaded.Id);
        Assert.Equal(ReceiptStatus.NeedsReview, receipt.Status);
        return (client, receipt);
    }

    private static async Task<ReceiptDetailDto> PutAsync(HttpClient client, Guid id, UpdateReceiptRequest edit)
    {
        var response = await client.PutAsJsonAsync($"/api/receipts/{id}", edit, Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReceiptDetailDto>(Json))!;
    }

    private static async Task<ReceiptDetailDto> PostAsync(HttpClient client, Guid id, string action, object? body = null)
    {
        var response = body is null
            ? await client.PostAsync($"/api/receipts/{id}/{action}", null)
            : await client.PostAsJsonAsync($"/api/receipts/{id}/{action}", body, Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReceiptDetailDto>(Json))!;
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var auth = await TestAuth.RegisterAsync(client);
        return client.Authorize(auth.AccessToken);
    }

    private static async Task<ReceiptSummaryDto> UploadAsync(HttpClient client, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "files", "receipt.jpg");

        var response = await client.PostAsync("/api/receipts", form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<ReceiptSummaryDto>>(Json))!.Single();
    }
}
