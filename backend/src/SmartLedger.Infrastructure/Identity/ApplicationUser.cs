using Microsoft.AspNetCore.Identity;

namespace SmartLedger.Infrastructure.Identity;

/// <summary>
/// The signed-in account. Lives in Infrastructure because it depends on ASP.NET Core Identity;
/// domain entities refer to it only by <c>UserId</c>.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }

    /// <summary>ISO 4217 code that every approved receipt is converted to, e.g. "USD".</summary>
    public required string BaseCurrency { get; set; }

    /// <summary>Email when a category reaches 80% and 100% of its monthly budget. On unless the user turns it off.</summary>
    public bool BudgetAlerts { get; set; } = true;
}
