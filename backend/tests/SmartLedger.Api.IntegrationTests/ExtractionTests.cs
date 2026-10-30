using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImageMagick;
using SmartLedger.Application.Categories;
using SmartLedger.Application.Receipts;
using SmartLedger.Domain.Enums;

namespace SmartLedger.Api.IntegrationTests;

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

        var uploaded = await UploadAsync(client, "receipt.jpg", TestFiles.Jpeg());
        var receipt = await WaitUntilReadAsync(client, uploaded.Id);

        Assert.Equal(ReceiptStatus.NeedsReview, receipt.Status);
        Assert.Equal("Paper & Co. Office Supply", receipt.VendorName);
        Assert.Equal(new DateOnly(2026, 10, 19), receipt.ReceiptDate);
        Assert.Equal("USD", receipt.Currency);
        Assert.Equal(126.45m, receipt.Total);
        Assert.Equal(3, receipt.LineItems.Count);
        Assert.Equal("VISA ····4242", receipt.PaymentMethod);
        Assert.Equal("PaymentMethod", Assert.Single(receipt.ExtractionFlags).Field);

        var categories = await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", Json);
        Assert.Equal("Office Supplies", categories!.Single(c => c.Id == receipt.CategoryId).Name);
        Assert.True(receipt.CategorySuggestedByAi);
    }

    [Fact]
    public async Task SomethingThatIsntAReceipt_IsMarkedUnreadable_WithTheReason()
    {
        var client = await SignedInClientAsync();

        var uploaded = await UploadAsync(client, "cat.jpg", TestFiles.NotAReceiptJpeg());
        var receipt = await WaitUntilReadAsync(client, uploaded.Id);

        Assert.Equal(ReceiptStatus.Failed, receipt.Status);
        Assert.Equal("This is a photo of a cat, not a receipt.", receipt.FailureReason);
    }

    [Fact]
    public async Task APhonePhoto_GetsACleanUprightPreview_AndKeepsItsOriginalUntouched()
    {
        var client = await SignedInClientAsync();
        var heic = TestFiles.RotatedHeicWithGps();

        var uploaded = await UploadAsync(client, "IMG_0042.HEIC", heic);
        var receipt = await WaitUntilReadAsync(client, uploaded.Id);
        var preview = await client.GetAsync($"/api/receipts/{receipt.Id}/preview");
        var original = await client.GetAsync($"/api/receipts/{receipt.Id}/file");

        Assert.Equal(ReceiptStatus.NeedsReview, receipt.Status);
        Assert.True(receipt.HasPreview);

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("image/jpeg", preview.Content.Headers.ContentType?.MediaType);
        Assert.Equal("receipt-0001-preview.jpg", preview.Content.Headers.ContentDisposition?.FileName);
        Assert.Contains("sandbox", preview.Headers.GetValues("Content-Security-Policy").Single());
        using (var image = new MagickImage(await preview.Content.ReadAsByteArrayAsync()))
        {
            Assert.Equal((200u, 400u), (image.Width, image.Height));
            Assert.Empty(image.ProfileNames);
        }

        Assert.Equal("image/heic", original.Content.Headers.ContentType?.MediaType);
        Assert.Equal(heic, await original.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task APdf_GetsAPreviewOfItsFirstPage()
    {
        var client = await SignedInClientAsync();

        var uploaded = await UploadAsync(client, "invoice.pdf", TestFiles.Pdf());
        var receipt = await WaitUntilReadAsync(client, uploaded.Id);
        var preview = await client.GetByteArrayAsync($"/api/receipts/{receipt.Id}/preview");

        Assert.Equal(ReceiptStatus.NeedsReview, receipt.Status);
        Assert.Equal(MagickFormat.Jpeg, new MagickImageInfo(preview).Format);
    }

    [Fact]
    public async Task ADamagedPhoto_IsMarkedUnreadable_WithoutAPreview()
    {
        var client = await SignedInClientAsync();

        var uploaded = await UploadAsync(client, "broken.jpg", [0xFF, 0xD8, 0xFF, 0xE0, .. new byte[64]]);
        var receipt = await WaitUntilReadAsync(client, uploaded.Id);
        var preview = await client.GetAsync($"/api/receipts/{receipt.Id}/preview");

        Assert.Equal(ReceiptStatus.Failed, receipt.Status);
        Assert.Contains("couldn't be opened", receipt.FailureReason);
        Assert.False(receipt.HasPreview);
        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
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

    private static async Task<ReceiptSummaryDto> UploadAsync(HttpClient client, string fileName, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "files", fileName);

        var response = await client.PostAsync("/api/receipts", form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<ReceiptSummaryDto>>(Json))!.Single();
    }

    private static Task<ReceiptDetailDto> WaitUntilReadAsync(HttpClient client, Guid id) =>
        TestReceipts.WaitUntilReadAsync(client, id);
}
