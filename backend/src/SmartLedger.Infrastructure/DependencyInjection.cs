using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SmartLedger.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the infrastructure layer's services (database, repositories, AI, file storage).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}
