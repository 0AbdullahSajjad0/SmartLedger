using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartLedger.Application.Reports;
using SmartLedger.Infrastructure.Extraction;
using SmartLedger.Infrastructure.Gemini;
using SmartLedger.Infrastructure.Reports;

namespace SmartLedger.Api.IntegrationTests;

public class GeminiInsightWriterTests
{
    private static readonly InsightRequest October = new(
        "October 2026", "USD", 1640m, 7, 1500m, "September 2026", 1200m,
        [new InsightCategory("Travel", 1640m, 1500m, 7)],
        [new VendorSpendingDto("Skyway Airlines", "Travel", 2, 1120m)],
        [new InsightReceipt("Skyway Airlines", new DateOnly(2026, 10, 21), "Travel", 482m)]);

    [Fact]
    public async Task SendsTheFiguresWithTheRules_AndAStrictShape()
    {
        var server = new FakeGemini(Reply("""{"headline":"Travel ran USD 140.00 over budget.","paragraphs":["One.","Two."]}"""));

        var written = await Writer(server).WriteAsync(October, CancellationToken.None);

        using var body = JsonDocument.Parse(server.LastBody!);
        var system = body.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString();
        var figures = body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString();
        var schema = body.RootElement.GetProperty("generationConfig").GetProperty("responseSchema");

        Assert.Contains("Never invent amounts", system);
        Assert.Contains("\"vendor\": \"Skyway Airlines\"", figures);
        Assert.Contains("\"name\": \"September 2026\"", figures);
        Assert.Contains("\"date\": \"21 October\"", figures);
        Assert.Equal(["headline", "paragraphs"], schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(4, schema.GetProperty("properties").GetProperty("paragraphs").GetProperty("maxItems").GetInt32());

        Assert.Equal("Travel ran USD 140.00 over budget.", written.Headline);
        Assert.Equal(["One.", "Two."], written.Paragraphs);
        Assert.Equal("test-model", written.Model);
    }

    [Fact]
    public void AnEmptyPreviousMonth_IsLeftOut_SoThereIsNothingToCompareWith()
    {
        var figures = GeminiInsightWriter.Figures(October with { PreviousMonthSpent = 0m });

        Assert.Contains("\"previousMonth\": null", figures);
        Assert.DoesNotContain("September", figures);
    }

    [Fact]
    public async Task MarkdownAndListMarkers_AreStripped_AndLongTextIsCut()
    {
        var longParagraph = string.Join(' ', Enumerable.Repeat("word", 400));
        var server = new FakeGemini(Reply(JsonSerializer.Serialize(new
        {
            headline = "## **Travel** ran over",
            paragraphs = new[] { "- First point", "1. `Second` point", "   ", longParagraph, "Five", "Six" },
        })));

        var written = await Writer(server).WriteAsync(October, CancellationToken.None);

        Assert.Equal("Travel ran over", written.Headline);
        Assert.Equal("First point", written.Paragraphs[0]);
        Assert.Equal("Second point", written.Paragraphs[1]);
        Assert.Equal(GeminiInsightWriter.MaxParagraphs, written.Paragraphs.Count);
        Assert.True(written.Paragraphs[2].Length <= GeminiInsightWriter.MaxParagraphLength + 1);
        Assert.EndsWith("…", written.Paragraphs[2]);
    }

    [Theory]
    [InlineData("""{"headline":"","paragraphs":["One."]}""")]
    [InlineData("""{"headline":"Something","paragraphs":[]}""")]
    [InlineData("not json")]
    public async Task AnAnswerWeCantUse_IsReportedAsUnavailable(string answer)
    {
        var server = new FakeGemini(Reply(answer));

        await Assert.ThrowsAsync<InsightUnavailableException>(() => Writer(server).WriteAsync(October, CancellationToken.None));
    }

    [Fact]
    public async Task AFailedCall_IsReportedAsUnavailable()
    {
        var server = new FakeGemini("""{"error":{"message":"quota"}}""", HttpStatusCode.TooManyRequests);

        var error = await Assert.ThrowsAsync<InsightUnavailableException>(() => Writer(server).WriteAsync(October, CancellationToken.None));

        Assert.Contains("free-tier limit", error.Message);
    }

    private static GeminiInsightWriter Writer(FakeGemini server) =>
        new(new GeminiApi(
            new HttpClient(server) { BaseAddress = new Uri("https://gemini.test/v1beta/") },
            Options.Create(new GeminiOptions { ApiKey = "test-key", Model = "test-model" }),
            NullLogger<GeminiApi>.Instance));

    private static string Reply(string json) =>
        JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = json } } } } } });

    private sealed class FakeGemini(string responseBody, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
        }
    }
}
