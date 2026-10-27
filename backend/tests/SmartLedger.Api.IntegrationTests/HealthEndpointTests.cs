using System.Net;

namespace SmartLedger.Api.IntegrationTests;

public class HealthEndpointTests(SmartLedgerApiFactory factory)
    : IClassFixture<SmartLedgerApiFactory>
{
    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
