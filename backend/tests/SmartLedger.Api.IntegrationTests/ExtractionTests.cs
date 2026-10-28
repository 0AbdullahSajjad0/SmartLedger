using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartLedger.Application.Categories;
using SmartLedger.Application.Receipts;
using SmartLedger.Domain.Enums;

namespace SmartLedger.Api.IntegrationTests;

/// <summary>Upload → background worker → AI (faked) → sanitizer → the receipt the user sees.</summary>
[Collection(ApiCollection.Name)]
public class ExtractionTests(SmartLedgerApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task AnUploadedReceipt_IsReadInTheBackground_AndWaitsForReview()
    {
        var client = await SignedInClientAsync();

        var uploaded = await UploadAsync(client, [0xFF, 0xD8, 0xFF, 0xE0, .. "a receipt"u8]);
        var receipt = await WaitUntilReadAsync(client, uploaded.Id);

        Assert.Equal(ReceiptStatus.NeedsReview, receipt.Status);
        Assert.Equal("Paper & Co. Office Supply", receipt.VendorName);
        Assert.Equal(new DateOnly(2026, 10, 19), receipt.ReceiptDate);
        Assert.Equal("USD", receipt.Currency);
        Assert.Equal(126.45m, receipt.Total);
        Assert.Equal(3, receipt.LineItems.Count);
        Assert.Equal("VISA ····4242", receipt.PaymentMethod); // the full card number never reaches the database
        Assert.Equal("PaymentMethod", Assert.Single(receipt.ExtractionFlags).Field);

        // Suggested category is one of the user's own default categories.
        var categories = await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", Json);
        Assert.Equal("Office Supplies", categories!.Single(c => c.Id == receipt.CategoryId).Name);
        Assert.True(receipt.CategorySuggestedByAi);
    }

    [Fact]
    public async Task SomethingThatIsntAReceipt_IsMarkedUnreadable_WithTheReason()
    {
        var client = await SignedInClientAsync();

        var uploaded = await UploadAsync(client, [0xFF, 0xD8, 0xFF, 0xE0, .. "NOT-A-RECEIPT"u8]);
        var receipt = await WaitUntilReadAsync(client, uploaded.Id);

        Assert.Equal(ReceiptStatus.Failed, receipt.Status);
        Assert.Equal("This is a photo of a cat, not a receipt.", receipt.FailureReason);
    }

    [Fact]
    public async Task Categories_AreTheSignedInUsersOwn()
    {
        var client = await SignedInClientAsync();

        var categories = await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", Json);

        Assert.Equal(6, categories!.Count);
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
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(content, "files", "receipt.jpg");

        var response = await client.PostAsync("/api/receipts", form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<ReceiptSummaryDto>>(Json))!.Single();
    }

    /// <summary>The worker runs in the background: poll until the receipt has left Processing.</summary>
    private static async Task<ReceiptDetailDto> WaitUntilReadAsync(HttpClient client, Guid id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var receipt = (await client.GetFromJsonAsync<ReceiptDetailDto>($"/api/receipts/{id}", Json))!;
            if (receipt.Status != ReceiptStatus.Processing || DateTime.UtcNow > deadline)
            {
                return receipt;
            }
            await Task.Delay(100);
        }
    }
}
