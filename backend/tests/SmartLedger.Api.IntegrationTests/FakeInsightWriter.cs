using SmartLedger.Application.Reports;

namespace SmartLedger.Api.IntegrationTests;

internal sealed class FakeInsightWriter : IInsightWriter
{
    public static InsightRequest? LastRequest { get; private set; }

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
