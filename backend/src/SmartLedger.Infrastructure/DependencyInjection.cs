using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SmartLedger.Application.Account;
using SmartLedger.Application.Auth;
using SmartLedger.Application.Categories;
using SmartLedger.Application.Common;
using SmartLedger.Application.Common.Storage;
using SmartLedger.Application.ExchangeRates;
using SmartLedger.Application.Receipts;
using SmartLedger.Application.Receipts.Extraction;
using SmartLedger.Application.Receipts.Previews;
using SmartLedger.Application.Reports;
using SmartLedger.Infrastructure.ExchangeRates;
using SmartLedger.Infrastructure.Extraction;
using SmartLedger.Infrastructure.Gemini;
using SmartLedger.Infrastructure.Identity;
using SmartLedger.Infrastructure.Persistence;
using SmartLedger.Infrastructure.Previews;
using SmartLedger.Infrastructure.Reports;
using SmartLedger.Infrastructure.Repositories;
using SmartLedger.Infrastructure.Storage;

namespace SmartLedger.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the infrastructure layer's services (database, repositories, AI, file storage).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        // Azure SQL serverless pauses when idle and needs up to a minute to resume;
        // retrying transient errors covers that instead of failing the first request.
        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(
            configuration.GetConnectionString("Default"),
            sql => sql.EnableRetryOnFailure()));

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

        services.AddScoped<DevelopmentDataSeeder>();
        services.AddScoped<IReceiptRepository, ReceiptRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IReportRepository, ReportRepository>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IAccountDataReader, AccountDataReader>();

        AddAuth(services, configuration);
        AddStorage(services, configuration);
        AddExtraction(services, configuration);
        AddExchangeRates(services);

        return services;
    }

    private static void AddExchangeRates(IServiceCollection services)
    {
        services.AddScoped<IExchangeRateRepository, ExchangeRateRepository>();

        services.AddHttpClient<IExchangeRateProvider, CurrencyApiExchangeRateProvider>(client =>
                client.Timeout = Timeout.InfiniteTimeSpan) // The resilience handler below owns timeouts.
            // Static files on a CDN: quick retries for blips, and an overall limit so approving never hangs.
            .AddStandardResilienceHandler(resilience =>
            {
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(25);
            });
    }

    private static void AddExtraction(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GeminiOptions>()
            .Bind(configuration.GetSection(GeminiOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<GeminiOptions>, GeminiOptionsValidation>();

        // One Gemini client, shared by the receipt reader and the monthly report writer.
        services.AddHttpClient<GeminiApi>((provider, client) =>
            {
                client.BaseAddress = provider.GetRequiredService<IOptions<GeminiOptions>>().Value.BaseUrl;
                client.Timeout = Timeout.InfiniteTimeSpan; // The resilience handler below owns timeouts.
            })
            // Retries rate limits (429), outages (5xx) and timeouts with backoff, honouring Retry-After.
            .AddStandardResilienceHandler(resilience =>
            {
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(2);
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(4);
            });
        services.AddScoped<IReceiptExtractor, GeminiReceiptExtractor>();
        services.AddScoped<IInsightWriter, GeminiInsightWriter>();

        services.AddSingleton<IReceiptPreviewRenderer, ReceiptPreviewRenderer>();

        services.AddSingleton<ReceiptExtractionQueue>();
        services.AddSingleton<IReceiptExtractionQueue>(provider => provider.GetRequiredService<ReceiptExtractionQueue>());
        services.AddHostedService<ReceiptExtractionWorker>();
    }

    private static void AddStorage(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Chosen when first used, from the validated options: Local in development and tests, AzureBlob in production.
        services.AddSingleton<IFileStorage>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<StorageOptions>>().Value;

            return options.Provider switch
            {
                StorageProvider.Local => ActivatorUtilities.CreateInstance<LocalFileStorage>(provider),
                // No connection string or key: the Container App's managed identity has the
                // "Storage Blob Data Contributor" role on the account.
                StorageProvider.AzureBlob => new BlobFileStorage(
                    new BlobServiceClient(new Uri(options.BlobServiceUri!), new DefaultAzureCredential())
                        .GetBlobContainerClient(options.ContainerName)),
                _ => throw new InvalidOperationException($"Unknown storage provider {options.Provider}."),
            };
        });
    }

    private static void AddAuth(IServiceCollection services, IConfiguration configuration)
    {
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                // Length over complexity rules (NIST SP 800-63B): long passphrases beat "Passw0rd!".
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<AppDbContext>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<TokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAccountService, AccountService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;

                // Keep claim names as issued ("sub", "email") instead of the long XML-era URIs.
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = TokenService.CreateSigningKey(jwt.SigningKey),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = JwtRegisteredClaimNames.Name,
                };
            });
    }
}
