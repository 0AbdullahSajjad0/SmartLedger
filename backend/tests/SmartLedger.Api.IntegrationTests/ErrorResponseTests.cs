using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;

namespace SmartLedger.Api.IntegrationTests;

public class ErrorResponseTests(SmartLedgerApiFactory factory)
    : IClassFixture<SmartLedgerApiFactory>
{
    [Fact]
    public async Task UnknownRoute_ReturnsProblemDetails()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(404, problem.Status);
    }
}
