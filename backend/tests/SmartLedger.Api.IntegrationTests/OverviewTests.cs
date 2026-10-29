using System.Net;
using System.Net.Http.Json;
using SmartLedger.Application.Categories;
using SmartLedger.Application.Overview;
using SmartLedger.Domain.Enums;

namespace SmartLedger.Api.IntegrationTests;

/// <summary>
/// The monthly statement on the Overview page. The fake AI files every photo under Office Supplies, dated
/// 19 Oct 2026; test accounts report in EUR, and amounts are approved as "amount charged" so the sums are exact.
/// </summary>
[Collection(ApiCollection.Name)]
public class OverviewTests(SmartLedgerApiFactory factory)
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = TestReceipts.Json;

    [Fact]
    public async Task ANewAccount_HasAnEmptyStatementForThisMonth()
    {
        var client = await SignedInClientAsync();

        var overview = await client.GetFromJsonAsync<OverviewDto>("/api/overview", Json);

        Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM"), overview!.Month);
        Assert.Equal("EUR", overview.BaseCurrency);
        Assert.Equal(0, overview.TotalReceipts);
        Assert.Equal(0m, overview.Spent);
        Assert.Empty(overview.Categories);
        Assert.Empty(overview.Recent);
    }

    [Fact]
    public async Task TheStatement_AddsUpTheMonth_ComparesItWithTheLast_AndCountsWhatsWaiting()
    {
        var client = await SignedInClientAsync();
        await TestReceipts.ApprovedAsync(client, amountCharged: 100m);
        await TestReceipts.ApprovedAsync(client, amountCharged: 40m);
        await TestReceipts.ApprovedAsync(client, amountCharged: 50m, receiptDate: new DateOnly(2026, 9, 12));
        await TestReceipts.UploadAndReadAsync(client); // waiting for review
        var unreadable = await TestReceipts.UploadAndReadAsync(client, TestFiles.NotAReceiptJpeg());
        var travel = (await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", Json))!.Single(c => c.Name == "Travel");
        (await client.PutAsJsonAsync($"/api/categories/{travel.Id}",
            new UpdateCategoryRequest(travel.Name, travel.Color, 300m, false), Json)).EnsureSuccessStatusCode();

        var overview = (await client.GetFromJsonAsync<OverviewDto>("/api/overview?month=2026-10", Json))!;

        Assert.Equal("2026-10", overview.Month);
        Assert.Equal(140m, overview.Spent);
        Assert.Equal(2, overview.ApprovedCount);
        Assert.Equal(300m, overview.TotalBudget); // budgets count even for categories with nothing spent
        Assert.Equal(50m, overview.PreviousMonthSpent);
        Assert.Equal(1, overview.AwaitingReview);
        Assert.Equal(1, overview.Unreadable);
        Assert.Equal(5, overview.TotalReceipts);

        var office = Assert.Single(overview.Categories);
        Assert.Equal("Office Supplies", office.Name);
        Assert.Equal(140m, office.Spent);
        Assert.Equal(2, office.ReceiptCount);

        // Newest first, every status.
        Assert.Equal([5, 4, 3, 2, 1], overview.Recent.Select(r => r.Number));
        Assert.Equal(unreadable.Id, overview.Recent[0].Id);
        Assert.Equal(ReceiptStatus.Failed, overview.Recent[0].Status);
    }

    [Fact]
    public async Task TheStatement_IsOnlyYourOwn()
    {
        var someoneElse = await SignedInClientAsync();
        await TestReceipts.ApprovedAsync(someoneElse, amountCharged: 75m);
        var mine = await SignedInClientAsync();

        var overview = (await mine.GetFromJsonAsync<OverviewDto>("/api/overview?month=2026-10", Json))!;

        Assert.Equal(0m, overview.Spent);
        Assert.Equal(0, overview.TotalReceipts);
    }

    [Fact]
    public async Task AMonthThatMakesNoSense_IsRejected()
    {
        var client = await SignedInClientAsync();

        var response = await client.GetAsync("/api/overview?month=2026-13");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var auth = await TestAuth.RegisterAsync(client);
        return client.Authorize(auth.AccessToken);
    }
}
