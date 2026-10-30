using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartLedger.Application.Receipts;
using SmartLedger.Domain.Enums;

namespace SmartLedger.Api.IntegrationTests;

internal static class TestReceipts
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<ReceiptDetailDto> WaitUntilReadAsync(HttpClient client, Guid id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var receipt = (await client.GetFromJsonAsync<ReceiptDetailDto>($"/api/receipts/{id}", Json))!;
            if (receipt.Status != ReceiptStatus.Processing || DateTime.UtcNow > deadline)
            {
                return receipt;
            }
            await Task.Delay(100);
        }
    }

    public static async Task<ReceiptDetailDto> UploadAndReadAsync(HttpClient client, byte[]? photo = null)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(photo ?? TestFiles.Jpeg());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "files", "receipt.jpg");

        var response = await client.PostAsync("/api/receipts", form);
        response.EnsureSuccessStatusCode();
        var uploaded = (await response.Content.ReadFromJsonAsync<List<ReceiptSummaryDto>>(Json))!.Single();
        return await WaitUntilReadAsync(client, uploaded.Id);
    }

    public static async Task<ReceiptDetailDto> ApprovedAsync(
        HttpClient client, decimal amountCharged, DateOnly? receiptDate = null, Guid? categoryId = null)
    {
        var receipt = await UploadAndReadAsync(client);
        if (receiptDate is not null || categoryId is not null)
        {
            var edit = new UpdateReceiptRequest(receipt.VendorName, receiptDate ?? receipt.ReceiptDate, receipt.Currency, receipt.Subtotal, receipt.Tax,
                receipt.Total, receipt.PaymentMethod, categoryId ?? receipt.CategoryId, receipt.Notes,
                receipt.LineItems.Select(l => new LineItemRequest(l.Description, l.Quantity, l.UnitPrice, l.Amount)).ToList());
            (await client.PutAsJsonAsync($"/api/receipts/{receipt.Id}", edit, Json)).EnsureSuccessStatusCode();
        }

        var approved = await client.PostAsJsonAsync($"/api/receipts/{receipt.Id}/approve", new ApproveReceiptRequest(amountCharged), Json);
        approved.EnsureSuccessStatusCode();
        return (await approved.Content.ReadFromJsonAsync<ReceiptDetailDto>(Json))!;
    }
}
