using System.Net.Http.Headers;
using System.Net.Http.Json;
using SmartLedger.Application.Auth;

namespace SmartLedger.Api.IntegrationTests;

internal static class TestAuth
{
    public const string Password = "correct horse battery staple";

    public static string NewEmail() => $"user-{Guid.NewGuid():N}@test.local";

    public static async Task<AuthResponse> RegisterAsync(HttpClient client, string? email = null)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email ?? NewEmail(), Password, "Test User", "EUR"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    public static HttpClient Authorize(this HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}
