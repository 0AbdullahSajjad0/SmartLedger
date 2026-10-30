using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartLedger.Application.Account;
using SmartLedger.Application.Auth;
using SmartLedger.Application.Categories;
using SmartLedger.Application.Email;
using SmartLedger.Infrastructure.Persistence;

namespace SmartLedger.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public partial class EmailTests(SmartLedgerApiFactory factory)
{
    private const string NewPassword = "a brand new long passphrase";

    private static readonly System.Text.Json.JsonSerializerOptions Json = TestReceipts.Json;

    [Fact]
    public async Task ForgotPassword_ForAnUnknownAddress_Answers202_AndSendsNothing()
    {
        var email = TestAuth.NewEmail();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await Task.Delay(300);
        Assert.Empty(factory.Emails.SentTo(email));
    }

    [Fact]
    public async Task ResetLink_SetsANewPassword_SignsOutEverywhere_AndWorksOnce()
    {
        var auth = await TestAuth.RegisterAsync(factory.CreateClient());

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(auth.User.Email));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var email = await factory.Emails.WaitForAsync(auth.User.Email, FakeEmailSender.IsPasswordReset);
        Assert.Equal("Reset your SmartLedger password", email.Subject);
        var (userId, token) = ReadResetLink(email);
        Assert.Equal(auth.User.Id, userId);

