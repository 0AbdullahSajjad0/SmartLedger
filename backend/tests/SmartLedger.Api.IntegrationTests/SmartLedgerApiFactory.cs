using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartLedger.Application.Email;
using SmartLedger.Application.ExchangeRates;
using SmartLedger.Application.Receipts.Extraction;
using SmartLedger.Application.Reports;
using SmartLedger.Infrastructure.Persistence;

namespace SmartLedger.Api.IntegrationTests;

public class SmartLedgerApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("SMARTLEDGER_TEST_DB")
        ?? @"Server=(localdb)\MSSQLLocalDB;Database=SmartLedger_Tests;Trusted_Connection=True;TrustServerCertificate=True";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:InitializeOnStartup", "false");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Jwt:SigningKey", "integration-tests-only-signing-key-0123456789abcdef");
        builder.UseSetting("RateLimiting:AuthPermitLimit", "1000");
        builder.UseSetting("RateLimiting:UploadPermitLimit", "1000");
        builder.UseSetting("Storage:Provider", "Local");
        builder.UseSetting("Storage:LocalPath", StoragePath);
        builder.UseSetting("Gemini:DelayBetweenRequests", "00:00:00");
        builder.UseSetting("Email:Provider", "Local");
        builder.UseSetting("Email:AppUrl", AppUrl);
        builder.UseSetting("Email:DelayBetweenEmails", "00:00:00");

        builder.ConfigureTestServices(services =>
        {
            services.AddScoped<IReceiptExtractor, FakeReceiptExtractor>();
            services.AddSingleton<IExchangeRateProvider, FakeExchangeRateProvider>();
            services.AddScoped<IInsightWriter, FakeInsightWriter>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    public const string AppUrl = "https://app.test";

    public FakeEmailSender Emails { get; } = new();

    public string StoragePath { get; } = Path.Combine(Path.GetTempPath(), "smartledger-tests", Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        if (Directory.Exists(StoragePath))
        {
            Directory.Delete(StoragePath, recursive: true);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<SmartLedgerApiFactory>
{
    public const string Name = "Api";
}
