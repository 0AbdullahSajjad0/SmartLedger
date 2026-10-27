using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartLedger.Application.Receipts;
using SmartLedger.Domain.Enums;

namespace SmartLedger.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ReceiptTests(SmartLedgerApiFactory factory)
{
    // Smallest byte sequences each format starts with, padded so they look like real files.
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, .. new byte[64]];
    private static readonly byte[] Pdf = [.. "%PDF-1.7\n"u8, .. new byte[64]];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task Upload_CreatesOneProcessingReceiptPerFile_NumberedInOrder()
    {
        var client = await SignedInClientAsync();

        var response = await UploadAsync(client, ("receipt.jpg", Jpeg), ("invoice.pdf", Pdf));
        var receipts = (await response.Content.ReadFromJsonAsync<List<ReceiptSummaryDto>>(Json))!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([1, 2], receipts.Select(r => r.Number));
        Assert.All(receipts, r => Assert.Equal(ReceiptStatus.Processing, r.Status));
        Assert.Equal(["receipt.jpg", "invoice.pdf"], receipts.Select(r => r.FileName));
    }

    [Fact]
    public async Task Upload_ContinuesNumberingAcrossUploads()
    {
        var client = await SignedInClientAsync();
        await UploadAsync(client, ("first.jpg", Jpeg));

        var second = await (await UploadAsync(client, ("second.jpg", Jpeg))).Content.ReadFromJsonAsync<List<ReceiptSummaryDto>>(Json);

        Assert.Equal(2, Assert.Single(second!).Number);
    }

    [Fact]
    public async Task Upload_RejectsAFileThatOnlyPretendsToBeAnImage()
    {
        var client = await SignedInClientAsync();

        var response = await UploadAsync(client, ("receipt.jpg", "<script>alert(1)</script>"u8.ToArray()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("isn't a JPG, PNG, WebP, HEIC or PDF", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upload_WithoutFiles_Returns400()
    {
        var client = await SignedInClientAsync();

        var response = await client.PostAsync("/api/receipts", new MultipartFormDataContent());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_ShowsOnlyYourOwnReceipts_NewestFirst()
    {
        var mine = await SignedInClientAsync();
        var someoneElse = await SignedInClientAsync();
        await UploadAsync(mine, ("a.jpg", Jpeg), ("b.jpg", Jpeg));
        await UploadAsync(someoneElse, ("theirs.jpg", Jpeg));

        var page = await mine.GetFromJsonAsync<PagedResult<ReceiptSummaryDto>>("/api/receipts", Json);

        Assert.Equal(2, page!.TotalCount);
        Assert.Equal(["b.jpg", "a.jpg"], page.Items.Select(r => r.FileName));
    }

    [Fact]
    public async Task List_FiltersByStatus()
    {
        var client = await SignedInClientAsync();
        await UploadAsync(client, ("a.jpg", Jpeg));

        var processing = await client.GetFromJsonAsync<PagedResult<ReceiptSummaryDto>>("/api/receipts?status=Processing", Json);
        var approved = await client.GetFromJsonAsync<PagedResult<ReceiptSummaryDto>>("/api/receipts?status=Approved", Json);

        Assert.Equal(1, processing!.TotalCount);
        Assert.Equal(0, approved!.TotalCount);
    }

    [Fact]
    public async Task GetFile_ReturnsTheOriginalBytes()
    {
        var client = await SignedInClientAsync();
        var uploaded = (await (await UploadAsync(client, ("invoice.pdf", Pdf))).Content.ReadFromJsonAsync<List<ReceiptSummaryDto>>(Json))!.Single();

        var response = await client.GetAsync($"/api/receipts/{uploaded.Id}/file");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Pdf, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AnotherUsersReceipt_IsNotFound_NotForbidden()
    {
        // 404 rather than 403, so nobody can even confirm that a receipt id exists.
        var owner = await SignedInClientAsync();
        var stranger = await SignedInClientAsync();
        var receipt = (await (await UploadAsync(owner, ("a.jpg", Jpeg))).Content.ReadFromJsonAsync<List<ReceiptSummaryDto>>(Json))!.Single();

        var details = await stranger.GetAsync($"/api/receipts/{receipt.Id}");
        var file = await stranger.GetAsync($"/api/receipts/{receipt.Id}/file");

        Assert.Equal(HttpStatusCode.NotFound, details.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, file.StatusCode);
    }

    [Fact]
    public async Task Receipts_RequireSignIn()
    {
        var response = await factory.CreateClient().GetAsync("/api/receipts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var auth = await TestAuth.RegisterAsync(client);
        return client.Authorize(auth.AccessToken);
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, params (string Name, byte[] Bytes)[] files)
    {
        var form = new MultipartFormDataContent();
        foreach (var (name, bytes) in files)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(content, "files", name);
        }
        return client.PostAsync("/api/receipts", form);
    }
}
