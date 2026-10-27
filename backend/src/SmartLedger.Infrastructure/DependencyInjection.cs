using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartLedger.Infrastructure.Identity;
using SmartLedger.Infrastructure.Persistence;

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

        services.AddIdentityCore<ApplicationUser>()
            .AddEntityFrameworkStores<AppDbContext>();

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

        services.AddScoped<DevelopmentDataSeeder>();

        return services;
    }
}
