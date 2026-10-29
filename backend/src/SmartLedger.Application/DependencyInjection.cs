using Microsoft.Extensions.DependencyInjection;
using SmartLedger.Application.Account;
using SmartLedger.Application.Categories;
using SmartLedger.Application.ExchangeRates;
using SmartLedger.Application.Overview;
using SmartLedger.Application.Reports;
using SmartLedger.Application.Receipts;
using SmartLedger.Application.Receipts.Extraction;

namespace SmartLedger.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the application layer's services (business logic, validators).
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IReceiptService, ReceiptService>();
        services.AddScoped<IReceiptExtractionService, ReceiptExtractionService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IExchangeRateService, ExchangeRateService>();
        services.AddScoped<IOverviewService, OverviewService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IAccountExportService, AccountExportService>();

        return services;
    }
}
