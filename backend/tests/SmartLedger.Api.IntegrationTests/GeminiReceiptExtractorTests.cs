using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartLedger.Application.Receipts.Extraction;
using SmartLedger.Infrastructure.Extraction;

namespace SmartLedger.Api.IntegrationTests;

/// <summary>The Gemini client against a fake HTTP server: what it sends, and how it reads the answer.</summary>
public class GeminiReceiptExtractorTests
{
    private const string ReceiptJson = """
        {"isReceipt":true,"vendorName":"Lahore Chai House","receiptDate":"2026-10-15","currency":"PKR",
         "subtotal":4000,"tax":500,"total":4500,"paymentMethod":"Cash","category":"Food & Dining",
         "lineItems":[{"description":"Chai","quantity":2,"unitPrice":2000,"amount":4000}],
         "fieldsToCheck":[{"field":"Currency","reason":"Only \"Rs\" is printed."}]}
        """;

    [Fact]
    public async Task SendsTheImage_AndAStrictSchema_WithTheKeyInAHeader()
    {
        var server = new FakeGemini(HttpStatusCode.OK, Reply(ReceiptJson));

        await Extractor(server).ExtractAsync(Request(), CancellationToken.None);

        var request = server.LastRequest!.Value;
        Assert.Equal("https://gemini.test/v1beta/models/test-model:generateContent", request.Uri);
        Assert.Equal("test-key", request.ApiKey);
        Assert.DoesNotContain("test-key", request.Uri); // Never in the URL, where it would end up in logs.

        using var body = JsonDocument.Parse(request.Body);
        var parts = body.RootElement.GetProperty("contents")[0].GetProperty("parts");
        Assert.Equal("image/jpeg", parts[0].GetProperty("inlineData").GetProperty("mimeType").GetString());
        Assert.Equal(Convert.ToBase64String("fake image"u8.ToArray()), parts[0].GetProperty("inlineData").GetProperty("data").GetString());

        var config = body.RootElement.GetProperty("generationConfig");
        Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString());
        var categoryEnum = config.GetProperty("responseSchema").GetProperty("properties").GetProperty("category").GetProperty("enum");
        Assert.Equal(["Food & Dining", "Travel"], categoryEnum.EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task ReadsTheStructuredAnswer()
    {
        var server = new FakeGemini(HttpStatusCode.OK, Reply(ReceiptJson));

        var result = await Extractor(server).ExtractAsync(Request(), CancellationToken.None);

        Assert.True(result.IsReceipt);
        Assert.Equal("Lahore Chai House", result.VendorName);
        Assert.Equal("PKR", result.Currency);
        Assert.Equal(4500m, result.Total);
        Assert.Equal("Chai", Assert.Single(result.LineItems).Description);
        Assert.Equal("Currency", Assert.Single(result.FieldsToCheck).Field);
        Assert.Contains("Lahore Chai House", result.RawResponse);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AFailedCall_IsReportedAsUnavailable(HttpStatusCode status)
    {
        var server = new FakeGemini(status, """{"error":{"message":"nope"}}""");

        await Assert.ThrowsAsync<ReceiptExtractionUnavailableException>(
            () => Extractor(server).ExtractAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task WithoutAnApiKey_NothingIsSent()
    {
        var server = new FakeGemini(HttpStatusCode.OK, Reply(ReceiptJson));

        await Assert.ThrowsAsync<ReceiptExtractionUnavailableException>(
            () => Extractor(server, apiKey: "").ExtractAsync(Request(), CancellationToken.None));
        Assert.Null(server.LastRequest);
    }

    private static GeminiReceiptExtractor Extractor(FakeGemini server, string apiKey = "test-key") =>
        new(
            new HttpClient(server) { BaseAddress = new Uri("https://gemini.test/v1beta/") },
            Options.Create(new GeminiOptions { ApiKey = apiKey, Model = "test-model" }),
            NullLogger<GeminiReceiptExtractor>.Instance);

    private static ExtractionRequest Request() =>
        new(new MemoryStream("fake image"u8.ToArray()), "image/jpeg", ["Food & Dining", "Travel"], "USD");

    /// <summary>Gemini's envelope: our JSON arrives as text inside the first candidate.</summary>
    private static string Reply(string json) =>
        JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = json } } } } } });

    private sealed class FakeGemini(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public (string Uri, string? ApiKey, string Body)? LastRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = (
                request.RequestUri!.ToString(),
                request.Headers.TryGetValues("x-goog-api-key", out var keys) ? keys.Single() : null,
                await request.Content!.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
        }
    }
}
