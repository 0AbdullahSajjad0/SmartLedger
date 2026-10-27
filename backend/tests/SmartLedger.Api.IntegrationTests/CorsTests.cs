namespace SmartLedger.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class CorsTests(SmartLedgerApiFactory factory)
{
    [Fact]
    public async Task Preflight_FromAllowedOrigin_IsAllowed()
    {
        var response = await SendPreflightAsync("http://localhost:4200");

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.Equal("http://localhost:4200", Assert.Single(origins));
    }

    [Fact]
    public async Task Preflight_FromUnknownOrigin_IsNotAllowed()
    {
        var response = await SendPreflightAsync("https://evil.example.com");

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private async Task<HttpResponseMessage> SendPreflightAsync(string origin)
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        return await client.SendAsync(request);
    }
}
