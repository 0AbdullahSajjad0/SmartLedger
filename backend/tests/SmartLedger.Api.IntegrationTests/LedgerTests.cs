using System.Net;
using System.Net.Http.Json;
using SmartLedger.Application.Categories;
using SmartLedger.Application.Overview;
using SmartLedger.Application.Receipts;

namespace SmartLedger.Api.IntegrationTests;

/// <summary>
/// Narrowing the ledger (search, category, dates) and deleting receipts. The fake AI reads every photo as a
/// Paper &amp; Co. Office Supplies receipt dated 19 Oct 2026; tests rename and redate them through the review API.
/// </summary>
[Collection(ApiCollection.Name)]
public class LedgerTests(SmartLedgerApiFactory factory)
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = TestReceipts.Json;

    [Fact]
    public async Task Search_FindsVendorsByAnyPartOfTheName_InAnyCase()
    {
        var client = await SignedInClientAsync();
        await ReceiptAsync(client, "Lahore Chai House");
        await ReceiptAsync(client, "Paper & Co.");

        var found = await ListAsync(client, "search=CHAI");

        Assert.Equal(["Lahore Chai House"], found.Items.Select(r => r.VendorName));
    }

    [Theory]
    [InlineData("%", "100% Juice")]
    [InlineData("_", "Juice_Bar")]
    [InlineData("[", "[Brackets] Ltd")]
    public async Task Search_TreatsWildcardCharactersLiterally(string search, string expected)
    {
        var client = await SignedInClientAsync();
        foreach (var vendor in new[] { "100% Juice", "Juice_Bar", "[Brackets] Ltd", "Plain Vendor" })
        {
            await ReceiptAsync(client, vendor);
        }

        var found = await ListAsync(client, $"search={Uri.EscapeDataString(search)}");

        Assert.Equal([expected], found.Items.Select(r => r.VendorName));
    }

    [Fact]
    public async Task Search_FindsUnreadReceiptsByFileName()
    {
        var client = await SignedInClientAsync();
        var unread = await TestReceipts.UploadAndReadAsync(client, TestFiles.NotAReceiptJpeg()); // uploaded as receipt.jpg
        await ReceiptAsync(client, "Somewhere Else");

        var found = await ListAsync(client, "search=receipt.jp");

        Assert.Equal([unread.Id], found.Items.Select(r => r.Id));
    }

    [Fact]
    public async Task TheLedger_FiltersByCategory_AndByReceiptDate()
    {
        var client = await SignedInClientAsync();
        var travel = (await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", Json))!.Single(c => c.Name == "Travel");
        var flight = await ReceiptAsync(client, "Skyway Airlines", new DateOnly(2026, 9, 2), travel.Id);
        var paper = await ReceiptAsync(client, "Paper & Co.", new DateOnly(2026, 10, 5));
        await ReceiptAsync(client, "Old Shop", new DateOnly(2026, 8, 20));

        var byCategory = await ListAsync(client, $"categoryId={travel.Id}");
        var byDates = await ListAsync(client, "from=2026-09-01&to=2026-10-31");
        var backToFront = await ListAsync(client, "from=2026-10-31&to=2026-09-01");

        Assert.Equal([flight.Id], byCategory.Items.Select(r => r.Id));
        Assert.Equal([paper.Id, flight.Id], byDates.Items.Select(r => r.Id)); // newest entry first
        Assert.Equal(byDates.Items.Select(r => r.Id), backToFront.Items.Select(r => r.Id));
    }

    [Fact]
    public async Task Deleting_RemovesTheReceipt_ItsFiles_AndItsSpending()
    {
        var client = await SignedInClientAsync();
        var approved = await TestReceipts.ApprovedAsync(client, amountCharged: 80m);
        var files = Directory.GetFiles(factory.StoragePath, $"{approved.Id}*", SearchOption.AllDirectories);
        Assert.Equal(2, files.Length); // the original and its preview

        var response = await client.DeleteAsync($"/api/receipts/{approved.Id}");
        var afterwards = await client.GetAsync($"/api/receipts/{approved.Id}");
        var overview = await client.GetFromJsonAsync<OverviewDto>("/api/overview?month=2026-10", Json);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, afterwards.StatusCode);
        Assert.All(files, file => Assert.False(File.Exists(file)));
        Assert.Equal(0m, overview!.Spent);
        Assert.Equal(0, overview.TotalReceipts);
    }

    [Fact]
    public async Task SomeoneElsesReceipt_CantBeDeleted()
    {
        var owner = await SignedInClientAsync();
        var receipt = await TestReceipts.UploadAndReadAsync(owner);
        var stranger = await SignedInClientAsync();

        var response = await stranger.DeleteAsync($"/api/receipts/{receipt.Id}");
        var stillThere = await owner.GetAsync($"/api/receipts/{receipt.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
    }

    private static async Task<PagedResult<ReceiptSummaryDto>> ListAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<PagedResult<ReceiptSummaryDto>>($"/api/receipts?{query}", Json))!;

    /// <summary>A receipt read by the fake AI, then given this vendor (and optionally date and category).</summary>
    private static async Task<ReceiptDetailDto> ReceiptAsync(HttpClient client, string vendor, DateOnly? date = null, Guid? categoryId = null)
    {
        var receipt = await TestReceipts.UploadAndReadAsync(client);
        var edit = new UpdateReceiptRequest(vendor, date ?? receipt.ReceiptDate, receipt.Currency, receipt.Subtotal, receipt.Tax,
            receipt.Total, receipt.PaymentMethod, categoryId ?? receipt.CategoryId, receipt.Notes, []);
        var response = await client.PutAsJsonAsync($"/api/receipts/{receipt.Id}", edit, Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReceiptDetailDto>(Json))!;
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var auth = await TestAuth.RegisterAsync(client);
        return client.Authorize(auth.AccessToken);
    }
}
