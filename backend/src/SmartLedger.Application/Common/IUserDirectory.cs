namespace SmartLedger.Application.Common;

/// <summary>Account settings this layer needs, without depending on ASP.NET Core Identity.</summary>
public interface IUserDirectory
{
    /// <summary>Null when the user no longer exists.</summary>
    Task<string?> GetBaseCurrencyAsync(Guid userId, CancellationToken cancellationToken);
}
