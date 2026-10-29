using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using SmartLedger.Application.Categories;
using SmartLedger.Application.Receipts;

namespace SmartLedger.Api.IntegrationTests;

/// <summary>
/// Categories, budgets and what was spent under them. The fake AI files every photo under Office Supplies, dated
/// 19 Oct 2026; test accounts report in EUR, and amounts are approved as "amount charged" so the sums are exact.
/// </summary>
[Collection(ApiCollection.Name)]
public class CategoriesTests(SmartLedgerApiFactory factory)
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = TestReceipts.Json;

    [Fact]
    public async Task Spending_SumsApprovedReceipts_ByReceiptDate_InTheBaseCurrency()
    {
        var client = await SignedInClientAsync();
        var october = await ApprovedReceiptAsync(client, amountCharged: 100m);
        await ApprovedReceiptAsync(client, amountCharged: 50m, receiptDate: new DateOnly(2026, 9, 5));
        await TestReceipts.UploadAndReadAsync(client); // waiting for review: not spent yet
        var office = await CategoryAsync(client, "Office Supplies");
        await PutAsync(client, office.Id, new UpdateCategoryRequest(office.Name, office.Color, 500m, false));

        var octoberMonth = await MonthAsync(client, "2026-10");
        var septemberMonth = await MonthAsync(client, "2026-09");

        var officeInOctober = octoberMonth.Categories.Single(c => c.Id == office.Id);
        Assert.Equal("2026-10", octoberMonth.Month);
        Assert.Equal("EUR", octoberMonth.BaseCurrency);
        Assert.Equal(1, officeInOctober.ReceiptCount);
        Assert.Equal(100m, officeInOctober.Spent);
        Assert.Equal(100m, octoberMonth.TotalSpent);
        Assert.Equal(500m, octoberMonth.TotalBudget);
        Assert.Equal(office.Id, octoberMonth.Categories[0].Id); // biggest spend first
        Assert.Equal(50m, septemberMonth.Categories.Single(c => c.Id == office.Id).Spent);
        Assert.Equal(october.CategoryId, office.Id);
    }

    [Fact]
    public async Task Spending_CountsOnlyYourOwnReceipts()
    {
        var mine = await SignedInClientAsync();
        var someoneElse = await SignedInClientAsync();
        await ApprovedReceiptAsync(someoneElse, amountCharged: 75m);

        var month = await MonthAsync(mine, "2026-10");

        Assert.Equal(0m, month.TotalSpent);
        Assert.Equal(6, month.Categories.Count); // the defaults every account starts with
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("1999-12")]
    [InlineData("October")]
    public async Task Spending_ForAMonthThatMakesNoSense_IsRejected(string month)
    {
        var client = await SignedInClientAsync();

        var response = await client.GetAsync($"/api/categories/spending?month={month}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_AddsACategory_TidyingNameColourAndBudget()
    {
        var client = await SignedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("  Marketing ", "#2f5d62", 250.555m), Json);
        var created = await response.Content.ReadFromJsonAsync<CategoryDto>(Json);
        var all = await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Marketing", created!.Name);
        Assert.Equal("#2F5D62", created.Color);
        Assert.Equal(250.56m, created.MonthlyBudget);
        Assert.Contains(all!, c => c.Id == created.Id);
    }

    [Fact]
    public async Task Create_WithANameYouAlreadyHave_InAnyCase_IsAConflict()
    {
        var client = await SignedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("travel", "#2F5D62", null), Json);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("You already have a category called \"Travel\".", problem!.Title);
    }

    [Fact]
    public async Task Create_WithAColourOutsideThePalette_IsRejected()
    {
        var client = await SignedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Marketing", "#FF00FF", null), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_RenamesAndArchives_AndTheReceiptsKeepTheirCategory()
    {
        var client = await SignedInClientAsync();
        var receipt = await ApprovedReceiptAsync(client, amountCharged: 20m);
        var office = await CategoryAsync(client, "Office Supplies");

        var updated = await PutAsync(client, office.Id, new UpdateCategoryRequest("Stationery", "#556B5D", 300m, IsArchived: true));
        var month = await MonthAsync(client, "2026-10");
        var stillOnReceipt = await client.GetFromJsonAsync<ReceiptDetailDto>($"/api/receipts/{receipt.Id}", Json);

        Assert.Equal("Stationery", updated.Name);
        Assert.True(updated.IsArchived);
        // Archived, but its spending this month still shows, and still counts.
        var row = month.Categories.Single(c => c.Id == office.Id);
        Assert.True(row.IsArchived);
        Assert.Equal(20m, row.Spent);
        Assert.Equal(20m, month.TotalSpent);
        Assert.Equal(office.Id, stillOnReceipt!.CategoryId);
    }

    [Fact]
    public async Task ANameTakenByAnArchivedCategory_SuggestsUnarchivingIt()
    {
        var client = await SignedInClientAsync();
        var travel = await CategoryAsync(client, "Travel");
        await PutAsync(client, travel.Id, new UpdateCategoryRequest(travel.Name, travel.Color, null, IsArchived: true));

        var response = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Travel", "#2F5D62", null), Json);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Unarchive it instead", problem!.Title);
    }

    [Fact]
    public async Task TheLastActiveCategory_CantBeArchived()
    {
        var client = await SignedInClientAsync();
        var categories = (await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", Json))!;
        foreach (var category in categories.Skip(1))
        {
            await PutAsync(client, category.Id, new UpdateCategoryRequest(category.Name, category.Color, null, IsArchived: true));
        }

        var last = categories[0];
        var response = await client.PutAsJsonAsync($"/api/categories/{last.Id}",
            new UpdateCategoryRequest(last.Name, last.Color, null, IsArchived: true), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SomeoneElsesCategory_IsNotFound()
    {
        var owner = await SignedInClientAsync();
        var stranger = await SignedInClientAsync();
        var theirs = await CategoryAsync(owner, "Travel");

        var response = await stranger.PutAsJsonAsync($"/api/categories/{theirs.Id}",
            new UpdateCategoryRequest("Mine now", theirs.Color, null, false), Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<CategoryMonthDto> MonthAsync(HttpClient client, string month) =>
        (await client.GetFromJsonAsync<CategoryMonthDto>($"/api/categories/spending?month={month}", Json))!;

    private static async Task<CategoryDto> CategoryAsync(HttpClient client, string name) =>
        (await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", Json))!.Single(c => c.Name == name);

    private static async Task<CategoryDto> PutAsync(HttpClient client, Guid id, UpdateCategoryRequest request)
    {
        var response = await client.PutAsJsonAsync($"/api/categories/{id}", request, Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CategoryDto>(Json))!;
    }

    private static Task<ReceiptDetailDto> ApprovedReceiptAsync(HttpClient client, decimal amountCharged, DateOnly? receiptDate = null) =>
        TestReceipts.ApprovedAsync(client, amountCharged, receiptDate);

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var auth = await TestAuth.RegisterAsync(client);
        return client.Authorize(auth.AccessToken);
    }
}