        var reset = await ResetAsync(userId, token, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(auth.User.Email, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(auth.User.Email, TestAuth.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(auth.RefreshToken)).StatusCode);

        var again = await ResetAsync(userId, token, "yet another long passphrase");
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal("This link has expired or was already used. Ask for a new one.",
            (await again.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
    }

    [Fact]
    public async Task ResetLink_LiftsALockout()
    {
        var auth = await TestAuth.RegisterAsync(factory.CreateClient());
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await SignInAsync(auth.User.Email, "wrong guess " + attempt);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(auth.User.Email, TestAuth.Password)).StatusCode);

        await factory.CreateClient().PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(auth.User.Email));
        var (userId, token) = ReadResetLink(await factory.Emails.WaitForAsync(auth.User.Email, FakeEmailSender.IsPasswordReset));
        await ResetAsync(userId, token, NewPassword);

        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(auth.User.Email, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_AskedTwiceInAFewMinutes_SendsOneEmail()
    {
        var auth = await TestAuth.RegisterAsync(factory.CreateClient());
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(auth.User.Email));
        var second = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(auth.User.Email));

        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.OutgoingEmails.CountAsync(e => e.UserId == auth.User.Id && e.Kind == EmailKind.PasswordReset));
    }

    [Fact]
    public async Task ResetPassword_WithAMadeUpToken_Returns400()
    {
        var auth = await TestAuth.RegisterAsync(factory.CreateClient());

        var response = await ResetAsync(auth.User.Id, "bm90LWEtcmVhbC10b2tlbg", NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(auth.User.Email, TestAuth.Password)).StatusCode);
    }

    [Fact]
    public async Task ResetLink_AlsoConfirmsTheEmailAddress()
    {
        var auth = await TestAuth.RegisterAsync(factory.CreateClient());
        Assert.False(auth.User.EmailConfirmed);

        await factory.CreateClient().PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(auth.User.Email));
        var (userId, token) = ReadResetLink(await factory.Emails.WaitForAsync(auth.User.Email, FakeEmailSender.IsPasswordReset));
        await ResetAsync(userId, token, NewPassword);

        var signedIn = await (await SignInAsync(auth.User.Email, NewPassword)).Content.ReadFromJsonAsync<AuthResponse>();
        Assert.True(signedIn!.User.EmailConfirmed);
    }

    [Fact]
    public async Task NewAccounts_GetAConfirmationLink_ThatConfirmsTheAddress()
    {
        var (client, auth) = await SignedInAsync(confirmed: false);
        Assert.False(auth.User.EmailConfirmed);

        var email = await factory.Emails.WaitForAsync(auth.User.Email, FakeEmailSender.IsConfirmation);
        Assert.Contains("Thanks for opening a SmartLedger account, Test User.", email.PlainText);
        var (userId, token) = ReadConfirmLink(email);
        Assert.Equal(auth.User.Id, userId);

        var confirmed = await ConfirmAsync(userId, token);

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.True((await confirmed.Content.ReadFromJsonAsync<UserDto>())!.EmailConfirmed);
        Assert.True((await client.GetFromJsonAsync<UserDto>("/api/auth/me"))!.EmailConfirmed);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(userId, token)).StatusCode);
    }

    [Fact]
    public async Task ConfirmEmail_WithAMadeUpToken_Returns400()
    {
        var auth = await TestAuth.RegisterAsync(factory.CreateClient());

        var response = await ConfirmAsync(auth.User.Id, "bm90LWEtcmVhbC10b2tlbg");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("This link has expired or isn't complete. Send a new one from your Account page.",
            (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
    }

    [Fact]
    public async Task SendingTheLinkAgain_RightAfterSigningUp_SendsNothingMore()
    {
        var (client, auth) = await SignedInAsync(confirmed: false);

        var response = await client.PostAsync("/api/account/confirmation-email", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.OutgoingEmails.CountAsync(e => e.UserId == auth.User.Id && e.Kind == EmailKind.EmailConfirmation));
    }

    [Fact]
    public async Task SendingTheLinkAgain_IsNotAllowedWithoutSigningIn()
    {
        var response = await factory.CreateClient().PostAsync("/api/account/confirmation-email", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BudgetAlerts_GoOnlyToConfirmedAddresses()
    {
        var (client, auth) = await SignedInAsync(confirmed: false);
        await BudgetAsync(client, "Office Supplies", 50m);

        await TestReceipts.ApprovedAsync(client, 60m, Today());

        await Task.Delay(500);
        Assert.Empty(factory.Emails.SentTo(auth.User.Email, FakeEmailSender.IsBudgetAlert));
        Assert.Empty(await AlertsAsync(auth.User.Id));
    }

    [Fact]
    public async Task BudgetAlerts_WarnAt80Percent_ThenSayExceededAt100_OncePerLevel()
    {
        var (client, auth) = await SignedInAsync();
        var category = await BudgetAsync(client, "Snacks & <Treats>", 100m);
        var today = Today();

        await TestReceipts.ApprovedAsync(client, 85m, today, category.Id);
        var warning = await factory.Emails.WaitForAsync(auth.User.Email, FakeEmailSender.IsBudgetAlert);

        Assert.Equal($"Snacks & <Treats> has used 85% of its {Month(today)} budget", warning.Subject);
        Assert.Contains("You've spent EUR 85.00 of your EUR 100.00 budget for Snacks & <Treats>", warning.PlainText);
        Assert.Contains("That leaves EUR 15.00 for the rest of the month.", warning.PlainText);
        Assert.Contains("Snacks &amp; &lt;Treats&gt;", warning.Html);
        Assert.DoesNotContain("<Treats>", warning.Html);
        Assert.Contains($"{SmartLedgerApiFactory.AppUrl}/categories?month={today.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)}", warning.PlainText);

        await TestReceipts.ApprovedAsync(client, 10m, today, category.Id);
        await TestReceipts.ApprovedAsync(client, 10m, today, category.Id);
        var exceeded = await factory.Emails.WaitForAsync(auth.User.Email, FakeEmailSender.IsBudgetAlert, count: 2);
        Assert.Equal($"Snacks & <Treats> is over its {Month(today)} budget", exceeded.Subject);
        Assert.Contains("That's EUR 5.00 more than you planned.", exceeded.PlainText);

        await TestReceipts.ApprovedAsync(client, 10m, today, category.Id);
        await Task.Delay(500);
        Assert.Equal(2, factory.Emails.SentTo(auth.User.Email, FakeEmailSender.IsBudgetAlert).Count);
        Assert.Equal(category.Id, (await AlertsAsync(auth.User.Id)).Select(a => a.CategoryId).Distinct().Single());
    }

    [Fact]
    public async Task BudgetAlerts_GoingStraightPast100_SendsOnlyExceeded()
    {
        var (client, auth) = await SignedInAsync();
        await BudgetAsync(client, "Office Supplies", 50m);

        await TestReceipts.ApprovedAsync(client, 60m, Today());

        Assert.Contains("is over its", (await factory.Emails.WaitForAsync(auth.User.Email, FakeEmailSender.IsBudgetAlert)).Subject);
        await Task.Delay(300);
        Assert.Single(factory.Emails.SentTo(auth.User.Email, FakeEmailSender.IsBudgetAlert));
    }

    [Fact]
    public async Task BudgetAlerts_TurnedOff_SendNothing()
    {
        var (client, auth) = await SignedInAsync();
        await BudgetAsync(client, "Office Supplies", 50m);

        var off = await client.PutAsJsonAsync("/api/account/notifications", new UpdateNotificationsRequest(false));
        Assert.False((await off.Content.ReadFromJsonAsync<UserDto>())!.BudgetAlerts);

        await TestReceipts.ApprovedAsync(client, 60m, Today());

        await Task.Delay(500);
        Assert.Empty(factory.Emails.SentTo(auth.User.Email, FakeEmailSender.IsBudgetAlert));
        Assert.Empty(await AlertsAsync(auth.User.Id));
    }

    [Fact]
    public async Task BudgetAlerts_AreOnlyForTheCurrentMonth()
    {
        var (client, auth) = await SignedInAsync();
        await BudgetAsync(client, "Office Supplies", 50m);

        await TestReceipts.ApprovedAsync(client, 60m, Today().AddMonths(-1));

        await Task.Delay(500);
        Assert.Empty(factory.Emails.SentTo(auth.User.Email, FakeEmailSender.IsBudgetAlert));
    }

    [Fact]
    public async Task NewAccounts_HaveBudgetAlertsOn()
    {
        var auth = await TestAuth.RegisterAsync(factory.CreateClient());

        Assert.True(auth.User.BudgetAlerts);
    }

    [GeneratedRegex(@"reset-password\?user=(?<user>[0-9a-f-]{36})#token=(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex ResetLink();

    private static (Guid UserId, string Token) ReadResetLink(EmailMessage email)
    {
        var match = ResetLink().Match(email.PlainText);
        Assert.True(match.Success, "The email has no reset link.");
        Assert.Contains($"{SmartLedgerApiFactory.AppUrl}/reset-password?user=", email.PlainText);
        return (Guid.Parse(match.Groups["user"].Value), match.Groups["token"].Value);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static string Month(DateOnly date) => date.ToString("MMMM", System.Globalization.CultureInfo.InvariantCulture);

    [GeneratedRegex(@"confirm-email\?user=(?<user>[0-9a-f-]{36})#token=(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex ConfirmLink();

    private static (Guid UserId, string Token) ReadConfirmLink(EmailMessage email)
    {
        var match = ConfirmLink().Match(email.PlainText);
        Assert.True(match.Success, "The email has no confirmation link.");
        Assert.Contains($"{SmartLedgerApiFactory.AppUrl}/confirm-email?user=", email.PlainText);
        return (Guid.Parse(match.Groups["user"].Value), match.Groups["token"].Value);
    }

    private async Task<(HttpClient Client, AuthResponse Auth)> SignedInAsync(bool confirmed = true)
    {
        var client = factory.CreateClient();
        var auth = await TestAuth.RegisterAsync(client);
        client.Authorize(auth.AccessToken);

        if (confirmed)
        {
            var (userId, token) = ReadConfirmLink(await factory.Emails.WaitForAsync(auth.User.Email, FakeEmailSender.IsConfirmation));
            (await ConfirmAsync(userId, token)).EnsureSuccessStatusCode();
        }
        return (client, auth);
    }

    private Task<HttpResponseMessage> ConfirmAsync(Guid userId, string token) =>
        factory.CreateClient().PostAsJsonAsync("/api/auth/confirm-email", new ConfirmEmailRequest(userId, token));

    private static async Task<CategoryDto> BudgetAsync(HttpClient client, string name, decimal budget)
    {
        var categories = (await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", Json))!;
        var office = categories.Single(c => c.Name == "Office Supplies");
        var response = await client.PutAsJsonAsync($"/api/categories/{office.Id}",
            new UpdateCategoryRequest(name, office.Color, budget, IsArchived: false), Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CategoryDto>(Json))!;
    }

    private async Task<List<Domain.Entities.BudgetAlert>> AlertsAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.BudgetAlerts.Where(a => a.UserId == userId).ToListAsync();
    }

    private Task<HttpResponseMessage> ResetAsync(Guid userId, string token, string password) =>
        factory.CreateClient().PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(userId, token, password));

    private Task<HttpResponseMessage> SignInAsync(string email, string password) =>
        factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password, RememberMe: false));

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(refreshToken));
}
