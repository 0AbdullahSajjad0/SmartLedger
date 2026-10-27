using Microsoft.Extensions.DependencyInjection;
using SmartLedger.Application.Receipts;

namespace SmartLedger.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the application layer's services (business logic, validators).
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IReceiptService, ReceiptService>();

        return services;
    }
}
