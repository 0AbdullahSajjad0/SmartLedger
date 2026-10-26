using Microsoft.Extensions.DependencyInjection;

namespace SmartLedger.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the application layer's services (business logic, validators).
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        return services;
    }
}
