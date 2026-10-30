using System.Globalization;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartLedger.Application.Categories;
using SmartLedger.Application.Overview;
using SmartLedger.Application.Reports;
using SmartLedger.Domain.Entities;
using SmartLedger.Infrastructure.Persistence;

namespace SmartLedger.Api.IntegrationTests;

/// <summary>
/// Budgets keep their history: a change applies from the current month on, and past months keep the budget they had.
/// Earlier budgets are written straight into the database, as edits through the API can only touch this month.
/// </summary>
[Collection(ApiCollection.Name)]
public class BudgetHistoryTests(SmartLedgerApiFactory factory)
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = TestReceipts.Json;

    public static string ThisMonth() => DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static string LastMonth() => DateTime.UtcNow.AddMonths(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture);

    [Fact]
    public async Task ChangingABudget_KeepsWhatEarlierMonthsHad()
    {
        var client = await SignedInClientAsync();
        var office = await CategoryAsync(client, "Office Supplies");
        await AddHistoryAsync(office.Id, monthsAgo: 3, 100m);

        var updated = await PutAsync(client, office, budget: 250m, archived: false);

        Assert.Equal(250m, updated.MonthlyBudget);
        Assert.Equal(250m, (await CategoryAsync(client, "Office Supplies")).MonthlyBudget);
        Assert.Equal(250m, Budget(await MonthAsync(client, ThisMonth()), office.Id));
        Assert.Equal(100m, Budget(await MonthAsync(client, LastMonth()), office.Id));
        Assert.Equal(100m, (await MonthAsync(client, LastMonth())).TotalBudget);
    }

    [Fact]
    public async Task ChangingItTwiceInAMonth_KeepsTheLatest_AndSettingItBack_LeavesNoEntry()
    {
        var client = await SignedInClientAsync();
        var office = await CategoryAsync(client, "Office Supplies");
        await AddHistoryAsync(office.Id, monthsAgo: 2, 100m);

        await PutAsync(client, office, budget: 150m, archived: false);
        await PutAsync(client, office, budget: 175m, archived: false);
        Assert.Equal(new decimal?[] { 100m, 175m }, await HistoryAsync(office.Id));

        await PutAsync(client, office, budget: 100m, archived: false);
        Assert.Equal(new decimal?[] { 100m }, await HistoryAsync(office.Id));
    }

    [Fact]
    public async Task Archiving_EndsTheBudgetFromThisMonth_ButEarlierMonthsStillCountIt()
    {
        var client = await SignedInClientAsync();
        var office = await CategoryAsync(client, "Office Supplies");
        await AddHistoryAsync(office.Id, monthsAgo: 1, 80m);

        var archived = await PutAsync(client, office, budget: 80m, archived: true);

        Assert.Null(archived.MonthlyBudget);
        var thisMonth = await MonthAsync(client, ThisMonth());
        Assert.Null(Budget(thisMonth, office.Id));
        Assert.Equal(0m, thisMonth.TotalBudget);
        Assert.Equal(80m, (await MonthAsync(client, LastMonth())).TotalBudget);
    }

    [Fact]
    public async Task ANewCategorysBudget_StartsThisMonth()
    {
        var client = await SignedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Marketing", "#2F5D62", 90m), Json);
        var created = (await response.Content.ReadFromJsonAsync<CategoryDto>(Json))!;

        Assert.Equal(90m, Budget(await MonthAsync(client, ThisMonth()), created.Id));
        Assert.Null(Budget(await MonthAsync(client, LastMonth()), created.Id));
    }

    [Fact]
    public async Task PastMonths_AreClosed_AndTheCurrentOneIsNot()
    {
        var client = await SignedInClientAsync();

        Assert.False((await MonthAsync(client, ThisMonth())).IsClosed);
        Assert.True((await MonthAsync(client, LastMonth())).IsClosed);
    }

    [Fact]
    public async Task OverviewAndReports_ShowTheBudgetsTheMonthHad()
    {
        var client = await SignedInClientAsync();
        var office = await CategoryAsync(client, "Office Supplies");
        await AddHistoryAsync(office.Id, monthsAgo: 1, 120m);
        await PutAsync(client, office, budget: 400m, archived: false);

        var overview = (await client.GetFromJsonAsync<OverviewDto>($"/api/overview?month={LastMonth()}", Json))!;
        var report = (await client.GetFromJsonAsync<MonthReportDto>($"/api/reports?month={LastMonth()}", Json))!;

        Assert.Equal(120m, overview.TotalBudget);
        Assert.Equal(120m, report.TotalBudget);
        Assert.Equal(120m, report.Categories.Single(c => c.Id == office.Id).MonthlyBudget);
        Assert.Equal(400m, (await client.GetFromJsonAsync<OverviewDto>("/api/overview", Json))!.TotalBudget);
    }

    private static decimal? Budget(CategoryMonthDto month, Guid categoryId) =>
        month.Categories.Single(c => c.Id == categoryId).MonthlyBudget;

    /// <summary>A budget that started <paramref name="monthsAgo"/> months ago, as if it had been set then.</summary>
    private async Task AddHistoryAsync(Guid categoryId, int monthsAgo, decimal amount)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var month = CategoryBudget.FirstOfMonth(DateOnly.FromDateTime(DateTime.UtcNow)).AddMonths(-monthsAgo);
        db.CategoryBudgets.Add(new CategoryBudget { CategoryId = categoryId, EffectiveFrom = month, Amount = amount });
        await db.SaveChangesAsync();
    }

    /// <summary>The category's budget entries, oldest first.</summary>
    private async Task<decimal?[]> HistoryAsync(Guid categoryId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.CategoryBudgets
            .Where(b => b.CategoryId == categoryId)
            .OrderBy(b => b.EffectiveFrom)
            .Select(b => b.Amount)
            .ToArrayAsync();
    }

    private static async Task<CategoryMonthDto> MonthAsync(HttpClient client, string month) =>
        (await client.GetFromJsonAsync<CategoryMonthDto>($"/api/categories/spending?month={month}", Json))!;

    private static async Task<CategoryDto> CategoryAsync(HttpClient client, string name) =>
        (await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", Json))!.Single(c => c.Name == name);

    private static async Task<CategoryDto> PutAsync(HttpClient client, CategoryDto category, decimal? budget, bool archived)
    {
        var response = await client.PutAsJsonAsync($"/api/categories/{category.Id}",
            new UpdateCategoryRequest(category.Name, category.Color, budget, archived), Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CategoryDto>(Json))!;
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var auth = await TestAuth.RegisterAsync(client);
        return client.Authorize(auth.AccessToken);
    }
}
