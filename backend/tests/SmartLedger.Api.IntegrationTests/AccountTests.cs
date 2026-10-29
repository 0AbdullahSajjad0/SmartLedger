using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartLedger.Application.Account;
using SmartLedger.Application.Auth;
using SmartLedger.Application.Receipts;
using SmartLedger.Infrastructure.Persistence;

namespace SmartLedger.Api.IntegrationTests;

/// <summary>The Account page: name, password, other devices, export and deleting the account.</summary>
[Collection(ApiCollection.Name)]
public class AccountTests(SmartLedgerApiFactory factory)
{
    private const string NewPassword = "a different long passphrase";

    [Fact]
    public async Task UpdateProfile_ChangesTheName()
    {
        var (client, _) = await SignedInAsync();

        var response = await client.PutAsJsonAsync("/api/account", new UpdateProfileRequest("  Ayesha Khan  "));
        var me = await client.GetFromJsonAsync<UserDto>("/api/auth/me");

        Assert.Equal("Ayesha Khan", (await response.Content.ReadFromJsonAsync<UserDto>())!.DisplayName);
        Assert.Equal("Ayesha Khan", me!.DisplayName);
    }

    [Fact]
    public async Task UpdateProfile_RefusesABlankName()
    {
        var (client, _) = await SignedInAsync();

        var response = await client.PutAsJsonAsync("/api/account", new UpdateProfileRequest("   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithTheWrongCurrentPassword_Returns400_NotASignOut()
    {
        var (client, auth) = await SignedInAsync();

        var response = await client.PostAsJsonAsync("/api/account/password",
            new ChangePasswordRequest("not my password", NewPassword, auth.RefreshToken));

        // 400 with a message for the form; a 401 would make the app sign the user out.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("That password isn't right.", (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(auth.User.Email, TestAuth.Password)).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_SignsOutOtherDevices_AndKeepsThisOne()
    {
        var (client, thisDevice) = await SignedInAsync();
        var otherDevice = await (await SignInAsync(thisDevice.User.Email, TestAuth.Password)).Content.ReadFromJsonAsync<AuthResponse>();

        var response = await client.PostAsJsonAsync("/api/account/password",
            new ChangePasswordRequest(TestAuth.Password, NewPassword, thisDevice.RefreshToken));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(thisDevice.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(otherDevice!.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(thisDevice.User.Email, TestAuth.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(thisDevice.User.Email, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_ToTheSamePassword_Returns400()
    {
        var (client, auth) = await SignedInAsync();

        var response = await client.PostAsJsonAsync("/api/account/password",
            new ChangePasswordRequest(TestAuth.Password, TestAuth.Password, auth.RefreshToken));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SignOutOthers_KeepsThisDevice_EvenWithAnAlreadyExchangedToken()
    {
        var (client, first) = await SignedInAsync();
        var otherDevice = await (await SignInAsync(first.User.Email, TestAuth.Password)).Content.ReadFromJsonAsync<AuthResponse>();
        // Another tab may have just refreshed: the token the page sends is the one before.
        var latest = await (await RefreshAsync(first.RefreshToken)).Content.ReadFromJsonAsync<AuthResponse>();

        var response = await client.PostAsJsonAsync("/api/account/sign-out-others", new SignOutOthersRequest(first.RefreshToken));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(latest!.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(otherDevice!.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task WrongPasswords_CountTowardsTheSignInLockout()
    {
        var (client, auth) = await SignedInAsync();

        ProblemDetails? last = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/account/delete", new DeleteAccountRequest("guessing " + attempt));
            last = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        }

        Assert.Equal("Too many failed attempts. Try again in 15 minutes.", last!.Title);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(auth.User.Email, TestAuth.Password)).StatusCode);
    }

    [Fact]
    public async Task Export_IsAZipOfTheDataAndTheOriginalFiles()
    {
        var (client, auth) = await SignedInAsync();
        var photo = TestFiles.Jpeg();
        var waiting = await TestReceipts.UploadAndReadAsync(client, photo);
        var approved = await TestReceipts.ApprovedAsync(client, 42.50m);

        var response = await client.GetAsync("/api/account/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        Assert.StartsWith("smartledger-export-", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));

        using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);
        Assert.Equal(
            ["README.txt", "account.json", "files/receipt-0001.jpg", "files/receipt-0002.jpg", "line-items.csv", "receipts.csv"],
            zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
        Assert.Equal(photo, await ReadBytesAsync(zip, "files/receipt-0001.jpg"));

        using var json = JsonDocument.Parse(await ReadBytesAsync(zip, "account.json"));
        var root = json.RootElement;
        Assert.Equal(auth.User.Email, root.GetProperty("account").GetProperty("email").GetString());
        Assert.Equal(6, root.GetProperty("categories").GetArrayLength());

        var receipts = root.GetProperty("receipts").EnumerateArray().ToList();
        Assert.Equal([waiting.Id, approved.Id], receipts.Select(r => r.GetProperty("id").GetGuid()));
        Assert.Equal("NeedsReview", receipts[0].GetProperty("status").GetString());
        Assert.Equal("files/receipt-0001.jpg", receipts[0].GetProperty("file").GetString());
        Assert.Equal("receipt.jpg", receipts[0].GetProperty("originalFileName").GetString());
        Assert.Equal(42.50m, receipts[1].GetProperty("baseCurrencyTotal").GetDecimal());
        Assert.True(receipts[1].GetProperty("lineItems").GetArrayLength() > 0);

        var csv = System.Text.Encoding.UTF8.GetString(await ReadBytesAsync(zip, "receipts.csv")).Split("\r\n");
        Assert.StartsWith("﻿Entry,Status,Date,Vendor", csv[0]);
        Assert.StartsWith("0002,Approved,2026-10-19,", csv[2]);
    }

    [Fact]
    public async Task Export_NeedsASignedInUser()
    {
        var response = await factory.CreateClient().GetAsync("/api/account/export");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithTheWrongPassword_KeepsEverything()
    {
        var (client, auth) = await SignedInAsync();
        await TestReceipts.UploadAndReadAsync(client);

        var response = await client.PostAsJsonAsync("/api/account/delete", new DeleteAccountRequest("not my password"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single((await client.GetFromJsonAsync<PagedResult<ReceiptSummaryDto>>("/api/receipts", TestReceipts.Json))!.Items);
        Assert.True(Directory.Exists(Path.Combine(factory.StoragePath, auth.User.Id.ToString())));
    }

    [Fact]
    public async Task Delete_RemovesTheAccount_EverythingInIt_AndItsFiles()
    {
        var (client, auth) = await SignedInAsync();
        await TestReceipts.ApprovedAsync(client, 12m);
        await TestReceipts.UploadAndReadAsync(client);
        await client.PostAsync("/api/reports/insight?month=2026-10", null);

        // Someone else's account must not be touched.
        var (otherClient, other) = await SignedInAsync();
        await TestReceipts.UploadAndReadAsync(otherClient);

        var response = await client.PostAsJsonAsync("/api/account/delete", new DeleteAccountRequest(TestAuth.Password));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(factory.StoragePath, auth.User.Id.ToString())));
        Assert.True(Directory.Exists(Path.Combine(factory.StoragePath, other.User.Id.ToString())));
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(auth.User.Email, TestAuth.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(auth.RefreshToken)).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = auth.User.Id;
        Assert.False(await db.Users.AnyAsync(u => u.Id == userId));
        Assert.False(await db.Receipts.AnyAsync(r => r.UserId == userId));
        Assert.False(await db.LineItems.AnyAsync(l => db.Receipts.Any(r => r.Id == l.ReceiptId && r.UserId == userId)));
        Assert.False(await db.Categories.AnyAsync(c => c.UserId == userId));
        Assert.False(await db.MonthlyInsights.AnyAsync(i => i.UserId == userId));
        Assert.False(await db.RefreshTokens.AnyAsync(t => t.UserId == userId));
        Assert.True(await db.Receipts.AnyAsync(r => r.UserId == other.User.Id));
    }

    private async Task<(HttpClient Client, AuthResponse Auth)> SignedInAsync()
    {
        var client = factory.CreateClient();
        var auth = await TestAuth.RegisterAsync(client);
        client.Authorize(auth.AccessToken);
        return (client, auth);
    }

    private Task<HttpResponseMessage> SignInAsync(string email, string password) =>
        factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password, RememberMe: false));

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(refreshToken));

    private static async Task<byte[]> ReadBytesAsync(ZipArchive zip, string path)
    {
        await using var entry = await zip.GetEntry(path)!.OpenAsync();
        using var copy = new MemoryStream();
        await entry.CopyToAsync(copy);
        return copy.ToArray();
    }
}
