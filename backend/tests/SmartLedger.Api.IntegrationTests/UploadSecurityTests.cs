using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using SmartLedger.Application.Receipts;

namespace SmartLedger.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class UploadSecurityTests(SmartLedgerApiFactory factory)
{
    private static readonly byte[] Jpeg = TestFiles.Jpeg();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task AFileOver10MB_IsRejectedWith413_AndNothingIsStored()
    {
        var client = await SignedInClientAsync(factory);
        byte[] oversized = [0xFF, 0xD8, 0xFF, 0xE0, .. new byte[(10 * 1024 * 1024) + 1]];

        var response = await UploadAsync(client, ("huge.jpg", oversized));
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        var list = await client.GetFromJsonAsync<PagedResult<ReceiptSummaryDto>>("/api/receipts", Json);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("10 MB", problem!.Title);
        Assert.Equal(0, list!.TotalCount);
    }

    [Fact]
    public async Task DownloadedFiles_AreSandboxed_AndNamedByEntryNumber()
    {
        var client = await SignedInClientAsync(factory);
        var receipt = (await (await UploadAsync(client, ("<script>.jpg", Jpeg))).Content
            .ReadFromJsonAsync<List<ReceiptSummaryDto>>(Json))!.Single();

        var response = await client.GetAsync($"/api/receipts/{receipt.Id}/file");

        Assert.Contains("sandbox", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("receipt-0001.jpg", response.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal("inline", response.Content.Headers.ContentDisposition?.DispositionType);
    }

    [Fact]
    public async Task EveryResponse_CarriesTheHardeningHeaders()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task Uploads_AreRateLimitedPerUser()
    {
        await using var limited = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:UploadPermitLimit", "2"));
        var client = await SignedInClientAsync(limited);

        var first = await UploadAsync(client, ("1.jpg", Jpeg));
        var second = await UploadAsync(client, ("2.jpg", Jpeg));
        var third = await UploadAsync(client, ("3.jpg", Jpeg));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    private static async Task<HttpClient> SignedInClientAsync<TFactory>(TFactory apiFactory)
        where TFactory : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
    {
        var client = apiFactory.CreateClient();
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
