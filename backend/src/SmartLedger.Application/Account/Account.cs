using System.ComponentModel.DataAnnotations;
using SmartLedger.Application.Auth;
using SmartLedger.Application.Common;
using SmartLedger.Domain.Entities;

namespace SmartLedger.Application.Account;

public sealed record UpdateProfileRequest([Required, StringLength(100, MinimumLength = 1)] string DisplayName);

public sealed record UpdateNotificationsRequest(bool BudgetAlerts);

/// <param name="RefreshToken">This device's refresh token: its session stays signed in, every other one ends.</param>
public sealed record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, StringLength(128, MinimumLength = 10)] string NewPassword,
    string? RefreshToken);

/// <param name="RefreshToken">This device's refresh token, so it stays signed in.</param>
public sealed record SignOutOthersRequest(string? RefreshToken);

public sealed record DeleteAccountRequest([Required] string Password);

/// <summary>Changes to the account itself. Lives with ASP.NET Core Identity in Infrastructure.</summary>
public interface IAccountService
{
    Task<Result<UserDto>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken);

    Task<Result<UserDto>> UpdateNotificationsAsync(Guid userId, UpdateNotificationsRequest request, CancellationToken cancellationToken);

    /// <summary>Checks the current password, sets the new one and signs out every other device.</summary>
    Task<Result<bool>> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Ends every session except the one <paramref name="refreshToken"/> belongs to. Other devices can still use the
    /// access token they hold until it expires, at most 15 minutes.
    /// </summary>
    Task SignOutOtherDevicesAsync(Guid userId, string? refreshToken, CancellationToken cancellationToken);

    /// <summary>After checking the password: removes the account, everything in it, and its files. Can't be undone.</summary>
    Task<Result<bool>> DeleteAsync(Guid userId, DeleteAccountRequest request, CancellationToken cancellationToken);
}

/// <param name="WriteToAsync">
/// Writes the ZIP archive to the stream. Kept apart from the checks so the API can answer an error properly before the
/// download starts, and then stream the archive without holding it in memory.
/// </param>
public sealed record AccountExport(string FileName, Func<Stream, CancellationToken, Task> WriteToAsync);

public interface IAccountExportService
{
    /// <summary>Everything the signed-in user has stored, as a ZIP: data as JSON and CSV, and the original files.</summary>
    Task<Result<AccountExport>> PrepareAsync(CancellationToken cancellationToken);
}

public sealed record AccountProfile(string Email, string DisplayName, string BaseCurrency);

/// <summary>Everything an account holds. Receipts include their line items.</summary>
public sealed record AccountData(
    AccountProfile Profile,
    IReadOnlyList<Category> Categories,
    IReadOnlyList<Receipt> Receipts,
    IReadOnlyList<MonthlyInsight> Insights);

public interface IAccountDataReader
{
    /// <summary>Null when the user no longer exists.</summary>
    Task<AccountData?> ReadAsync(Guid userId, CancellationToken cancellationToken);
}
