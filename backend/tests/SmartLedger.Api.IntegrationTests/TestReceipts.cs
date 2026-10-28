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

    /// <summary>
    /// The background worker reads uploads on its own schedule, so a receipt's status changes by itself shortly
    /// after upload. Tests that care about status wait for it to settle instead of racing the worker.
    /// </summary>
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
}
