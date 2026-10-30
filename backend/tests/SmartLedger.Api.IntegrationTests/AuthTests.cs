using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartLedger.Application.Auth;
using SmartLedger.Infrastructure.Persistence;

namespace SmartLedger.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthTests(SmartLedgerApiFactory factory)
{
    [Fact]
    public async Task Register_ReturnsTokens_AndCreatesDefaultCategories()
    {
        var auth = await TestAuth.RegisterAsync(factory.CreateClient());

        Assert.False(string.IsNullOrEmpty(auth.AccessToken));
        Assert.False(string.IsNullOrEmpty(auth.RefreshToken));
        Assert.Equal("EUR", auth.User.BaseCurrency);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(6, await db.Categories.CountAsync(c => c.UserId == auth.User.Id));
    }

    [Fact]
    public async Task Register_WithExistingEmail_Returns409()
    {
        var client = factory.CreateClient();
        var email = TestAuth.NewEmail();
        await TestAuth.RegisterAsync(client, email);

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, TestAuth.Password, "Someone Else", "USD"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithShortPassword_Returns400()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(TestAuth.NewEmail(), "short", "Test User", "USD"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithAccessToken_ReturnsTheUser()
    {
        var client = factory.CreateClient();
        var auth = await TestAuth.RegisterAsync(client);

        var user = await client.Authorize(auth.AccessToken).GetFromJsonAsync<UserDto>("/api/auth/me");

        Assert.Equal(auth.User.Id, user!.Id);
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPasswordAndUnknownEmail_GiveTheSameAnswer()
    {
        var client = factory.CreateClient();
        var email = TestAuth.NewEmail();
        await TestAuth.RegisterAsync(client, email);

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "not the password", false));
        var unknownEmail = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(TestAuth.NewEmail(), "not the password", false));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(
            (await wrongPassword.Content.ReadFromJsonAsync<ProblemDetails>())!.Title,
            (await unknownEmail.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
    }

    [Fact]
    public async Task Login_AfterFiveWrongPasswords_LocksTheAccount()
    {
        var client = factory.CreateClient();
        var email = TestAuth.NewEmail();
        await TestAuth.RegisterAsync(client, email);

        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "not the password", false));
        }

        var correctPassword = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, TestAuth.Password, false));
        var problem = await correctPassword.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.Unauthorized, correctPassword.StatusCode);
        Assert.Equal("auth.locked_out", problem!.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task Login_WithRememberMe_KeepsTheSessionLonger()
    {
        var client = factory.CreateClient();
        var email = TestAuth.NewEmail();
        await TestAuth.RegisterAsync(client, email);

        var normal = await LoginAsync(client, email, rememberMe: false);
        var remembered = await LoginAsync(client, email, rememberMe: true);

        var now = DateTimeOffset.UtcNow;
        Assert.InRange(normal.RefreshTokenExpiresAt - now, TimeSpan.FromDays(6.9), TimeSpan.FromDays(7.1));
        Assert.InRange(remembered.RefreshTokenExpiresAt - now, TimeSpan.FromDays(29.9), TimeSpan.FromDays(30.1));
    }

    [Fact]
    public async Task Refresh_IssuesNewTokens_AndTheOldOneStopsWorking()
    {
        var client = factory.CreateClient();
        var first = await TestAuth.RegisterAsync(client);

        var second = await RefreshAsync(client, first.RefreshToken);
        var reused = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(first.RefreshToken));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
    }

    [Fact]
    public async Task Refresh_ReusingAnOldToken_EndsTheWholeSession()
    {
        var client = factory.CreateClient();
        var first = await TestAuth.RegisterAsync(client);
        var second = (await (await RefreshAsync(client, first.RefreshToken)).Content.ReadFromJsonAsync<AuthResponse>())!;

        await RefreshAsync(client, first.RefreshToken);

        var newest = await RefreshAsync(client, second.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, newest.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesTheRefreshToken()
    {
        var client = factory.CreateClient();
        var auth = await TestAuth.RegisterAsync(client);

        var logout = await client.PostAsJsonAsync("/api/auth/logout", new RefreshRequest(auth.RefreshToken));
        var refresh = await RefreshAsync(client, auth.RefreshToken);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    private static async Task<AuthResponse> LoginAsync(HttpClient client, string email, bool rememberMe)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, TestAuth.Password, rememberMe));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(refreshToken));
}
