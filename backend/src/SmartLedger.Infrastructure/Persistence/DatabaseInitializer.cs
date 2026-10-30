using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SmartLedger.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    public static async Task InitializeDevelopmentDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        var seeder = scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}
