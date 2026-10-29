using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartLedger.Application.Overview;
using SmartLedger.Application.Receipts;
using SmartLedger.Application.Reports;

namespace SmartLedger.Api.IntegrationTests;

/// <summary>
/// The month in review, AI's analysis (faked) and the CSV export. The fake AI reads every photo as a Paper &amp; Co.
/// Office Supplies receipt dated 19 Oct 2026; test accounts report in EUR; amounts are approved as "amount charged".
/// </summary>
[Collection(ApiCollection.Name)]
public class ReportsTests(SmartLedgerApiFactory factory)
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = TestReceipts.Json;

    [Fact]
    public async Task TheReport_HasTheMonthsFigures_TrendAndVendors()
    {
        var client = await SignedInClientAsync();
        await TestReceipts.ApprovedAsync(client, amountCharged: 100m);
        await TestReceipts.ApprovedAsync(client, amountCharged: 40m);
        await TestReceipts.ApprovedAsync(client, amountCharged: 50m, receiptDate: new DateOnly(2026, 9, 3));

        var report = await ReportAsync(client, "2026-10");

        Assert.Equal(140m, report.Spent);
        Assert.Equal(2, report.ApprovedCount);
        Assert.Equal(50m, report.PreviousMonthSpent);
        Assert.Equal(["2026-05", "2026-06", "2026-07", "2026-08", "2026-09", "2026-10"], report.Trend.Select(t => t.Month));
        Assert.Equal([0m, 0m, 0m, 0m, 50m, 140m], report.Trend.Select(t => t.Spent));

        var vendor = Assert.Single(report.TopVendors);
        Assert.Equal(("Paper & Co. Office Supply", "Office Supplies", 2, 140m), (vendor.Vendor, vendor.CategoryName, vendor.ReceiptCount, vendor.Spent));

        // Budget against actual lists every active category, spent or not.
        Assert.Equal(6, report.Categories.Count);
        Assert.Equal(140m, report.Categories[0].Spent);
        Assert.Null(report.Insight);
    }

    [Fact]
    public async Task TheAnalysis_IsWrittenFromTotalsOnly_KeptAndShownOnTheOverview()
    {
        var client = await SignedInClientAsync();
        var receipt = await TestReceipts.ApprovedAsync(client, amountCharged: 100m);

        var response = await client.PostAsync("/api/reports/insight?month=2026-10", null);
        var insight = await response.Content.ReadFromJsonAsync<InsightDto>(Json);
        var report = await ReportAsync(client, "2026-10");
        var overview = await client.GetFromJsonAsync<OverviewDto>("/api/overview?month=2026-10", Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("October 2026: EUR 100.00 across 1 receipts.", insight!.Headline);
        Assert.Equal(["The first paragraph.", "The second paragraph."], insight.Paragraphs);
        Assert.False(insight.IsOutOfDate);
        Assert.Equal(insight.Headline, report.Insight?.Headline);
        Assert.Equal(insight.Headline, overview!.InsightHeadline);

        // What the AI saw: totals and names, never the receipt's notes, card number or line items.
        var sent = FakeInsightWriter.LastRequest!;
        Assert.Equal("September 2026", sent.PreviousMonthName);
        Assert.Equal("Paper & Co. Office Supply", Assert.Single(sent.LargestReceipts).Vendor);
        var everythingSent = System.Text.Json.JsonSerializer.Serialize(sent);
        Assert.DoesNotContain(receipt.PaymentMethod!, everythingSent);
        Assert.DoesNotContain("Printer paper", everythingSent);
    }

    [Fact]
    public async Task TheAnalysis_SaysWhenApprovalsHaveChangedSince()
    {
        var client = await SignedInClientAsync();
        await TestReceipts.ApprovedAsync(client, amountCharged: 100m);
        (await client.PostAsync("/api/reports/insight?month=2026-10", null)).EnsureSuccessStatusCode();

        await TestReceipts.ApprovedAsync(client, amountCharged: 25m);
        var report = await ReportAsync(client, "2026-10");

        Assert.True(report.Insight!.IsOutOfDate);
    }

    [Fact]
    public async Task AMonthWithNothingApproved_HasNothingToAnalyse()
    {
        var client = await SignedInClientAsync();

        var response = await client.PostAsync("/api/reports/insight?month=2026-10", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WhenTheAiIsDown_TheAnalysisIsUnavailable_ForNow()
    {
        var client = await SignedInClientAsync();
        await TestReceipts.ApprovedAsync(client, amountCharged: 100m);

        FakeInsightWriter.Unavailable = true;
        try
        {
            var response = await client.PostAsync("/api/reports/insight?month=2026-10", null);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Null((await ReportAsync(client, "2026-10")).Insight);
        }
        finally
        {
            FakeInsightWriter.Unavailable = false;
        }
    }

    [Fact]
    public async Task Analyses_AreLimitedPerUser()
    {
        await using var limited = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:InsightPermitLimit", "1"));
        var client = limited.CreateClient();
        client.Authorize((await TestAuth.RegisterAsync(client)).AccessToken);
        await TestReceipts.ApprovedAsync(client, amountCharged: 10m);

        var first = await client.PostAsync("/api/reports/insight?month=2026-10", null);
        var second = await client.PostAsync("/api/reports/insight?month=2026-10", null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task TheExport_IsACsvOfTheMonthsApprovedReceipts_SafeToOpenInExcel()
    {
        var client = await SignedInClientAsync();
        var receipt = await TestReceipts.UploadAndReadAsync(client);
        // A vendor name that would run as a formula in a spreadsheet.
        var edit = new UpdateReceiptRequest("=HYPERLINK(\"http://evil.test\",\"Click\")", receipt.ReceiptDate, receipt.Currency,
            receipt.Subtotal, receipt.Tax, receipt.Total, receipt.PaymentMethod, receipt.CategoryId, "Team, offsite", []);
        (await client.PutAsJsonAsync($"/api/receipts/{receipt.Id}", edit, Json)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/receipts/{receipt.Id}/approve", new ApproveReceiptRequest(113.81m), Json)).EnsureSuccessStatusCode();
        await TestReceipts.ApprovedAsync(client, amountCharged: 50m, receiptDate: new DateOnly(2026, 9, 3)); // another month

        var response = await client.GetAsync("/api/reports/export?month=2026-10");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var lines = Encoding.UTF8.GetString(bytes).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("smartledger-2026-10.csv", response.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]); // UTF-8 marker, so Excel reads accents and "····" correctly
        Assert.Equal("Entry,Date,Vendor,Category,Currency,Total,Exchange rate,Total (EUR),Amount charged,Paid with,Notes", lines[0]);
        var row = Assert.Single(lines[1..]);
        Assert.StartsWith("0001,2026-10-19,\"'=HYPERLINK(\"\"http://evil.test\"\",\"\"Click\"\")\",Office Supplies,USD,126.45,", row);
        Assert.EndsWith(",113.81,113.81,VISA ····4242,\"Team, offsite\"", row);
    }

    [Fact]
    public async Task AMonthThatMakesNoSense_IsRejected()
    {
        var client = await SignedInClientAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/reports?month=13-2026")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/reports/export?month=2026-00")).StatusCode);
    }

    private static async Task<MonthReportDto> ReportAsync(HttpClient client, string month) =>
        (await client.GetFromJsonAsync<MonthReportDto>($"/api/reports?month={month}", Json))!;

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var auth = await TestAuth.RegisterAsync(client);
        return client.Authorize(auth.AccessToken);
    }
}
