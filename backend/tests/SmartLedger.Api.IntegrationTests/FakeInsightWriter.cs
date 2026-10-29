using SmartLedger.Application.Reports;

namespace SmartLedger.Api.IntegrationTests;

/// <summary>
/// Stands in for Gemini's monthly analysis in integration tests. It writes a predictable headline from the figures,
/// and remembers what it was sent so tests can check what the AI would see. Tests in the API collection run one at a
/// time, so the shared state is safe.
/// </summary>
internal sealed class FakeInsightWriter : IInsightWriter
{
    public static InsightRequest? LastRequest { get; private set; }

    /// <summary>Set by a test to make the next calls fail as if the AI service were down.</summary>
    public static bool Unavailable { get; set; }

    public Task<WrittenInsight> WriteAsync(InsightRequest request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        if (Unavailable)
        {
            throw new InsightUnavailableException("Down for the test.");
        }

        return Task.FromResult(new WrittenInsight(
            $"{request.MonthName}: {request.BaseCurrency} {request.Spent:0.00} across {request.ReceiptCount} receipts.",
            ["The first paragraph.", "The second paragraph."],
            "fake-model"));
    }
}
