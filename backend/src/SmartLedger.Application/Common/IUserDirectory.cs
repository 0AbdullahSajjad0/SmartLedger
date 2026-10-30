namespace SmartLedger.Application.Common;

public interface IUserDirectory
{
    Task<string?> GetBaseCurrencyAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> WantsBudgetAlertsAsync(Guid userId, CancellationToken cancellationToken);
}
