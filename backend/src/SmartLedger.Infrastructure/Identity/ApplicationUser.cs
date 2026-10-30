using Microsoft.AspNetCore.Identity;

namespace SmartLedger.Infrastructure.Identity;

public class ApplicationUser : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }

    public required string BaseCurrency { get; set; }

    public bool BudgetAlerts { get; set; } = true;
}
