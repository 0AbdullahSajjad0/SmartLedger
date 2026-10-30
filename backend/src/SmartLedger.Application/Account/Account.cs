using System.ComponentModel.DataAnnotations;
using SmartLedger.Application.Auth;
using SmartLedger.Application.Common;
using SmartLedger.Domain.Entities;

namespace SmartLedger.Application.Account;

public sealed record UpdateProfileRequest([Required, StringLength(100, MinimumLength = 1)] string DisplayName);

public sealed record UpdateNotificationsRequest(bool BudgetAlerts);

public sealed record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, StringLength(128, MinimumLength = 10)] string NewPassword,
    string? RefreshToken);

public sealed record SignOutOthersRequest(string? RefreshToken);

public sealed record DeleteAccountRequest([Required] string Password);

public interface IAccountService
{
    Task<Result<UserDto>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken);

    Task<Result<UserDto>> UpdateNotificationsAsync(Guid userId, UpdateNotificationsRequest request, CancellationToken cancellationToken);

    Task<Result<bool>> SendConfirmationEmailAsync(Guid userId, CancellationToken cancellationToken);

    Task<Result<bool>> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken);

    Task SignOutOtherDevicesAsync(Guid userId, string? refreshToken, CancellationToken cancellationToken);

    Task<Result<bool>> DeleteAsync(Guid userId, DeleteAccountRequest request, CancellationToken cancellationToken);
}

public sealed record AccountExport(string FileName, Func<Stream, CancellationToken, Task> WriteToAsync);

public interface IAccountExportService
{
    Task<Result<AccountExport>> PrepareAsync(CancellationToken cancellationToken);
}

public sealed record AccountProfile(string Email, string DisplayName, string BaseCurrency);

public sealed record AccountData(
    AccountProfile Profile,
    IReadOnlyList<Category> Categories,
    IReadOnlyList<CategoryBudget> Budgets,
    IReadOnlyList<Receipt> Receipts,
    IReadOnlyList<MonthlyInsight> Insights);

public interface IAccountDataReader
{
    Task<AccountData?> ReadAsync(Guid userId, CancellationToken cancellationToken);
}
