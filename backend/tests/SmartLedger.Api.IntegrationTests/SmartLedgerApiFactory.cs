using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SmartLedger.Api.IntegrationTests;

/// <summary>
/// Runs the API in memory for tests. The startup database setup is switched off,
/// so these tests don't need SQL Server (CI runners have none).
/// </summary>
public class SmartLedgerApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:InitializeOnStartup", "false");
    }
}
