namespace SmartLedger.Application.Common;

/// <summary>Account settings this layer needs, without depending on ASP.NET Core Identity.</summary>
public interface IUserDirectory
{
    /// <summary>Null when the user no longer exists.</summary>
    Task<string?> GetBaseCurrencyAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the user wants budget alerts by email and has confirmed the address they go to. False when the user no
    /// longer exists.
    /// </summary>
    Task<bool> WantsBudgetAlertsAsync(Guid userId, CancellationToken cancellationToken);
}
