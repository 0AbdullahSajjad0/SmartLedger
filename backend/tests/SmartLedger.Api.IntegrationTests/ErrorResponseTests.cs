using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;

namespace SmartLedger.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ErrorResponseTests(SmartLedgerApiFactory factory)
{
    [Fact]
    public async Task UnknownRoute_WhenSignedIn_ReturnsProblemDetails404()
    {
        var client = factory.CreateClient();
        var auth = await TestAuth.RegisterAsync(client);

        var response = await client.Authorize(auth.AccessToken).GetAsync("/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(404, problem!.Status);
    }

    [Fact]
    public async Task AnyRoute_WhenAnonymous_ReturnsProblemDetails401()
    {
        var response = await factory.CreateClient().GetAsync("/does-not-exist");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
